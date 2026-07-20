namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示不依赖 Word 页面坐标的结构化文档预览。
/// </summary>
public sealed record DocumentPreview
{
    /// <summary>
    /// 获取按文档顺序排列的段落。
    /// </summary>
    public required IReadOnlyList<PreviewParagraph> Paragraphs { get; init; }
}

/// <summary>
/// 表示预览中的一个文本段落。
/// </summary>
public sealed record PreviewParagraph
{
    /// <summary>
    /// 获取段落索引。
    /// </summary>
    public required int ParagraphIndex { get; init; }

    /// <summary>
    /// 获取由 Word 文本节点拼接得到的纯文本。
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 获取段落中的模拟数据高亮范围。
    /// </summary>
    public required IReadOnlyList<PreviewHighlight> Highlights { get; init; }
}

/// <summary>
/// 表示浏览器预览中的模拟数据高亮范围。
/// </summary>
public sealed record PreviewHighlight
{
    /// <summary>
    /// 获取模拟数据定位标识。
    /// </summary>
    public required string LocatorId { get; init; }

    /// <summary>
    /// 获取高亮在段落文本中的起始偏移。
    /// </summary>
    public required int StartOffset { get; init; }

    /// <summary>
    /// 获取高亮文本长度。
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// 获取高亮对应的原始模拟值。
    /// </summary>
    public required string MockValue { get; init; }
}
