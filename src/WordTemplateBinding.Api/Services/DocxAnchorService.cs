using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WordTemplateBinding.Api.Services;

/// <summary>
/// 在 DOCX 中插入稳定的 Word 书签，作为绑定定位的锚点。
/// 书签是隐藏的 Open XML 节点，不会改变分页或样式。
/// </summary>
public sealed class DocxAnchorService
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly ILogger<DocxAnchorService> _logger;

    /// <summary>
    /// 构造 <see cref="DocxAnchorService"/>。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public DocxAnchorService(ILogger<DocxAnchorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 在 DOCX 中添加书签锚点。
    /// </summary>
    /// <param name="docxBytes">原始 DOCX 字节数组</param>
    /// <param name="anchors">锚点信息列表</param>
    /// <returns>添加了锚点的 DOCX 字节数组</returns>
    public byte[] AddAnchors(byte[] docxBytes, IReadOnlyList<AnchorInfo> anchors)
    {
        _logger.LogInformation("开始为 DOCX 添加 {Count} 个书签锚点", anchors.Count);

        AssignAnchorNames(anchors);

        using var output = new MemoryStream();
        output.Write(docxBytes);
        output.Position = 0;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var bookmarkId = GetNextBookmarkId(archive);

            foreach (var anchorGroup in anchors.GroupBy(x => x.PartName))
            {
                var document = LoadXml(archive, anchorGroup.Key);
                if (document is null) continue;

                var paragraphs = document.Descendants(W + "p").ToList();
                var tables = document.Descendants(W + "tbl").ToList();

                foreach (var anchor in anchorGroup)
                {
                    switch (anchor.AnchorType)
                    {
                        case AnchorType.Placeholder:
                            AddPlaceholderAnchor(document, paragraphs, anchor, bookmarkId++);
                            break;
                        case AnchorType.Table:
                            AddTableAnchor(document, tables, anchor, bookmarkId++);
                            break;
                        case AnchorType.Chart:
                            AddChartAnchor(document, paragraphs, anchor, bookmarkId++);
                            break;
                    }
                }

                WriteXml(archive, anchorGroup.Key, document);
            }
        }

        _logger.LogInformation("书签锚点添加完成");
        return output.ToArray();
    }

    /// <summary>
    /// 为锚点分配唯一的书签名称。
    /// </summary>
    public void AssignAnchorNames(IReadOnlyList<AnchorInfo> anchors)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var markerCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in anchors.OrderBy(x => x.DocumentOrder))
        {
            var markerName = ExtractMarkerName(anchor);

            if (!markerCounters.TryGetValue(markerName, out var count))
                count = 0;
            markerCounters[markerName] = count + 1;

            var baseName = count == 0
                ? $"bm_{markerName}"
                : $"bm_{markerName}_{count + 1}";

            anchor.AnchorName = MakeUnique(used, baseName);
        }
    }

    private static string ExtractMarkerName(AnchorInfo anchor)
    {
        var inner = anchor.DisplayName ?? anchor.TargetId ?? "unnamed";

        inner = Regex.Replace(inner.Trim(), @"[^A-Za-z0-9_\u4e00-\u9fff]+", "_");
        inner = inner.Trim('_', '-');

        return string.IsNullOrWhiteSpace(inner) ? "unnamed" : inner;
    }

    private static string MakeUnique(ISet<string> used, string baseName)
    {
        var candidate = baseName;
        var index = 2;
        while (!used.Add(candidate)) candidate = $"{baseName}_{index++}";
        return candidate;
    }

    private void AddPlaceholderAnchor(
        XDocument document,
        List<XElement> paragraphs,
        AnchorInfo anchor,
        int bookmarkId)
    {
        if (!anchor.ParagraphIndex.HasValue || anchor.ParagraphIndex < 0 || anchor.ParagraphIndex >= paragraphs.Count)
        {
            _logger.LogWarning("段落索引无效: {Index}", anchor.ParagraphIndex);
            return;
        }

        var paragraph = paragraphs[anchor.ParagraphIndex.Value];

        if (anchor.StartOffset.HasValue && anchor.Length.HasValue)
        {
            AddRangeBookmark(paragraph, anchor, bookmarkId);
        }
        else
        {
            AddPointBookmark(paragraph, anchor.AnchorName!, bookmarkId);
        }
    }

    private void AddTableAnchor(
        XDocument document,
        List<XElement> tables,
        AnchorInfo anchor,
        int bookmarkId)
    {
        if (!anchor.TableIndex.HasValue || anchor.TableIndex < 0 || anchor.TableIndex >= tables.Count)
        {
            _logger.LogWarning("表格索引无效: {Index}", anchor.TableIndex);
            return;
        }

        var table = tables[anchor.TableIndex.Value];
        var paragraph = table.Descendants(W + "p").FirstOrDefault();
        if (paragraph is not null)
        {
            AddPointBookmark(paragraph, anchor.AnchorName!, bookmarkId);
        }
    }

    private void AddChartAnchor(
        XDocument document,
        List<XElement> paragraphs,
        AnchorInfo anchor,
        int bookmarkId)
    {
        if (!anchor.ParagraphIndex.HasValue || anchor.ParagraphIndex < 0 || anchor.ParagraphIndex >= paragraphs.Count)
        {
            _logger.LogWarning("图表段落索引无效: {Index}", anchor.ParagraphIndex);
            return;
        }

        var paragraph = paragraphs[anchor.ParagraphIndex.Value];
        AddPointBookmark(paragraph, anchor.AnchorName!, bookmarkId);
    }

    private void AddRangeBookmark(XElement paragraph, AnchorInfo anchor, int bookmarkId)
    {
        if (string.IsNullOrEmpty(anchor.AnchorName)) return;

        if (paragraph.Descendants(W + "bookmarkStart")
            .Any(x => string.Equals(x.Attribute(W + "name")?.Value, anchor.AnchorName, StringComparison.OrdinalIgnoreCase)))
            return;

        var textNodes = paragraph.Descendants(W + "t").ToList();
        var cursor = 0;
        var overlappingRuns = new List<XElement>();

        var start = anchor.StartOffset ?? 0;
        var length = anchor.Length ?? 0;
        var end = start + length;

        foreach (var textNode in textNodes)
        {
            var nodeStart = cursor;
            var nodeEnd = nodeStart + textNode.Value.Length;
            cursor = nodeEnd;

            if (nodeEnd <= start || nodeStart >= end) continue;

            var run = textNode.Ancestors(W + "r").FirstOrDefault();
            if (run is not null && !overlappingRuns.Contains(run))
                overlappingRuns.Add(run);
        }

        if (overlappingRuns.Count == 0)
        {
            AddPointBookmark(paragraph, anchor.AnchorName, bookmarkId);
            return;
        }

        var startNode = new XElement(
            W + "bookmarkStart",
            new XAttribute(W + "id", bookmarkId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(W + "name", anchor.AnchorName));
        var endNode = new XElement(
            W + "bookmarkEnd",
            new XAttribute(W + "id", bookmarkId.ToString(CultureInfo.InvariantCulture)));

        overlappingRuns[0].AddBeforeSelf(startNode);
        overlappingRuns[^1].AddAfterSelf(endNode);
    }

    private static void AddPointBookmark(XElement paragraph, string name, int bookmarkId)
    {
        if (paragraph.Descendants(W + "bookmarkStart")
            .Any(x => string.Equals(x.Attribute(W + "name")?.Value, name, StringComparison.OrdinalIgnoreCase)))
            return;

        var startNode = new XElement(
            W + "bookmarkStart",
            new XAttribute(W + "id", bookmarkId.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(W + "name", name));
        var endNode = new XElement(
            W + "bookmarkEnd",
            new XAttribute(W + "id", bookmarkId.ToString(CultureInfo.InvariantCulture)));

        var firstRun = paragraph.Elements(W + "r").FirstOrDefault();
        if (firstRun is null)
        {
            paragraph.Add(startNode, endNode);
        }
        else
        {
            firstRun.AddBeforeSelf(startNode);
            startNode.AddAfterSelf(endNode);
        }
    }

    private static int GetNextBookmarkId(ZipArchive archive)
    {
        var max = 0;
        foreach (var entry in archive.Entries.Where(x =>
                     x.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                     x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var xml = LoadXml(archive, entry.FullName);
            if (xml is null) continue;
            foreach (var node in xml.Descendants(W + "bookmarkStart"))
                if (int.TryParse(node.Attribute(W + "id")?.Value, out var id)) max = Math.Max(max, id);
        }
        return max + 1;
    }

    private static XDocument? LoadXml(ZipArchive archive, string partName)
    {
        var entry = archive.GetEntry(partName);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void WriteXml(ZipArchive archive, string partName, XDocument document)
    {
        using var buffer = new MemoryStream();
        document.Save(buffer, SaveOptions.DisableFormatting);
        archive.GetEntry(partName)?.Delete();
        var entry = archive.CreateEntry(partName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(buffer.ToArray());
    }
}

/// <summary>
/// 锚点类型
/// </summary>
public enum AnchorType
{
    /// <summary>普通占位符锚点。</summary>
    Placeholder,

    /// <summary>表格锚点。</summary>
    Table,

    /// <summary>图表锚点。</summary>
    Chart
}

/// <summary>
/// 锚点信息
/// </summary>
public class AnchorInfo
{
    /// <summary>目标标识，对应模板中可绑定的对象 ID。</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>展示用名称，便于阅读。</summary>
    public string? DisplayName { get; set; }

    /// <summary>锚点种类。</summary>
    public AnchorType AnchorType { get; set; } = AnchorType.Placeholder;

    /// <summary>DOCX 内部部件路径，例如 <c>word/document.xml</c>。</summary>
    public string PartName { get; set; } = "word/document.xml";

    /// <summary>文档顺序（用于命名时的稳定排序）。</summary>
    public int DocumentOrder { get; set; }

    /// <summary>段落索引（仅对 <see cref="AnchorType.Placeholder"/> 与 <see cref="AnchorType.Chart"/> 有效）。</summary>
    public int? ParagraphIndex { get; set; }

    /// <summary>表格索引（仅对 <see cref="AnchorType.Table"/> 有效）。</summary>
    public int? TableIndex { get; set; }

    /// <summary>选区起始偏移（基于段落纯文本的字符位置）。</summary>
    public int? StartOffset { get; set; }

    /// <summary>选区长度（字符数）。</summary>
    public int? Length { get; set; }

    /// <summary>最终生成的书签名称，由 <see cref="DocxAnchorService"/> 赋值。</summary>
    public string? AnchorName { get; set; }

    /// <summary>已绑定的数据路径（例如 <c>school.name</c>）。</summary>
    public string? BoundDataPath { get; set; }
}
