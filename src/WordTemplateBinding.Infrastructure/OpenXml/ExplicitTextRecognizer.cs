using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 识别形如 <c>{{text:示例文字}}</c> 或 <c>{{示例文字}}</c> 的显式文本模拟数据标记。
/// </summary>
public sealed class ExplicitTextRecognizer : IMockDataRecognizer
{
    private readonly Regex _regex;

    /// <summary>
    /// 初始化显式文本标记识别器。
    /// </summary>
    /// <param name="options">模板处理配置。</param>
    public ExplicitTextRecognizer(TemplateProcessingOptions options)
    {
        _regex = ConfiguredRegexFactory.Create(
            options.MockTextPattern,
            options.RegexTimeoutMilliseconds);
        if (_regex.GetGroupNames().All(name => !string.Equals(name, "value", StringComparison.Ordinal)))
        {
            throw new ArgumentException("文字标记正则必须包含名为 value 的捕获组。", nameof(options));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecognizedMockData> Recognize(ParagraphTextMap paragraph)
    {
        List<RecognizedMockData> recognized = new();
        foreach (Match match in _regex.Matches(paragraph.FullText))
        {
            Group valueGroup = match.Groups["value"];
            if (!valueGroup.Success || valueGroup.Length == 0)
            {
                continue;
            }

            recognized.Add(new RecognizedMockData
            {
                StartOffset = match.Index,
                Length = match.Length,
                Value = valueGroup.Value,
                OriginalText = match.Value,
                DataType = MockDataType.String,
            });
        }

        return recognized.AsReadOnly();
    }
}
