using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

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
    public MockDataRecognitionPriority Priority =>
        MockDataRecognitionPriority.AutomaticRegex;

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
