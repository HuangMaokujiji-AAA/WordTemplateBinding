using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Cloning;

/// <summary>
/// 克隆 ChartPart 及其关联的嵌入工作簿和图片，为每个重复实例创建独立的图表部件。
/// </summary>
public sealed class OpenXmlChartPartCloner
{
    /// <summary>
    /// 为克隆元素中的所有图表创建独立副本。
    /// </summary>
    /// <param name="sourceMainPart">源主文档部件。</param>
    /// <param name="targetMainPart">目标主文档部件。</param>
    /// <param name="clonedRoot">已克隆的根元素。</param>
    /// <param name="instanceKey">实例键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回图表克隆结果。</returns>
    public async Task<ChartCloneResult> CloneForElementAsync(
        MainDocumentPart sourceMainPart,
        MainDocumentPart targetMainPart,
        OpenXmlElement clonedRoot,
        string instanceKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceMainPart);
        ArgumentNullException.ThrowIfNull(targetMainPart);
        ArgumentNullException.ThrowIfNull(clonedRoot);

        List<ClonedChartInfo> clonedCharts = new();

        // Find all chart references in the cloned content
        IEnumerable<DocumentFormat.OpenXml.Drawing.Charts.ChartReference> chartRefs =
            clonedRoot.Descendants<DocumentFormat.OpenXml.Drawing.Charts.ChartReference>();

        foreach (DocumentFormat.OpenXml.Drawing.Charts.ChartReference chartRef in chartRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? oldRelId = chartRef.Id?.Value;
            if (string.IsNullOrEmpty(oldRelId))
            {
                continue;
            }

            // Get the source ChartPart from the source document
            OpenXmlPart? sourcePart = sourceMainPart.GetPartById(oldRelId);
            if (sourcePart is not ChartPart sourceChartPart)
            {
                continue;
            }

            // Clone the ChartPart
            ChartPart newChartPart = targetMainPart.AddNewPart<ChartPart>();
            await CloneChartPartContentAsync(sourceChartPart, newChartPart, cancellationToken);

            // Clone embedded workbook if present
            EmbeddedPackagePart? newWorkbookPart = null;
            foreach (IdPartPair pair in sourceChartPart.Parts)
            {
                if (pair.OpenXmlPart is EmbeddedPackagePart sourceWorkbook)
                {
                    newWorkbookPart = newChartPart.AddNewPart<EmbeddedPackagePart>(
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        ".xlsx");

                    await CloneEmbeddedPackagePartAsync(
                        sourceWorkbook, newWorkbookPart, cancellationToken);
                    break;
                }
            }

            // Clone any image parts referenced by the chart
            Dictionary<string, string> relIdMap = new(StringComparer.Ordinal);
            foreach (IdPartPair pair in sourceChartPart.Parts.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pair.OpenXmlPart is ImagePart sourceImagePart)
                {
                    ImagePart newImagePart = newChartPart.AddImagePart(
                        sourceImagePart.ContentType);
                    await CloneImagePartAsync(
                        sourceImagePart, newImagePart, cancellationToken);

                    // Map old relationship ID to new
                    string? sourceRelId = sourceChartPart.GetIdOfPart(sourceImagePart);
                    string? targetRelId = newChartPart.GetIdOfPart(newImagePart);
                    if (sourceRelId is not null && targetRelId is not null)
                    {
                        relIdMap[sourceRelId] = targetRelId;
                    }
                }
            }

            // Update relationship IDs in the cloned chart XML
            if (relIdMap.Count > 0)
            {
                UpdateChartRelationships(newChartPart, relIdMap);
            }

            // Get new relationship ID from target MainDocumentPart to new ChartPart
            string newRelId = targetMainPart.GetIdOfPart(newChartPart);

            // Update the chart reference in the cloned content
            chartRef.Id = newRelId;

            clonedCharts.Add(new ClonedChartInfo
            {
                SourceRelationshipId = oldRelId,
                TargetRelationshipId = newRelId,
                TargetChartPart = newChartPart,
                TargetWorkbookPart = newWorkbookPart,
                InstanceKey = instanceKey,
            });
        }

        return new ChartCloneResult
        {
            Root = clonedRoot,
            Charts = clonedCharts,
        };
    }

    /// <summary>
    /// 克隆 ChartPart 的 XML 内容。
    /// </summary>
    private static async Task CloneChartPartContentAsync(
        ChartPart source,
        ChartPart target,
        CancellationToken cancellationToken)
    {
        using Stream sourceStream = source.GetStream(FileMode.Open, FileAccess.Read);
        using Stream targetStream = target.GetStream(FileMode.Create, FileAccess.Write);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
    }

    /// <summary>
    /// 克隆嵌入工作簿部件。
    /// </summary>
    private static async Task CloneEmbeddedPackagePartAsync(
        EmbeddedPackagePart source,
        EmbeddedPackagePart target,
        CancellationToken cancellationToken)
    {
        using Stream sourceStream = source.GetStream(FileMode.Open, FileAccess.Read);
        using Stream targetStream = target.GetStream(FileMode.Create, FileAccess.Write);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
    }

    /// <summary>
    /// 克隆图片部件。
    /// </summary>
    private static async Task CloneImagePartAsync(
        ImagePart source,
        ImagePart target,
        CancellationToken cancellationToken)
    {
        using Stream sourceStream = source.GetStream(FileMode.Open, FileAccess.Read);
        using Stream targetStream = target.GetStream(FileMode.Create, FileAccess.Write);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
    }

    /// <summary>
    /// 更新图表 XML 中的关系引用。
    /// </summary>
    private static void UpdateChartRelationships(
        ChartPart chartPart,
        IReadOnlyDictionary<string, string> relIdMap)
    {
        // The chart XML may reference embedded data or images via relationship IDs.
        // After cloning, we need to update these references.
        using Stream stream = chartPart.GetStream(FileMode.Open, FileAccess.ReadWrite);
        System.Xml.Linq.XDocument chartDoc = System.Xml.Linq.XDocument.Load(stream);

        bool modified = false;
        foreach (System.Xml.Linq.XElement element in chartDoc.Descendants())
        {
            foreach (System.Xml.Linq.XAttribute attr in element.Attributes())
            {
                if (attr.Name.LocalName.Equals("embed", StringComparison.Ordinal) ||
                    attr.Name.LocalName.Equals("link", StringComparison.Ordinal))
                {
                    if (relIdMap.TryGetValue(attr.Value, out string? newRelId))
                    {
                        attr.Value = newRelId;
                        modified = true;
                    }
                }
            }
        }

        if (modified)
        {
            stream.SetLength(0);
            stream.Position = 0;
            chartDoc.Save(stream);
        }
    }
}

/// <summary>
/// 表示图表克隆操作的结果。
/// </summary>
public sealed record ChartCloneResult
{
    /// <summary>
    /// 获取克隆后的根元素。
    /// </summary>
    public required OpenXmlElement Root { get; init; }

    /// <summary>
    /// 获取克隆的图表信息列表。
    /// </summary>
    public required IReadOnlyList<ClonedChartInfo> Charts { get; init; }
}

/// <summary>
/// 表示一个克隆图表的信息。
/// </summary>
public sealed record ClonedChartInfo
{
    /// <summary>
    /// 获取源关系 ID。
    /// </summary>
    public required string SourceRelationshipId { get; init; }

    /// <summary>
    /// 获取目标关系 ID。
    /// </summary>
    public required string TargetRelationshipId { get; init; }

    /// <summary>
    /// 获取目标 ChartPart。
    /// </summary>
    public required ChartPart TargetChartPart { get; init; }

    /// <summary>
    /// 获取目标嵌入工作簿部件。
    /// </summary>
    public EmbeddedPackagePart? TargetWorkbookPart { get; init; }

    /// <summary>
    /// 获取实例键。
    /// </summary>
    public required string InstanceKey { get; init; }
}
