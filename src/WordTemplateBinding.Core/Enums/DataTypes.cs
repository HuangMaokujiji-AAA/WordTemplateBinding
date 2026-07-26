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

    /// <summary>
    /// 结构化对象；当前仅展示，不允许直接绑定到文字。
    /// </summary>
    Object = 7,

    /// <summary>
    /// 二进制或数据库大对象；当前不可绑定。
    /// </summary>
    Binary = 8,
}

/// <summary>
/// 表示模板绑定指向的目标类型。
/// </summary>
public enum BindingTargetKind
{
    /// <summary>
    /// 正文或页脚中的文本模拟值。
    /// </summary>
    Text = 1,

    /// <summary>
    /// Word 原生图表部件。
    /// </summary>
    Chart = 2,
}
