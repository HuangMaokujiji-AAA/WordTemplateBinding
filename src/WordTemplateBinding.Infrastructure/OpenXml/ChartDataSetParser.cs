using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 将 JSON 数组或 .NET 集合规范化为“分类 + 多个数值系列”。
/// </summary>
internal static class ChartDataSetParser
{
    private static readonly string[] PreferredCategoryNames =
    {
        "category", "name", "label", "类别", "分类", "名称",
    };

    internal static ChartDataSet Parse(object? value)
    {
        IReadOnlyList<object?> items = ReadItems(value);
        if (items.Count == 0)
        {
            throw new FormatException("图表集合不能为空。");
        }

        if (!TryReadRow(items[0], out IReadOnlyList<KeyValuePair<string, object?>> firstRow))
        {
            List<decimal?> scalarValues = items.Select(ToNullableDecimal).ToList();
            if (scalarValues.Any(item => item is null))
            {
                throw new FormatException("图表集合元素必须是数值或具有命名列的对象。");
            }

            return new ChartDataSet(
                Enumerable.Range(1, items.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToList(),
                new[] { new ChartDataSeries("值", scalarValues) });
        }

        List<IReadOnlyList<KeyValuePair<string, object?>>> rows = new(items.Count)
        {
            firstRow,
        };
        for (int index = 1; index < items.Count; index++)
        {
            if (!TryReadRow(items[index], out IReadOnlyList<KeyValuePair<string, object?>> row))
            {
                throw new FormatException($"图表集合第 {index + 1} 行不是对象。");
            }

            rows.Add(row);
        }

        IReadOnlyList<string> headers = firstRow.Select(pair => pair.Key).ToList();
        if (headers.Count < 2)
        {
            throw new FormatException("图表对象至少需要一个分类列和一个数值列。");
        }

        string categoryName = headers.FirstOrDefault(header =>
                PreferredCategoryNames.Contains(header, StringComparer.OrdinalIgnoreCase))
            ?? headers[0];
        List<string> numericHeaders = headers
            .Where(header => !string.Equals(header, categoryName, StringComparison.OrdinalIgnoreCase))
            .Where(header => rows.Any(row => ToNullableDecimal(GetValue(row, header)) is not null))
            .ToList();
        if (numericHeaders.Count == 0)
        {
            throw new FormatException("图表集合没有可用的数值系列列。");
        }

        List<string> categories = rows
            .Select((row, index) => Convert.ToString(
                    GetValue(row, categoryName),
                    CultureInfo.InvariantCulture)
                ?? (index + 1).ToString(CultureInfo.InvariantCulture))
            .ToList();
        List<ChartDataSeries> series = numericHeaders
            .Select(header => new ChartDataSeries(
                header,
                rows.Select(row => ToNullableDecimal(GetValue(row, header))).ToList()))
            .ToList();

        return new ChartDataSet(categories, series);
    }

    private static IReadOnlyList<object?> ReadItems(object? value)
    {
        if (value is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("图表字段必须是 JSON 数组。");
            }

            return json.EnumerateArray().Select(item => (object?)item.Clone()).ToList();
        }

        if (value is string || value is not IEnumerable enumerable)
        {
            throw new FormatException("图表字段必须是集合。");
        }

        return enumerable.Cast<object?>().ToList();
    }

    private static bool TryReadRow(
        object? value,
        out IReadOnlyList<KeyValuePair<string, object?>> row)
    {
        if (value is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            row = json.EnumerateObject()
                .Select(property => new KeyValuePair<string, object?>(property.Name, property.Value.Clone()))
                .ToList();
            return true;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            row = readOnlyDictionary.ToList();
            return true;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            row = dictionary.ToList();
            return true;
        }

        if (value is IDictionary nonGenericDictionary)
        {
            row = nonGenericDictionary.Keys.Cast<object>()
                .Select(key => new KeyValuePair<string, object?>(
                    Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty,
                    nonGenericDictionary[key]))
                .ToList();
            return true;
        }

        if (value is null || IsScalar(value.GetType()))
        {
            row = Array.Empty<KeyValuePair<string, object?>>();
            return false;
        }

        row = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => new KeyValuePair<string, object?>(property.Name, property.GetValue(value)))
            .ToList();
        return row.Count > 0;
    }

    private static object? GetValue(
        IReadOnlyList<KeyValuePair<string, object?>> row,
        string name) =>
        row.FirstOrDefault(pair => string.Equals(
            pair.Key,
            name,
            StringComparison.OrdinalIgnoreCase)).Value;

    private static decimal? ToNullableDecimal(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.Number when json.TryGetDecimal(out decimal number) => number,
                JsonValueKind.String when decimal.TryParse(
                    json.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal number) => number,
                _ => null,
            };
        }

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(Guid);
}

internal sealed record ChartDataSet(
    IReadOnlyList<string> Categories,
    IReadOnlyList<ChartDataSeries> Series);

internal sealed record ChartDataSeries(
    string Name,
    IReadOnlyList<decimal?> Values);
