using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 定义针对段落拼接文本的模拟数据识别器。
/// </summary>
public interface IMockDataRecognizer
{
    /// <summary>
    /// 在段落文本映射中识别当前实现支持的模拟数据。
    /// </summary>
    /// <param name="paragraph">段落文本映射。</param>
    /// <returns>返回按起始偏移排列的识别结果。</returns>
    IReadOnlyList<RecognizedMockData> Recognize(ParagraphTextMap paragraph);
}

/// <summary>
/// 使用可配置正则表达式识别小数型模拟数据。
/// </summary>
public sealed class DecimalNumberRecognizer : IMockDataRecognizer
{
    private readonly Regex _regex;

    /// <summary>
    /// 初始化小数识别器，并优先使用无回溯正则引擎。
    /// </summary>
    /// <param name="options">模板处理配置。</param>
    public DecimalNumberRecognizer(TemplateProcessingOptions options)
    {
        _regex = ConfiguredRegexFactory.Create(
            options.MockNumberPattern,
            options.RegexTimeoutMilliseconds);
    }

    /// <inheritdoc />
    public IReadOnlyList<RecognizedMockData> Recognize(ParagraphTextMap paragraph)
    {
        List<RecognizedMockData> recognized = new();
        foreach (Match match in _regex.Matches(paragraph.FullText))
        {
            recognized.Add(new RecognizedMockData
            {
                StartOffset = match.Index,
                Length = match.Length,
                Value = match.Value,
                OriginalText = match.Value,
                DataType = MockDataType.Decimal,
            });
        }

        return recognized.AsReadOnly();
    }
}
