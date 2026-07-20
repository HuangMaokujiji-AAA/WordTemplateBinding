using System.Globalization;
using System.Text.Json;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Infrastructure.OpenXml;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证统一数据值格式化规则。
/// </summary>
public sealed class DataValueFormatterTests
{
    private readonly DataValueFormatter _formatter = new();

    /// <summary>
    /// 验证 Decimal 和 Double 使用不丢失有效信息的格式。
    /// </summary>
    [Fact]
    public void Format_NumericValues_UsesInvariantPrecision()
    {
        Assert.Equal(
            "92.3",
            _formatter.Format("92.30", DataValueType.Decimal, CultureInfo.InvariantCulture));
        Assert.Equal(
            "0.10000000000000001",
            _formatter.Format(0.1d, DataValueType.Decimal, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 验证 JsonElement 数字和布尔值按声明类型转换。
    /// </summary>
    [Fact]
    public void Format_JsonElements_ConvertsByDeclaredType()
    {
        using JsonDocument decimalDocument = JsonDocument.Parse("92.3");
        using JsonDocument booleanDocument = JsonDocument.Parse("true");

        Assert.Equal(
            "92.3",
            _formatter.Format(
                decimalDocument.RootElement,
                DataValueType.Decimal,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            "true",
            _formatter.Format(
                booleanDocument.RootElement,
                DataValueType.Boolean,
                CultureInfo.InvariantCulture));
    }
}
