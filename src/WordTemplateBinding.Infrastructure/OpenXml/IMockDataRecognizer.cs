using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 表示模拟数据候选之间发生范围冲突时的识别优先级。
/// </summary>
public enum MockDataRecognitionPriority
{
    /// <summary>
    /// 小数、整数等基于正则表达式的自动识别。
    /// </summary>
    AutomaticRegex = 100,

    /// <summary>
    /// Word 中由作者直接标记的黄色高亮范围。
    /// </summary>
    YellowHighlight = 200,

    /// <summary>
    /// 已经使用双花括号声明的显式字段或文字标记。
    /// </summary>
    ExplicitMarker = 300,
}

/// <summary>
/// 定义针对段落拼接文本的模拟数据识别器。
/// </summary>
public interface IMockDataRecognizer
{
    /// <summary>
    /// 获取当前识别器的冲突处理优先级。
    /// </summary>
    MockDataRecognitionPriority Priority { get; }

    /// <summary>
    /// 在段落文本映射中识别当前实现支持的模拟数据。
    /// </summary>
    /// <param name="paragraph">段落文本映射。</param>
    /// <returns>返回按起始偏移排列的识别结果。</returns>
    IReadOnlyList<RecognizedMockData> Recognize(ParagraphTextMap paragraph);
}
