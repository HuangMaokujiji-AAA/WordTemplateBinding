using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 使用可配置正则表达式识别整数型模拟数据。
/// </summary>
public sealed class IntegerNumberRecognizer : IMockDataRecognizer
{
    private readonly Regex _regex;

    /// <summary>
    /// 初始化整数识别器。
    /// </summary>
    /// <param name="options">模板处理配置。</param>
    public IntegerNumberRecognizer(TemplateProcessingOptions options)
    {
        _regex = ConfiguredRegexFactory.Create(
            options.MockIntegerPattern,
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
                DataType = MockDataType.Integer,
            });
        }

        return recognized.AsReadOnly();
    }
}
