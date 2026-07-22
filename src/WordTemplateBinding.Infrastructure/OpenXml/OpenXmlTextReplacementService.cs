using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 在写入前验证全部文本定位，并以保留 Run 的方式执行跨 Text 节点范围替换。
/// </summary>
internal sealed class OpenXmlTextReplacementService
{
    private readonly TemplateProcessingOptions _options;

    /// <summary>
    /// 初始化 OpenXML 文本范围替换服务。
    /// </summary>
    /// <param name="options">模板定位配置。</param>
    internal OpenXmlTextReplacementService(TemplateProcessingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// 先验证所有部件、段落、范围、原值、上下文和重叠，再统一执行替换。
    /// </summary>
    /// <param name="mainPart">DOCX 主文档部件。</param>
    /// <param name="replacements">文本替换指令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal void ReplaceAll(
        MainDocumentPart mainPart,
        IReadOnlyCollection<OpenXmlTextReplacement> replacements,
        CancellationToken cancellationToken)
    {
        List<PreparedPart> preparedParts = new();
        foreach (IGrouping<DocumentPartLocatorKey, OpenXmlTextReplacement> partGroup in
                 replacements.GroupBy(item => new DocumentPartLocatorKey(
                     item.Locator.PartKind,
                     item.Locator.PartKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenXmlTextReplacement first = partGroup.First();
            DocumentPartContext partContext = ResolveDocumentPart(
                mainPart,
                partGroup.Key,
                first.LocatorId);
            List<PreparedParagraph> paragraphs = new();

            foreach (IGrouping<int, OpenXmlTextReplacement> paragraphGroup in
                     partGroup.GroupBy(item => item.Locator.ParagraphIndex))
            {
                int paragraphIndex = paragraphGroup.Key;
                if (paragraphIndex < 0 || paragraphIndex >= partContext.Paragraphs.Count)
                {
                    throw new LocatorNotFoundException(paragraphGroup.First().LocatorId);
                }

                ParagraphTextMap map = ParagraphTextMapBuilder.Build(
                    partContext.Paragraphs[paragraphIndex]);
                List<OpenXmlTextReplacement> paragraphReplacements = paragraphGroup
                    .OrderBy(item => item.Locator.StartOffset)
                    .ToList();
                ValidateParagraphReplacements(map, paragraphReplacements);
                paragraphs.Add(new PreparedParagraph(map, paragraphReplacements));
            }

            preparedParts.Add(new PreparedPart(partContext, paragraphs));
        }

        // 所有定位均通过验证后才开始修改，确保调用方永远不会得到半成功文件。
        foreach (PreparedPart preparedPart in preparedParts)
        {
            foreach (PreparedParagraph paragraph in preparedPart.Paragraphs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (OpenXmlTextReplacement replacement in paragraph.Replacements
                             .OrderByDescending(item => item.Locator.StartOffset))
                {
                    if (!string.Equals(
                            replacement.Locator.OriginalValue,
                            replacement.ReplacementValue,
                            StringComparison.Ordinal))
                    {
                        ReplaceMappedText(
                            paragraph.Map,
                            replacement.Locator,
                            replacement.ReplacementValue);
                    }
                }
            }

            preparedPart.Context.Save();
        }
    }

    private void ValidateParagraphReplacements(
        ParagraphTextMap map,
        IReadOnlyList<OpenXmlTextReplacement> replacements)
    {
        int previousEnd = -1;
        foreach (OpenXmlTextReplacement replacement in replacements)
        {
            TextLocator locator = replacement.Locator;
            int endOffset = locator.StartOffset + locator.Length;
            if (!OpenXmlDocumentHelpers.IsSupportedTextLocator(locator) ||
                locator.StartOffset < 0 ||
                locator.Length <= 0 ||
                endOffset > map.FullText.Length)
            {
                throw new LocatorNotFoundException(replacement.LocatorId);
            }

            if (locator.StartOffset < previousEnd)
            {
                throw new ReportRenderingException("同一段落中的绑定范围发生重叠。");
            }

            string currentValue = map.FullText.Substring(locator.StartOffset, locator.Length);
            string contextHash = OpenXmlDocumentHelpers.ComputeContextHash(
                map.FullText,
                locator.StartOffset,
                locator.Length,
                _options.ContextLength);
            if (!string.Equals(currentValue, locator.OriginalValue, StringComparison.Ordinal) ||
                !string.Equals(contextHash, locator.ContextHash, StringComparison.Ordinal))
            {
                throw new LocatorNotFoundException(replacement.LocatorId);
            }

            previousEnd = endOffset;
        }
    }

    private static DocumentPartContext ResolveDocumentPart(
        MainDocumentPart mainPart,
        DocumentPartLocatorKey key,
        string locatorId)
    {
        if (key.PartKind == DocumentPartKind.MainDocument &&
            string.Equals(
                key.PartKey,
                OpenXmlDocumentHelpers.MainDocumentPartKey,
                StringComparison.Ordinal))
        {
            Body body = mainPart.Document?.Body
                ?? throw new ReportRenderingException("模板缺少主文档正文。");
            return new DocumentPartContext(
                OpenXmlDocumentHelpers.GetMainDocumentParagraphs(body),
                () => mainPart.Document.Save());
        }

        if (key.PartKind == DocumentPartKind.Footer)
        {
            FooterPart? footerPart = mainPart.FooterParts.FirstOrDefault(part =>
                string.Equals(part.Uri.OriginalString, key.PartKey, StringComparison.Ordinal));
            if (footerPart?.Footer is null)
            {
                throw new LocatorNotFoundException(locatorId);
            }

            return new DocumentPartContext(
                OpenXmlDocumentHelpers.GetTextParagraphs(footerPart.Footer),
                () => footerPart.Footer.Save());
        }

        throw new LocatorNotFoundException(locatorId);
    }

    private static void ReplaceMappedText(
        ParagraphTextMap map,
        TextLocator locator,
        string replacementValue)
    {
        int targetEnd = locator.StartOffset + locator.Length;
        List<TextSegment> affected = map.Segments
            .Where(segment =>
                segment.StartOffset < targetEnd &&
                segment.EndOffset > locator.StartOffset)
            .ToList();
        if (affected.Count == 0)
        {
            throw new LocatorNotFoundException(locator.OriginalValue);
        }

        for (int index = 0; index < affected.Count; index++)
        {
            TextSegment segment = affected[index];
            string originalNodeText = segment.TextNode.Text ?? string.Empty;
            int localStart = Math.Max(0, locator.StartOffset - segment.StartOffset);
            int localEnd = Math.Min(segment.Length, targetEnd - segment.StartOffset);
            string prefix = originalNodeText[..localStart];
            string suffix = originalNodeText[localEnd..];

            if (index == 0)
            {
                segment.TextNode.Text = prefix + replacementValue +
                    (affected.Count == 1 ? suffix : string.Empty);
            }
            else if (index == affected.Count - 1)
            {
                segment.TextNode.Text = suffix;
            }
            else
            {
                segment.TextNode.Text = prefix + suffix;
            }

            OpenXmlDocumentHelpers.PreserveBoundaryWhitespace(segment.TextNode);
        }
    }

    private sealed record DocumentPartLocatorKey(
        DocumentPartKind PartKind,
        string PartKey);

    private sealed record DocumentPartContext(
        IReadOnlyList<Paragraph> Paragraphs,
        Action Save);

    private sealed record PreparedParagraph(
        ParagraphTextMap Map,
        IReadOnlyList<OpenXmlTextReplacement> Replacements);

    private sealed record PreparedPart(
        DocumentPartContext Context,
        IReadOnlyList<PreparedParagraph> Paragraphs);
}

/// <summary>
/// 表示一个已经确定替换文本的 OpenXML 定位指令。
/// </summary>
internal sealed record OpenXmlTextReplacement(
    string LocatorId,
    TextLocator Locator,
    string ReplacementValue);
