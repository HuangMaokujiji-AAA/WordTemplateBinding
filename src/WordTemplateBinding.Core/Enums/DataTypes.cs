namespace WordTemplateBinding.Core.Enums;

/// <summary>
/// 表示模板中识别出的模拟数据类型。
/// </summary>
public enum MockDataType
{
    /// <summary>
    /// 小数型模拟数据。
    /// </summary>
    Decimal = 1,

    /// <summary>
    /// 整数型模拟数据。
    /// </summary>
    Integer = 2,

    /// <summary>
    /// 使用显式标记声明的文本型模拟数据。
    /// </summary>
    String = 3,
}

/// <summary>
/// 表示数据源字段的值类型。
/// </summary>
public enum DataValueType
{
    /// <summary>
    /// 文本值。
    /// </summary>
    String = 1,

    /// <summary>
    /// 整数值。
    /// </summary>
    Integer = 2,

    /// <summary>
    /// 小数值。
    /// </summary>
    Decimal = 3,

    /// <summary>
    /// 日期或日期时间值。
    /// </summary>
    Date = 4,

    /// <summary>
    /// 布尔值。
    /// </summary>
    Boolean = 5,

    /// <summary>
    /// 数组或集合节点。
    /// </summary>
    Array = 6,
}
