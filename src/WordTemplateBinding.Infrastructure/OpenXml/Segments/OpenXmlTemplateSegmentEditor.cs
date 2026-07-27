using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Segments;

#pragma warning disable CS1591

public sealed partial class OpenXmlTemplateSegmentEditor : IWordTemplateSegmentEditor
{
    private const string SegmentTagPrefix = "wtb:segment:";

    public Task<IReadOnlyList<TemplateOutlineBlock>> ReadOutlineAsync(
        string sourceDocxPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using WordprocessingDocument document =
            WordprocessingDocument.Open(sourceDocxPath, false);
        Body body = GetBody(document);
        return Task.FromResult<IReadOnlyList<TemplateOutlineBlock>>(
            BuildOutline(body, "body", 0, cancellationToken)
                .AsReadOnly());
    }

    public Task<Stream> InsertBoundaryAsync(
        string sourceDocxPath,
        InsertTemplateSegmentBoundaryRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        return EditCopyAsync(sourceDocxPath, document =>
        {
            Body body = GetBody(document);
            if (body.Descendants<SdtBlock>().Any(block =>
                    string.Equals(
                        GetSegmentKey(block),
                        request.SegmentKey,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"片段键“{request.SegmentKey}”已存在。");
            }

            (string startParentPath, int startIndex) = ParseBlockId(
                request.StartBlockId);
            (string endParentPath, int endIndex) = ParseBlockId(
                request.EndBlockId);
            if (!string.Equals(
                    startParentPath,
                    endParentPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("片段起点和终点必须位于同一层级。");
            }

            OpenXmlCompositeElement parent =
                ResolveContainer(body, startParentPath);
            if (startIndex > endIndex)
            {
                throw new InvalidDataException("片段起点必须位于终点之前。");
            }

            IReadOnlyList<OpenXmlElement> children = parent.ChildElements.ToList();
            if (startIndex < 0 || endIndex >= children.Count)
            {
                throw new InvalidDataException("片段边界已失效，请刷新结构树后重试。");
            }

            List<OpenXmlElement> selected = children
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .ToList();
            if (selected.Count == 0 || selected.Any(element => !CanSelect(element)))
            {
                throw new InvalidDataException(
                    "片段只能包含完整段落、表格或块级内容控件。");
            }

            SdtContentBlock content = new();
            foreach (OpenXmlElement element in selected)
            {
                element.Remove();
                content.Append(element);
            }

            SdtBlock wrapper = new(
                new SdtProperties(
                    new SdtAlias { Val = request.SegmentName.Trim() },
                    new Tag { Val = $"{SegmentTagPrefix}{request.SegmentKey}" },
                    new SdtId { Val = Random.Shared.Next(1, int.MaxValue) }),
                content);
            parent.InsertAt(wrapper, startIndex);
            document.MainDocumentPart!.Document.Save();
        }, cancellationToken);
    }

    public Task<Stream> RemoveBoundaryAsync(
        string sourceDocxPath,
        string segmentKey,
        CancellationToken cancellationToken = default)
    {
        if (!SegmentKeyRegex().IsMatch(segmentKey))
        {
            throw new InvalidDataException("片段键格式无效。");
        }

        return EditCopyAsync(sourceDocxPath, document =>
        {
            Body body = GetBody(document);
            List<SdtBlock> matches = body.Descendants<SdtBlock>()
                .Where(block => string.Equals(
                    GetSegmentKey(block),
                    segmentKey,
                    StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
            {
                throw new InvalidDataException($"找不到片段边界“{segmentKey}”。");
            }

            if (matches.Count > 1)
            {
                throw new InvalidDataException(
                    $"片段边界“{segmentKey}”不唯一，不能安全删除。");
            }

            SdtBlock target = matches[0];
            SdtContentBlock content = target.SdtContentBlock
                ?? throw new InvalidDataException("片段内容控件缺少正文容器。");
            OpenXmlCompositeElement parent =
                target.Parent as OpenXmlCompositeElement
                ?? throw new InvalidDataException("片段内容控件缺少父容器。");
            int targetIndex = parent.ChildElements.ToList().IndexOf(target);
            List<OpenXmlElement> preserved = content.ChildElements.ToList();
            foreach (OpenXmlElement element in preserved)
            {
                element.Remove();
            }

            target.Remove();
            for (int index = 0; index < preserved.Count; index++)
            {
                parent.InsertAt(preserved[index], targetIndex + index);
            }

            document.MainDocumentPart!.Document.Save();
        }, cancellationToken);
    }

    private static List<TemplateOutlineBlock> BuildOutline(
        OpenXmlCompositeElement container,
        string containerPath,
        int depth,
        CancellationToken cancellationToken)
    {
        List<TemplateOutlineBlock> result = new();
        IReadOnlyList<OpenXmlElement> children = container.ChildElements.ToList();
        for (int index = 0; index < children.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenXmlElement child = children[index];
            if (!CanSelect(child))
            {
                continue;
            }

            string blockId = $"{containerPath}/{index}";
            string? segmentKey = child is SdtBlock block
                ? GetSegmentKey(block)
                : null;
            IReadOnlyList<TemplateOutlineBlock> nested =
                child is SdtBlock nestedBlock &&
                nestedBlock.SdtContentBlock is not null
                    ? BuildOutline(
                        nestedBlock.SdtContentBlock,
                        $"{blockId}/content",
                        depth + 1,
                        cancellationToken)
                    : Array.Empty<TemplateOutlineBlock>();
            result.Add(new TemplateOutlineBlock
            {
                BlockId = blockId,
                BlockType = child switch
                {
                    Paragraph => "PARAGRAPH",
                    Table => "TABLE",
                    SdtBlock when segmentKey is not null => "SEGMENT",
                    SdtBlock => "CONTENT_CONTROL",
                    _ => "OTHER",
                },
                DisplayText = Describe(child, segmentKey),
                SegmentKey = segmentKey,
                CanSelect = true,
                Depth = depth,
                Children = nested,
            });
        }

        return result;
    }

    private static string Describe(OpenXmlElement element, string? segmentKey)
    {
        string text = element.InnerText
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (text.Length > 100)
        {
            text = $"{text[..100]}…";
        }

        return element switch
        {
            SdtBlock when segmentKey is not null =>
                $"片段 {segmentKey} · {Fallback(text, "空片段")}",
            SdtBlock => $"内容控件 · {Fallback(text, "空内容控件")}",
            Table => $"表格 · {Fallback(text, "无文本")}",
            Paragraph when element.Descendants<
                DocumentFormat.OpenXml.Drawing.Charts.ChartReference>().Any() =>
                $"图表段落 · {Fallback(text, "无标题")}",
            Paragraph => Fallback(text, "空段落"),
            _ => Fallback(text, element.LocalName),
        };
    }

    private static string Fallback(string value, string fallback) =>
        value.Length == 0 ? fallback : value;

    private static bool CanSelect(OpenXmlElement element) =>
        element is Paragraph or Table or SdtBlock;

    private static (string ParentPath, int Index) ParseBlockId(string blockId)
    {
        int slash = blockId.LastIndexOf('/');
        if (slash <= 0 ||
            !int.TryParse(blockId[(slash + 1)..], out int index) ||
            index < 0)
        {
            throw new InvalidDataException("块级边界 ID 格式无效。");
        }

        return (blockId[..slash], index);
    }

    private static OpenXmlCompositeElement ResolveContainer(
        Body body,
        string path)
    {
        string[] tokens = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 ||
            !string.Equals(tokens[0], "body", StringComparison.Ordinal))
        {
            throw new InvalidDataException("块级边界不属于主文档正文。");
        }

        OpenXmlCompositeElement current = body;
        for (int tokenIndex = 1; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];
            if (string.Equals(token, "content", StringComparison.Ordinal))
            {
                if (current is not SdtBlock block ||
                    block.SdtContentBlock is null)
                {
                    throw new InvalidDataException("块级边界的内容控件路径无效。");
                }

                current = block.SdtContentBlock;
                continue;
            }

            if (!int.TryParse(token, out int childIndex) ||
                childIndex < 0 ||
                childIndex >= current.ChildElements.Count ||
                current.ChildElements[childIndex] is not OpenXmlCompositeElement child)
            {
                throw new InvalidDataException("块级边界已失效，请刷新后重试。");
            }

            current = child;
        }

        return current;
    }

    private static Body GetBody(WordprocessingDocument document) =>
        document.MainDocumentPart?.Document.Body
        ?? throw new InvalidDataException("DOCX 缺少主文档正文。");

    private static string? GetSegmentKey(SdtBlock block)
    {
        string? tag = block.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;
        return tag?.StartsWith(SegmentTagPrefix, StringComparison.Ordinal) == true
            ? tag[SegmentTagPrefix.Length..]
            : null;
    }

    private static void ValidateRequest(InsertTemplateSegmentBoundaryRequest request)
    {
        if (!SegmentKeyRegex().IsMatch(request.SegmentKey))
        {
            throw new InvalidDataException(
                "segmentKey 只能包含小写字母、数字和短横线。");
        }

        if (string.IsNullOrWhiteSpace(request.SegmentName) ||
            request.SegmentName.Trim().Length > 255)
        {
            throw new InvalidDataException("片段名称不能为空且不能超过 255 个字符。");
        }
    }

    private static async Task<Stream> EditCopyAsync(
        string sourceDocxPath,
        Action<WordprocessingDocument> edit,
        CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"wtb-boundary-{Guid.NewGuid():N}.docx");
        try
        {
            File.Copy(sourceDocxPath, tempPath, overwrite: false);
            cancellationToken.ThrowIfCancellationRequested();
            using (WordprocessingDocument document =
                   WordprocessingDocument.Open(tempPath, true))
            {
                edit(document);
            }

            return new MemoryStream(
                await File.ReadAllBytesAsync(tempPath, cancellationToken),
                writable: false);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentKeyRegex();
}

#pragma warning restore CS1591
