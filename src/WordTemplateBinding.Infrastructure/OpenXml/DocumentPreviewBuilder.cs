using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 根据段落纯文本和模拟数据偏移构建浏览器结构化预览。
/// </summary>
public sealed class DocumentPreviewBuilder : IDocumentPreviewBuilder
{
    /// <inheritdoc />
    public DocumentPreview Build(
        IReadOnlyList<string> paragraphTexts,
        IReadOnlyList<MockDataItem> mockItems)
    {
        ILookup<int, MockDataItem> itemsByParagraph =
            mockItems.ToLookup(item => item.PreviewParagraphIndex);
        List<PreviewParagraph> paragraphs = new(paragraphTexts.Count);

        for (int index = 0; index < paragraphTexts.Count; index++)
        {
            IReadOnlyList<PreviewHighlight> highlights = itemsByParagraph[index]
                .OrderBy(item => item.Locator.StartOffset)
                .Select(item => new PreviewHighlight
                {
                    LocatorId = item.LocatorId,
                    StartOffset = item.Locator.StartOffset,
                    Length = item.Locator.Length,
                    MockValue = item.MockValue,
                })
                .ToList()
                .AsReadOnly();

            paragraphs.Add(new PreviewParagraph
            {
                ParagraphIndex = index,
                Text = paragraphTexts[index],
                Highlights = highlights,
            });
        }

        return new DocumentPreview { Paragraphs = paragraphs.AsReadOnly() };
    }
}
