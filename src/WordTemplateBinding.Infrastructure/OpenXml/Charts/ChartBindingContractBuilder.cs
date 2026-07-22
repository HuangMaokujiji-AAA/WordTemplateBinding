using System.Globalization;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 构建图表绑定契约（BindingContract），并生成：
/// - 系列字段映射（SeriesFields）
/// - 可直接用于 OpenXmlChartWriter 的样例替换数据（SampleReplacementPayload）
/// - 完整的报告生成请求示例（ReportRequestExample）
///
/// 生成的 Payload 必须与 ChartDataSetParser 和 OpenXmlChartWriter 完全兼容。
/// </summary>
internal static class ChartBindingContractBuilder
{
    internal static ChartBindingContract Build(
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories,
        string? templateId,
        ChartDataTableSnapshot dataTable,
        out List<ChartDiagnosticItem> diagnostics)
    {
        diagnostics = new List<ChartDiagnosticItem>();

        // Determine mode
        string mode = "whole-dataset";

        // Build series fields with dedup handling
        List<ChartBindingSeriesField> seriesFields = new(series.Count);
        HashSet<string> usedPropertyNames = new(StringComparer.Ordinal);

        foreach (ChartSeriesSnapshot s in series)
        {
            string originalName = s.Name;
            string propertyName = NormalizePayloadProperty(originalName);

            // Handle empty name
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = $"Series{s.SeriesIndex + 1}";
                diagnostics.Add(new ChartDiagnosticItem
                {
                    Code = "missing_series_name",
                    Level = "warning",
                    Message = $"系列 {s.SeriesIndex + 1} 的名称为空，使用 {propertyName}。",
                    SeriesIndex = s.SeriesIndex,
                    Recoverable = true,
                });
            }

            // Handle duplicate
            if (!usedPropertyNames.Add(propertyName))
            {
                int counter = 2;
                string baseName = propertyName;
                while (!usedPropertyNames.Add($"{baseName}_{counter}"))
                {
                    counter++;
                }
                string newName = $"{baseName}_{counter}";
                diagnostics.Add(new ChartDiagnosticItem
                {
                    Code = "duplicate_series_name",
                    Level = "warning",
                    Message = $"系列名称 \"{originalName}\" 重复，Payload 属性名使用 \"{newName}\"。",
                    SeriesIndex = s.SeriesIndex,
                    Recoverable = true,
                });
                propertyName = newName;
            }

            seriesFields.Add(new ChartBindingSeriesField
            {
                SeriesKey = s.Key,
                SeriesIndex = s.SeriesIndex,
                OriginalName = originalName,
                PayloadProperty = propertyName,
                ValueType = "number",
                Required = true,
            });
        }

        // Generate SampleReplacementPayload
        List<Dictionary<string, object?>> samplePayload = BuildSamplePayload(
            categories, series, seriesFields);

        // Generate ReportRequestExample
        string suggestedDataPath = "ChartData.Chart1";

        Dictionary<string, object?> values = new()
        {
            [suggestedDataPath] = samplePayload,
        };

        ChartReportRequestExample requestExample = new()
        {
            TemplateId = templateId ?? "00000000-0000-0000-0000-000000000000",
            BoundDataPath = null,
            SuggestedDataPath = suggestedDataPath,
            Values = values.AsReadOnly(),
        };

        return new ChartBindingContract
        {
            Mode = mode,
            CategoryProperty = "Category",
            SeriesFields = seriesFields.AsReadOnly(),
            SampleReplacementPayload = samplePayload.ToArray(),
            ReportRequestExample = requestExample,
        };
    }

    /// <summary>
    /// 标准化 Payload 属性名：替换空格为下划线，去除特殊字符。
    /// </summary>
    private static string NormalizePayloadProperty(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Replace spaces and non-alphanumeric chars (except CJK characters)
        string normalized = new(name.Select(c =>
            char.IsWhiteSpace(c) ? '_' : c).ToArray());

        // Collapse multiple underscores
        while (normalized.Contains("__"))
        {
            normalized = normalized.Replace("__", "_");
        }

        return normalized.Trim('_');
    }

    /// <summary>
    /// 生成与 ChartDataSetParser 兼容的样例 Payload。
    /// 格式：每行一个对象，包含"Category"键和每个系列的数值列。
    /// </summary>
    private static List<Dictionary<string, object?>> BuildSamplePayload(
        IReadOnlyList<ChartCategorySnapshot> categories,
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartBindingSeriesField> seriesFields)
    {
        int rowCount = categories.Count > 0
            ? categories.Count
            : series.Max(s => s.Values.Count);

        List<Dictionary<string, object?>> rows = new(rowCount);

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            Dictionary<string, object?> row = new()
            {
                ["Category"] = rowIndex < categories.Count
                    ? (categories[rowIndex].Value ?? (rowIndex + 1).ToString(CultureInfo.InvariantCulture))
                    : (rowIndex + 1).ToString(CultureInfo.InvariantCulture),
            };

            foreach (ChartBindingSeriesField field in seriesFields)
            {
                ChartSeriesSnapshot? s = series.FirstOrDefault(
                    s => s.SeriesIndex == field.SeriesIndex);

                if (s is not null && rowIndex < s.Values.Count)
                {
                    // Preserve null for missing values (not 0)
                    row[field.PayloadProperty] = s.Values[rowIndex].IsMissing
                        ? null
                        : s.Values[rowIndex].Value;
                }
                else
                {
                    row[field.PayloadProperty] = null;
                }
            }

            rows.Add(row);
        }

        return rows;
    }
}
