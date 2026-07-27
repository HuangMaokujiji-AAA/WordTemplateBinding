using System.Text.Json;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示条件表达式的失败处理策略。
/// </summary>
public enum ConditionFailureStrategy
{
    /// <summary>报错并终止生成。</summary>
    STRICT = 1,

    /// <summary>记录警告并按 false 处理。</summary>
    FALSE_ON_ERROR = 2,

    /// <summary>记录警告并保留原型。</summary>
    KEEP_TEMPLATE = 3,
}

/// <summary>
/// 表示一个条件块的定义配置。
/// </summary>
public sealed record ConditionBlockDefinition
{
    /// <summary>
    /// 获取条件块在模板中的稳定键。
    /// </summary>
    public required string BlockKey { get; init; }

    /// <summary>
    /// 获取条件表达式（受控 JSON DSL）。
    /// </summary>
    public required JsonElement Expression { get; init; }

    /// <summary>
    /// 获取条件为 false 时的行为策略。
    /// </summary>
    public ConditionFailureStrategy FalseBehavior { get; init; } = ConditionFailureStrategy.STRICT;

    /// <summary>
    /// 获取条件表达式解析失败时的处理策略。
    /// </summary>
    public ConditionFailureStrategy OnError { get; init; } = ConditionFailureStrategy.STRICT;
}

/// <summary>
/// 表示一个条件求值操作数，可以是字面常量或数据路径。
/// </summary>
public sealed record ConditionOperand
{
    /// <summary>
    /// 获取数据路径（适用于左侧操作数）。
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 获取字面常量值（适用于右侧操作数）。
    /// </summary>
    public object? Constant { get; init; }

    /// <summary>
    /// 获取嵌套表达式（适用于 AND/OR/NOT）。
    /// </summary>
    public ConditionExpression? Nested { get; init; }
}

/// <summary>
/// 表示一个受控的条件表达式。
/// </summary>
public sealed record ConditionExpression
{
    /// <summary>
    /// 获取操作符。
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// 获取左侧操作数。
    /// </summary>
    public ConditionOperand? Left { get; init; }

    /// <summary>
    /// 获取右侧操作数。
    /// </summary>
    public ConditionOperand? Right { get; init; }

    /// <summary>
    /// 获取 NOT 操作的操作数。
    /// </summary>
    public ConditionOperand? Operand { get; init; }
}

/// <summary>
/// 表示一次条件求值的结果。
/// </summary>
public sealed record ConditionEvaluationResult
{
    /// <summary>
    /// 获取条件求值是否成功。
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 获取条件求值结果（仅当 Success 为 true 时有效）。
    /// </summary>
    public bool Result { get; init; }

    /// <summary>
    /// 获取求值失败时的错误消息。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 支持的受控条件运算符。
/// </summary>
public static class ConditionOperators
{
    /// <summary>等于比较。</summary>
    public const string EQ = "EQ";
    /// <summary>不等于比较。</summary>
    public const string NE = "NE";
    /// <summary>大于比较。</summary>
    public const string GT = "GT";
    /// <summary>大于等于比较。</summary>
    public const string GTE = "GTE";
    /// <summary>小于比较。</summary>
    public const string LT = "LT";
    /// <summary>小于等于比较。</summary>
    public const string LTE = "LTE";
    /// <summary>判断是否为 null。</summary>
    public const string IS_NULL = "IS_NULL";
    /// <summary>判断是否不为 null。</summary>
    public const string IS_NOT_NULL = "IS_NOT_NULL";
    /// <summary>判断是否为空字符串或空数组。</summary>
    public const string IS_EMPTY = "IS_EMPTY";
    /// <summary>判断是否不为空。</summary>
    public const string IS_NOT_EMPTY = "IS_NOT_EMPTY";
    /// <summary>逻辑与。</summary>
    public const string AND = "AND";
    /// <summary>逻辑或。</summary>
    public const string OR = "OR";
    /// <summary>逻辑非。</summary>
    public const string NOT = "NOT";

    /// <summary>
    /// 返回所有支持的运算符。
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        EQ, NE, GT, GTE, LT, LTE,
        IS_NULL, IS_NOT_NULL, IS_EMPTY, IS_NOT_EMPTY,
        AND, OR, NOT,
    };
}
