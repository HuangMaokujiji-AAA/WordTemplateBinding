using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>识别主文档中具有标题行和样例行的业务表格。</summary>
internal static partial class OpenXmlTableReader
{
    public static IReadOnlyList<TableTemplateItem> Read(
        Body body,
        string contentHash,
        ILocatorIdGenerator locatorIdGenerator)
    {
        IReadOnlyList<Paragraph> paragraphs = OpenXmlDocumentHelpers.GetTextParagraphs(body);
        Dictionary<Paragraph, int> paragraphIndexes = paragraphs
            .Select((paragraph, index) => (paragraph, index))
            .ToDictionary(item => item.paragraph, item => item.index);
        List<TableTemplateItem> items = new();
        string? precedingParagraph = null;
        int tableIndex = 0;

        foreach (OpenXmlElement element in body.Descendants())
        {
            if (element is Paragraph paragraph && !paragraph.Ancestors<Table>().Any())
            {
                string text = ReadText(paragraph);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    precedingParagraph = text;
                }

                continue;
            }

            if (element is not Table table || table.Ancestors<Table>().Any())
            {
                continue;
            }

            TableTemplateItem? item = ReadTable(
                table,
                tableIndex,
                precedingParagraph,
                paragraphIndexes,
                contentHash,
                locatorIdGenerator);
            if (item is not null)
            {
                items.Add(item);
            }

            tableIndex++;
        }

        return items.AsReadOnly();
    }

    private static TableTemplateItem? ReadTable(
        Table table,
        int tableIndex,
        string? precedingParagraph,
        IReadOnlyDictionary<Paragraph, int> paragraphIndexes,
        string contentHash,
        ILocatorIdGenerator locatorIdGenerator)
    {
        List<TableRow> rows = table.Elements<TableRow>().ToList();
        if (rows.Count < 2)
        {
            return null;
        }

        List<TableCell> headerCells = rows[0].Elements<TableCell>().ToList();
        if (headerCells.Count < 2)
        {
            return null;
        }

        List<string> headers = headerCells.Select(ReadText).ToList();
        if (headers.Count(header => !string.IsNullOrWhiteSpace(header)) < 2)
        {
            return null;
        }

        Paragraph? firstParagraph = table.Descendants<Paragraph>().FirstOrDefault();
        int firstParagraphIndex = firstParagraph is not null &&
                                  paragraphIndexes.TryGetValue(firstParagraph, out int index)
            ? index
            : -1;
        string headerSignature = string.Join("|", headers.Select(NormalizeHeader));
        TableSuggestion suggestion = ResolveSuggestion(headers);
        string? contextLabel = NormalizeContextLabel(precedingParagraph);
        TableLocator locator = new()
        {
            PartKey = OpenXmlDocumentHelpers.MainDocumentPartKey,
            TableIndex = tableIndex,
            FirstParagraphIndex = firstParagraphIndex,
            HeaderSignature = headerSignature,
        };
        IReadOnlyList<TableColumnTemplate> columns = headers
            .Select((header, columnIndex) => new TableColumnTemplate
            {
                ColumnIndex = columnIndex,
                Header = header,
                SuggestedField = suggestion.Fields.GetValueOrDefault(columnIndex),
            })
            .ToList()
            .AsReadOnly();
        string title = contextLabel ?? $"表格 {tableIndex + 1}";
        return new TableTemplateItem
        {
            LocatorId = locatorIdGenerator.Generate(contentHash, locator),
            Locator = locator,
            Title = title,
            ContextLabel = contextLabel,
            SuggestedSourcePath = suggestion.SourcePath,
            HeaderRowCount = 1,
            TemplateRowCount = rows.Count,
            Columns = columns,
            // Any structurally valid two-row table can be bound manually.
            // Known header patterns only provide automatic source suggestions.
            IsBindable = columns.Count > 0,
            IsBound = false,
            BoundDataPath = null,
        };
    }

    private static TableSuggestion ResolveSuggestion(IReadOnlyList<string> headers)
    {
        HashSet<string> normalized = headers
            .Select(NormalizeHeader)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (normalized.Contains("学科") && normalized.Contains("数量") &&
            normalized.Contains("占比%"))
        {
            return Suggest(
                "undergraduateMajors",
                headers,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["学科"] = "disciplineCategory",
                    ["数量"] = "majorCount",
                    ["占比%"] = "percentage",
                });
        }

        if (normalized.Contains("指标项") && normalized.Contains("学校数据值"))
        {
            return Suggest(
                "teachingMetrics",
                headers,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["指标项"] = "displayName",
                    ["学校数据值"] = "displayValue",
                });
        }

        if (normalized.SetEquals(new[] { "专业代码", "专业名称", "学位", "监测结果" }))
        {
            return Suggest(
                "featuredMajors",
                headers,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["专业代码"] = "majorCode",
                    ["专业名称"] = "majorName",
                    ["学位"] = "degreeCategory",
                    ["监测结果"] = "status",
                });
        }

        if (normalized.Contains("序号") && normalized.Contains("专业代码") &&
            normalized.Contains("设置年份") && normalized.Contains("专任教师数"))
        {
            return Suggest(
                "majorColleges",
                headers,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["序号"] = "rowNumber",
                    ["专业代码"] = "majorCode",
                    ["专业名称"] = "majorName",
                    ["学位门类"] = "degreeCategory",
                    ["设置年份"] = "majorEstablishmentYears",
                    ["是否新专业"] = "isNewMajor",
                    ["专业学生数"] = "studentCount",
                    ["专任教师数"] = "fullTimeTeacherCount",
                    ["监测结果"] = "monitoringDisplay",
                });
        }

        return new TableSuggestion(null, new Dictionary<int, string>());
    }

    private static TableSuggestion Suggest(
        string sourcePath,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> mapping)
    {
        Dictionary<int, string> fields = new();
        for (int index = 0; index < headers.Count; index++)
        {
            if (mapping.TryGetValue(NormalizeHeader(headers[index]), out string? field))
            {
                fields[index] = field;
            }
        }

        return new TableSuggestion(sourcePath, fields);
    }

    private static string ReadText(OpenXmlElement element) =>
        string.Concat(element.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
                .Select(text => text.Text))
            .Trim();

    private static string NormalizeHeader(string value) =>
        WhitespaceRegex().Replace(value, string.Empty)
            .Replace("（", string.Empty, StringComparison.Ordinal)
            .Replace("）", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);

    private static string? NormalizeContextLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = HeadingPrefixRegex().Replace(value.Trim(), string.Empty);
        normalized = BasicInformationSuffixRegex().Replace(normalized, string.Empty).Trim();
        return normalized.Length == 0 ? value.Trim() : normalized;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^\s*\d+[\.、]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"\s*基本信息\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BasicInformationSuffixRegex();

    private sealed record TableSuggestion(
        string? SourcePath,
        IReadOnlyDictionary<int, string> Fields);
}
