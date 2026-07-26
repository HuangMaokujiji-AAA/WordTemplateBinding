using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using C = DocumentFormat.OpenXml.Drawing.Charts;

namespace WordTemplateBinding.Infrastructure.OpenXml.Segments;

#pragma warning disable CS1591

public sealed partial class OpenXmlTemplateSegmentScanner : IWordTemplateSegmentScanner
{
    private const string TagPrefix = "wtb:segment:";

    public Task<TemplateSegmentScanResult> ScanAsync(
        Stream seekableDocxStream,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!seekableDocxStream.CanSeek)
        {
            throw new ArgumentException("片段扫描需要可定位的 DOCX 流。", nameof(seekableDocxStream));
        }

        seekableDocxStream.Position = 0;
        using WordprocessingDocument document =
            WordprocessingDocument.Open(seekableDocxStream, false);
        Body body = document.MainDocumentPart?.Document.Body
            ?? throw new InvalidDataException("DOCX 缺少主文档正文。");

        List<OpenXmlElement> orderedNodes = body.Descendants().ToList();
        Dictionary<OpenXmlElement, int> nodeOrders = orderedNodes
            .Select((node, index) => (node, index))
            .ToDictionary(item => item.node, item => item.index);
        Dictionary<Paragraph, int> paragraphIndexes = body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<TextBoxContent>().Any())
            .Select((paragraph, index) => (paragraph, index))
            .ToDictionary(item => item.paragraph, item => item.index);
        Dictionary<Paragraph, string> textBoxLocatorKeys = new();
        List<TextBoxContent> textBoxes = body.Descendants<TextBoxContent>().ToList();
        for (int textBoxIndex = 0; textBoxIndex < textBoxes.Count; textBoxIndex++)
        {
            List<Paragraph> textBoxParagraphs = textBoxes[textBoxIndex]
                .Descendants<Paragraph>()
                .Where(paragraph => ReferenceEquals(
                    paragraph.Ancestors<TextBoxContent>().FirstOrDefault(),
                    textBoxes[textBoxIndex]))
                .ToList();
            for (int paragraphIndex = 0;
                 paragraphIndex < textBoxParagraphs.Count;
                 paragraphIndex++)
            {
                textBoxLocatorKeys[textBoxParagraphs[paragraphIndex]] =
                    $"/word/document.xml#textbox:{textBoxIndex}:{paragraphIndex}";
            }
        }

        List<TemplateSegmentDiagnostic> diagnostics = new();
        List<(SdtBlock Block, string Key)> candidates = new();
        foreach (SdtBlock block in body.Descendants<SdtBlock>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? tag = GetTag(block);
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(TagPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string key = tag[TagPrefix.Length..];
            if (!SegmentKeyRegex().IsMatch(key))
            {
                diagnostics.Add(new TemplateSegmentDiagnostic(
                    "SEGMENT_TAG_INVALID",
                    "ERROR",
                    $"片段 Tag“{tag}”不符合 wtb:segment:{{小写字母、数字、短横线}} 协议。",
                    key));
                continue;
            }

            candidates.Add((block, key));
        }

        HashSet<string> duplicated = candidates
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string key in duplicated)
        {
            diagnostics.Add(new TemplateSegmentDiagnostic(
                "SEGMENT_TAG_DUPLICATED",
                "ERROR",
                $"模板版本内存在重复片段键“{key}”。",
                key));
        }

        List<TemplateSegmentDefinition> result = new();
        int sortNo = 0;
        foreach ((SdtBlock block, string key) in candidates
                     .Where(item => !duplicated.Contains(item.Key)))
        {
            string? alias = block.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
            string name = string.IsNullOrWhiteSpace(alias)
                ? block.Descendants<Paragraph>()
                    .Select(paragraph => paragraph.InnerText.Trim())
                    .FirstOrDefault(text => text.Length > 0) ?? key
                : alias.Trim();
            if (string.IsNullOrWhiteSpace(alias))
            {
                diagnostics.Add(new TemplateSegmentDiagnostic(
                    "SEGMENT_ALIAS_MISSING",
                    "WARNING",
                    $"片段“{key}”没有 Alias，已使用首个非空段落或片段键作为名称。",
                    key));
            }

            string? parentKey = block.Ancestors<SdtBlock>()
                .Select(GetTag)
                .Where(tag => tag?.StartsWith(TagPrefix, StringComparison.Ordinal) == true)
                .Select(tag => tag![TagPrefix.Length..])
                .FirstOrDefault(parent => SegmentKeyRegex().IsMatch(parent) &&
                                          !duplicated.Contains(parent));
            HashSet<int> paragraphSet = block.Descendants<Paragraph>()
                .Where(paragraphIndexes.ContainsKey)
                .Select(paragraph => paragraphIndexes[paragraph])
                .ToHashSet();
            HashSet<string> chartIds = block.Descendants<C.ChartReference>()
                .Select(reference => reference.Id?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> textBoxKeys = block.Descendants<Paragraph>()
                .Where(textBoxLocatorKeys.ContainsKey)
                .Select(paragraph => textBoxLocatorKeys[paragraph])
                .ToHashSet(StringComparer.Ordinal);
            uint start = checked((uint)nodeOrders[block]);
            uint end = checked((uint)block.Descendants()
                .Where(nodeOrders.ContainsKey)
                .Select(node => nodeOrders[node])
                .DefaultIfEmpty(nodeOrders[block])
                .Max());
            if (paragraphSet.Count == 0 && chartIds.Count == 0)
            {
                diagnostics.Add(new TemplateSegmentDiagnostic(
                    "SEGMENT_EMPTY",
                    "WARNING",
                    $"片段“{key}”没有正文段落或图表。",
                    key));
            }

            string fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(block.OuterXml)))
                .ToLowerInvariant();
            result.Add(new TemplateSegmentDefinition
            {
                SegmentKey = key,
                SegmentName = name,
                ParentSegmentKey = parentKey,
                SegmentType = "SECTION",
                AnchorType = "CONTENT_CONTROL",
                StartAnchorJson = JsonSerializer.Serialize(new
                {
                    partKind = "MainDocument",
                    partKey = "/word/document.xml",
                    locatorType = "CONTENT_CONTROL",
                    tag = $"{TagPrefix}{key}",
                    sdtId = block.SdtProperties?.GetFirstChild<SdtId>()?.Val?.Value,
                    contextHash = fingerprint,
                }),
                EndAnchorJson = null,
                DocumentOrderStart = start,
                DocumentOrderEnd = end,
                SegmentStatus = diagnostics.Any(item =>
                    item.SegmentKey == key && item.Level == "WARNING")
                    ? "READY_WITH_WARNINGS"
                    : "READY",
                SegmentFingerprint = fingerprint,
                SortNo = sortNo++,
                MainDocumentParagraphIndexes = paragraphSet,
                MainDocumentChartRelationshipIds = chartIds,
                TextBoxLocatorKeys = textBoxKeys,
                Depth = block.Ancestors<SdtBlock>().Count(),
            });
        }

        if (result.Count == 0)
        {
            string fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(body.OuterXml)))
                .ToLowerInvariant();
            result.Add(new TemplateSegmentDefinition
            {
                SegmentKey = "full-document",
                SegmentName = "完整模板",
                SegmentType = "ROOT",
                AnchorType = "VIRTUAL",
                StartAnchorJson = """{"partKind":"MainDocument","partKey":"/word/document.xml","locatorType":"VIRTUAL"}""",
                DocumentOrderStart = 0,
                DocumentOrderEnd = checked((uint)Math.Max(0, orderedNodes.Count - 1)),
                SegmentStatus = diagnostics.Count == 0
                    ? "READY"
                    : "READY_WITH_WARNINGS",
                SegmentFingerprint = fingerprint,
                SortNo = 0,
                MainDocumentParagraphIndexes = paragraphIndexes.Values.ToHashSet(),
                MainDocumentChartRelationshipIds = body.Descendants<C.ChartReference>()
                    .Select(reference => reference.Id?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Cast<string>()
                    .ToHashSet(StringComparer.Ordinal),
                TextBoxLocatorKeys = textBoxLocatorKeys.Values
                    .ToHashSet(StringComparer.Ordinal),
            });
        }

        return Task.FromResult(new TemplateSegmentScanResult
        {
            Segments = result.AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
        });
    }

    private static string? GetTag(SdtBlock block) =>
        block.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentKeyRegex();
}

#pragma warning restore CS1591
