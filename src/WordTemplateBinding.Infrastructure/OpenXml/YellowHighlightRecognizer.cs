using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 将 Word 中连续的黄色文本高亮识别为作者显式指定的模拟数据范围。
/// </summary>
public sealed class YellowHighlightRecognizer : IMockDataRecognizer
{
    private readonly Regex _decimalRegex;
    private readonly Regex _integerRegex;
    private readonly bool _enableHighlight;
    private readonly bool _enableShading;
    private readonly HashSet<string> _yellowFillColors;

    /// <summary>
    /// 初始化黄色高亮识别器。
    /// </summary>
    /// <param name="options">模板处理配置。</param>
    public YellowHighlightRecognizer(TemplateProcessingOptions options)
    {
        _decimalRegex = ConfiguredRegexFactory.Create(
            options.MockNumberPattern,
            options.RegexTimeoutMilliseconds);
        _integerRegex = ConfiguredRegexFactory.Create(
            options.MockIntegerPattern,
            options.RegexTimeoutMilliseconds);
        _enableHighlight = options.EnableYellowHighlightRecognition;
        _enableShading = options.EnableYellowShadingRecognition;
        _yellowFillColors = options.YellowFillColors
            .Select(NormalizeFill)
            .Where(value => value.Length == 6)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public MockDataRecognitionPriority Priority =>
        MockDataRecognitionPriority.YellowHighlight;

    /// <inheritdoc />
    public IReadOnlyList<RecognizedMockData> Recognize(ParagraphTextMap paragraph)
    {
        List<RecognizedMockData> recognized = new();
        int? rangeStart = null;
        int rangeEnd = 0;

        foreach (TextSegment segment in paragraph.Segments.Where(item => item.Length > 0))
        {
            if (IsYellowHighlight(segment))
            {
                if (rangeStart is null)
                {
                    rangeStart = segment.StartOffset;
                }
                else if (segment.StartOffset > rangeEnd)
                {
                    AddRange(paragraph.FullText, rangeStart.Value, rangeEnd, recognized);
                    rangeStart = segment.StartOffset;
                }

                rangeEnd = segment.EndOffset;
                continue;
            }

            if (rangeStart is not null)
            {
                AddRange(paragraph.FullText, rangeStart.Value, rangeEnd, recognized);
                rangeStart = null;
            }
        }

        if (rangeStart is not null)
        {
            AddRange(paragraph.FullText, rangeStart.Value, rangeEnd, recognized);
        }

        return recognized.AsReadOnly();
    }

    private void AddRange(
        string fullText,
        int startOffset,
        int endOffset,
        ICollection<RecognizedMockData> recognized)
    {
        int length = endOffset - startOffset;
        if (length <= 0)
        {
            return;
        }

        string value = fullText.Substring(startOffset, length);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        recognized.Add(new RecognizedMockData
        {
            StartOffset = startOffset,
            Length = length,
            Value = value,
            OriginalText = value,
            DataType = InferDataType(value),
            RecognitionKind = IsFullMatch(_decimalRegex, value) ||
                IsFullMatch(_integerRegex, value)
                    ? "YellowNumber"
                    : "YellowHighlight",
        });
    }

    private MockDataType InferDataType(string value)
    {
        if (IsFullMatch(_decimalRegex, value))
        {
            return MockDataType.Decimal;
        }

        return IsFullMatch(_integerRegex, value)
            ? MockDataType.Integer
            : MockDataType.String;
    }

    private static bool IsFullMatch(Regex regex, string value)
    {
        Match match = regex.Match(value);
        return match.Success && match.Index == 0 && match.Length == value.Length;
    }

    private bool IsYellowHighlight(TextSegment segment)
    {
        Run? run = segment.TextNode.Ancestors<Run>().FirstOrDefault();
        RunProperties? properties = run?.RunProperties;
        Highlight? highlight = properties?.GetFirstChild<Highlight>();
        if (_enableHighlight && highlight?.Val?.Value == HighlightColorValues.Yellow)
        {
            return true;
        }

        if (!_enableShading)
        {
            return false;
        }

        Shading? shading = properties?.GetFirstChild<Shading>();
        string fill = NormalizeFill(shading?.Fill?.Value);
        return fill.Length == 6 && _yellowFillColors.Contains(fill);
    }

    private static string NormalizeFill(string? value) =>
        (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
}
