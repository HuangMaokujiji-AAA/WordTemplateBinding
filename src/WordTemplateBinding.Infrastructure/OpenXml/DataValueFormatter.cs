using System.Globalization;
using System.Text.Json;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Interfaces;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 将常见 CLR 或 JsonElement 数据值安全格式化为 Word 文本。
/// </summary>
public sealed class DataValueFormatter : IDataValueFormatter
{
    /// <inheritdoc />
    public string Format(object? value, DataValueType valueType, CultureInfo culture)
    {
        if (value is null)
        {
            throw new FormatException("数据值不能为空。");
        }

        return value is JsonElement jsonElement
            ? FormatJsonElement(jsonElement, valueType, culture)
            : valueType switch
            {
                DataValueType.String => Convert.ToString(value, culture) ?? string.Empty,
                DataValueType.Integer => Convert.ToInt64(value, culture).ToString(culture),
                DataValueType.Decimal => FormatDecimalValue(value, culture),
                DataValueType.Date => FormatDateValue(value, culture),
                DataValueType.Boolean => Convert.ToBoolean(value, culture) ? "true" : "false",
                DataValueType.Array => throw new FormatException("第一阶段不支持数组值格式化。"),
                _ => throw new FormatException($"不支持的数据类型：{valueType}。"),
            };
    }

    /// <summary>
    /// 按字段类型解析并格式化 JSON 值。
    /// </summary>
    /// <param name="element">JSON 值。</param>
    /// <param name="valueType">字段声明类型。</param>
    /// <param name="culture">格式化区域文化。</param>
    /// <returns>返回格式化文本。</returns>
    private static string FormatJsonElement(
        JsonElement element,
        DataValueType valueType,
        CultureInfo culture)
    {
        return valueType switch
        {
            DataValueType.String when element.ValueKind == JsonValueKind.String =>
                element.GetString() ?? string.Empty,
            DataValueType.String => element.ToString(),
            DataValueType.Integer => ReadJsonInteger(element, culture).ToString(culture),
            DataValueType.Decimal => ReadJsonDecimal(element, culture).ToString("G29", culture),
            DataValueType.Date => ReadJsonDate(element).ToString("O", culture),
            DataValueType.Boolean => ReadJsonBoolean(element) ? "true" : "false",
            DataValueType.Array => throw new FormatException("第一阶段不支持数组值格式化。"),
            _ => throw new FormatException($"JSON 值与字段类型 {valueType} 不兼容。"),
        };
    }

    /// <summary>
    /// 使用不丢失有效信息的格式输出小数或浮点数。
    /// </summary>
    /// <param name="value">数值对象。</param>
    /// <param name="culture">格式化区域文化。</param>
    /// <returns>返回数值文本。</returns>
    private static string FormatDecimalValue(object value, CultureInfo culture)
    {
        return value switch
        {
            decimal decimalValue => decimalValue.ToString("G29", culture),
            double doubleValue => doubleValue.ToString("G17", culture),
            float floatValue => floatValue.ToString("G9", culture),
            string stringValue => decimal.Parse(
                stringValue,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                culture).ToString("G29", culture),
            _ => Convert.ToDecimal(value, culture).ToString("G29", culture),
        };
    }

    /// <summary>
    /// 将日期相关 CLR 值格式化为往返格式。
    /// </summary>
    /// <param name="value">日期值。</param>
    /// <param name="culture">格式化区域文化。</param>
    /// <returns>返回 ISO 8601 往返格式文本。</returns>
    private static string FormatDateValue(object value, CultureInfo culture)
    {
        return value switch
        {
            DateTimeOffset offset => offset.ToString("O", culture),
            DateTime dateTime => dateTime.ToString("O", culture),
            string text => DateTimeOffset.Parse(text, culture, DateTimeStyles.RoundtripKind)
                .ToString("O", culture),
            _ => throw new FormatException("数据值不是受支持的日期类型。"),
        };
    }

    /// <summary>
    /// 从 JSON 数字或数值字符串读取 Int64。
    /// </summary>
    /// <param name="element">JSON 值。</param>
    /// <param name="culture">解析区域文化。</param>
    /// <returns>返回整数值。</returns>
    private static long ReadJsonInteger(JsonElement element, CultureInfo culture)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Integer, culture, out number))
        {
            return number;
        }

        throw new FormatException("JSON 值不是有效整数。");
    }

    /// <summary>
    /// 从 JSON 数字或数值字符串读取 Decimal。
    /// </summary>
    /// <param name="element">JSON 值。</param>
    /// <param name="culture">解析区域文化。</param>
    /// <returns>返回小数值。</returns>
    private static decimal ReadJsonDecimal(JsonElement element, CultureInfo culture)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out decimal number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                element.GetString(),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                culture,
                out number))
        {
            return number;
        }

        throw new FormatException("JSON 值不是有效小数。");
    }

    /// <summary>
    /// 从 JSON 布尔值或布尔字符串读取布尔结果。
    /// </summary>
    /// <param name="element">JSON 值。</param>
    /// <returns>返回布尔值。</returns>
    private static bool ReadJsonBoolean(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        if (element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out bool result))
        {
            return result;
        }

        throw new FormatException("JSON 值不是有效布尔值。");
    }

    /// <summary>
    /// 从 JSON 日期字符串读取 DateTimeOffset。
    /// </summary>
    /// <param name="element">JSON 值。</param>
    /// <returns>返回日期时间值。</returns>
    private static DateTimeOffset ReadJsonDate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String &&
            element.TryGetDateTimeOffset(out DateTimeOffset value))
        {
            return value;
        }

        throw new FormatException("JSON 值不是有效日期时间字符串。");
    }
}
