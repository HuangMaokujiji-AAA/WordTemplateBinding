using System.Globalization;
using System.Text.Json;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 根据 ChartBindingMapping 将数据源 JSON 数组转换为 NormalizedChartData。
/// </summary>
internal static class ChartDataBindingResolver
{
    internal static NormalizedChartData Resolve(
        object? sourceValue,
        ChartBindingMapping mapping,
        ChartDataDefinition definition)
    {
        var items = ReadArrayItems(sourceValue);
        if (items.Count == 0)
            throw new FormatException("图表数据集为空。");

        // Extract categories
        var categories = new List<string?>(items.Count);
        foreach (var item in items)
        {
            var catVal = GetFieldValue(item, mapping.CategoryField);
            categories.Add(catVal is null ? null : Convert.ToString(catVal, CultureInfo.InvariantCulture));
        }

        // Build series
        int requiredSeriesCount = definition.Series.Count;
        var normalizedSeries = new List<NormalizedChartSeries>(requiredSeriesCount);

        var usedIndices = new HashSet<int>();
        foreach (var seriesMapping in mapping.SeriesMappings)
        {
            if (seriesMapping.SeriesIndex < 0 || seriesMapping.SeriesIndex >= requiredSeriesCount)
                throw new FormatException(
                    $"系列映射中的 SeriesIndex {seriesMapping.SeriesIndex} 超出图表系列范围 0–{requiredSeriesCount - 1}。");

            if (!usedIndices.Add(seriesMapping.SeriesIndex))
                throw new FormatException($"系列索引 {seriesMapping.SeriesIndex} 被重复映射。");

            var templateSeries = definition.Series[seriesMapping.SeriesIndex];

            // Collect values
            var values = new List<decimal?>(items.Count);
            for (int rowIdx = 0; rowIdx < items.Count; rowIdx++)
            {
                var item = items[rowIdx];
                var rawVal = GetFieldValue(item, seriesMapping.ValueField);
                if (rawVal is JsonElement je && je.ValueKind == JsonValueKind.Null)
                {
                    values.Add(null);
                    continue;
                }
                if (rawVal is null)
                {
                    values.Add(null);
                    continue;
                }

                if (rawVal is JsonElement je2 && je2.ValueKind == JsonValueKind.Number)
                {
                    values.Add(je2.GetDecimal());
                }
                else if (rawVal is decimal dec)
                {
                    values.Add(dec);
                }
                else
                {
                    var str = Convert.ToString(rawVal, CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(str))
                    {
                        values.Add(null);
                    }
                    else if (decimal.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    {
                        values.Add(num);
                    }
                    else
                    {
                        throw new FormatException(
                            $"第 {rowIdx + 1} 行的字段 \"{seriesMapping.ValueField}\" 的值 \"{str}\" 不是有效数字。");
                    }
                }
            }

            // Determine series name
            string seriesName = templateSeries.Name;
            if (!string.IsNullOrWhiteSpace(seriesMapping.SeriesNameField))
            {
                // Dynamic series name: use first row's value
                var nameVal = GetFieldValue(items[0], seriesMapping.SeriesNameField);
                if (nameVal is not null)
                    seriesName = Convert.ToString(nameVal, CultureInfo.InvariantCulture) ?? seriesName;
            }

            normalizedSeries.Add(new NormalizedChartSeries
            {
                SeriesIndex = seriesMapping.SeriesIndex,
                SeriesKey = seriesMapping.SeriesKey,
                Name = seriesName,
                Values = values.AsReadOnly(),
            });
        }

        // Verify all series are covered
        for (int i = 0; i < requiredSeriesCount; i++)
        {
            if (!usedIndices.Contains(i))
                throw new FormatException(
                    $"图表需要 {requiredSeriesCount} 个系列，但映射只提供了 {usedIndices.Count} 个。系列 {i}（{definition.Series[i].Name}）未映射。");
        }

        // Sort by series index
        normalizedSeries.Sort((a, b) => a.SeriesIndex.CompareTo(b.SeriesIndex));

        return new NormalizedChartData
        {
            Categories = categories.AsReadOnly(),
            Series = normalizedSeries.AsReadOnly(),
        };
    }

    private static IReadOnlyList<object?> ReadArrayItems(object? value)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray().Select(e => (object?)e.Clone()).ToList();
        }
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            return enumerable.Cast<object?>().ToList();
        }
        throw new FormatException("图表数据必须是 JSON 数组或集合。");
    }

    private static object? GetFieldValue(object? item, string fieldName)
    {
        if (item is null) return null;

        if (item is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty(fieldName, out var prop))
                return prop.Clone();
            throw new FormatException($"数据行中缺少字段 \"{fieldName}\"。");
        }

        if (item is IReadOnlyDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(fieldName, out var val))
                return val;
            throw new FormatException($"数据行中缺少字段 \"{fieldName}\"。");
        }

        if (item is IDictionary<string, object?> genDict)
        {
            if (genDict.TryGetValue(fieldName, out var val))
                return val;
            throw new FormatException($"数据行中缺少字段 \"{fieldName}\"。");
        }

        // Reflection fallback
        var propInfo = item.GetType().GetProperty(fieldName);
        if (propInfo is not null && propInfo.CanRead)
            return propInfo.GetValue(item);

        throw new FormatException($"数据行类型 {item.GetType().Name} 没有字段 \"{fieldName}\"。");
    }
}
