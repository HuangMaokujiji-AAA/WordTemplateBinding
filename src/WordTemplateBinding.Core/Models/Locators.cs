using WordTemplateBinding.Core.Enums;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示模拟数据在 Word 文档文本流中的结构化位置。
/// </summary>
public sealed record TextLocator
{
    /// <summary>
    /// 获取文档部件类型。
    /// </summary>
    public required DocumentPartKind PartKind { get; init; }

    /// <summary>
    /// 获取文档部件的稳定键。
    /// </summary>
    public required string PartKey { get; init; }

    /// <summary>
    /// 获取部件内按文档顺序计算的段落索引。
    /// </summary>
    public required int ParagraphIndex { get; init; }

    /// <summary>
    /// 获取匹配值在段落拼接文本中的起始偏移。
    /// </summary>
    public required int StartOffset { get; init; }

    /// <summary>
    /// 获取匹配文本长度。
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// 获取当前段落内的匹配序号。
    /// </summary>
    public required int OccurrenceIndex { get; init; }

    /// <summary>
    /// 获取扫描时识别到的原始值。
    /// </summary>
    public required string OriginalValue { get; init; }

    /// <summary>
    /// 获取匹配位置附近文本的 SHA-256 哈希。
    /// </summary>
    public required string ContextHash { get; init; }
}

/// <summary>
/// 表示识别器在段落文本中发现的候选模拟数据。
/// </summary>
public sealed record RecognizedMockData
{
    /// <summary>
    /// 获取候选文本在段落中的起始偏移。
    /// </summary>
    public required int StartOffset { get; init; }

    /// <summary>
    /// 获取候选文本长度。
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// 获取候选模拟值。
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// 获取定位范围内未经处理的原始文本。对于显式文字标记，该值包含标记语法。
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// 获取候选模拟数据类型。
    /// </summary>
    public required MockDataType DataType { get; init; }
}
