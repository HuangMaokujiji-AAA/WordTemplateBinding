using System.Globalization;
using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Conditions;

/// <summary>
/// 基于受控 JSON DSL 的条件表达式求值器。
/// </summary>
public sealed class ConditionEvaluator : IConditionEvaluator
{
    /// <inheritdoc />
    public ConditionEvaluationResult Evaluate(
        ConditionBlockDefinition definition,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(resolver);

        try
        {
            ConditionExpression expression = ParseExpression(definition.Expression);
            bool result = EvaluateExpression(expression, scope, resolver);
            return new ConditionEvaluationResult
            {
                Success = true,
                Result = result,
            };
        }
        catch (Exception ex) when (ex is not ConditionEvaluationException)
        {
            return definition.OnError switch
            {
                ConditionFailureStrategy.STRICT =>
                    throw new ConditionEvaluationException(
                        definition.BlockKey,
                        $"条件表达式求值失败：{ex.Message}",
                        ex),

                ConditionFailureStrategy.FALSE_ON_ERROR =>
                    new ConditionEvaluationResult
                    {
                        Success = false,
                        Result = false,
                        ErrorMessage = ex.Message,
                    },

                ConditionFailureStrategy.KEEP_TEMPLATE =>
                    new ConditionEvaluationResult
                    {
                        Success = false,
                        Result = true, // Keep template content
                        ErrorMessage = ex.Message,
                    },

                _ => throw new ConditionEvaluationException(
                    definition.BlockKey,
                    $"未知的失败策略：{definition.OnError}",
                    ex),
            };
        }
    }

    /// <summary>
    /// 从 JsonElement 解析条件表达式。
    /// </summary>
    private static ConditionExpression ParseExpression(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("条件表达式必须是 JSON 对象。");
        }

        if (!element.TryGetProperty("operator", out JsonElement operatorProperty) ||
            operatorProperty.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("条件表达式必须包含字符串类型的 \"operator\" 字段。");
        }

        string op = operatorProperty.GetString()!;
        if (!ConditionOperators.All.Contains(op))
        {
            throw new FormatException($"不支持的条件运算符：{op}");
        }

        ConditionOperand? left = null;
        ConditionOperand? right = null;
        ConditionOperand? operand = null;

        if (element.TryGetProperty("left", out JsonElement leftElement))
        {
            left = ParseOperand(leftElement);
        }

        if (element.TryGetProperty("right", out JsonElement rightElement))
        {
            right = ParseOperand(rightElement);
        }

        if (element.TryGetProperty("operand", out JsonElement operandElement))
        {
            operand = ParseOperand(operandElement);
        }

        return new ConditionExpression
        {
            Operator = op,
            Left = left,
            Right = right,
            Operand = operand,
        };
    }

    /// <summary>
    /// 从 JsonElement 解析条件操作数。
    /// </summary>
    private static ConditionOperand ParseOperand(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            // 可能是直接常量
            return new ConditionOperand
            {
                Constant = ConvertJsonElement(element),
            };
        }

        string? path = null;
        object? constant = null;
        ConditionExpression? nested = null;

        if (element.TryGetProperty("path", out JsonElement pathElement))
        {
            path = pathElement.GetString();
        }

        if (element.TryGetProperty("constant", out JsonElement constantElement))
        {
            constant = ConvertJsonElement(constantElement);
        }

        if (element.TryGetProperty("operator", out _))
        {
            // This is a nested expression
            nested = ParseExpression(element);
        }

        return new ConditionOperand
        {
            Path = path,
            Constant = constant,
            Nested = nested,
        };
    }

    /// <summary>
    /// 递归求值条件表达式。
    /// </summary>
    private static bool EvaluateExpression(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        return expression.Operator switch
        {
            ConditionOperators.EQ => EvaluateEquality(expression, scope, resolver, equal: true),
            ConditionOperators.NE => EvaluateEquality(expression, scope, resolver, equal: false),
            ConditionOperators.GT => EvaluateComparison(expression, scope, resolver, ConditionOperators.GT),
            ConditionOperators.GTE => EvaluateComparison(expression, scope, resolver, ConditionOperators.GTE),
            ConditionOperators.LT => EvaluateComparison(expression, scope, resolver, ConditionOperators.LT),
            ConditionOperators.LTE => EvaluateComparison(expression, scope, resolver, ConditionOperators.LTE),
            ConditionOperators.IS_NULL => EvaluateNullCheck(expression, scope, resolver, expectNull: true),
            ConditionOperators.IS_NOT_NULL => EvaluateNullCheck(expression, scope, resolver, expectNull: false),
            ConditionOperators.IS_EMPTY => EvaluateEmptyCheck(expression, scope, resolver, expectEmpty: true),
            ConditionOperators.IS_NOT_EMPTY => EvaluateEmptyCheck(expression, scope, resolver, expectEmpty: false),
            ConditionOperators.AND => EvaluateLogicalAnd(expression, scope, resolver),
            ConditionOperators.OR => EvaluateLogicalOr(expression, scope, resolver),
            ConditionOperators.NOT => EvaluateLogicalNot(expression, scope, resolver),
            _ => throw new FormatException($"未知的运算符：{expression.Operator}"),
        };
    }

    /// <summary>
    /// 解析操作数的值。
    /// </summary>
    private static object? ResolveOperandValue(
        ConditionOperand? operand,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        if (operand is null)
        {
            return null;
        }

        if (operand.Nested is not null)
        {
            return EvaluateExpression(operand.Nested, scope, resolver);
        }

        if (operand.Path is not null)
        {
            return resolver.ResolveValue(scope, operand.Path);
        }

        return operand.Constant;
    }

    /// <summary>
    /// 求值 EQ/NE 比较。
    /// </summary>
    private static bool EvaluateEquality(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver,
        bool equal)
    {
        object? leftValue = ResolveOperandValue(expression.Left, scope, resolver);
        object? rightValue = ResolveOperandValue(expression.Right, scope, resolver);

        bool areEqual = ValuesEqual(leftValue, rightValue);
        return equal ? areEqual : !areEqual;
    }

    /// <summary>
    /// 求值 GT/GTE/LT/LTE 比较。
    /// </summary>
    private static bool EvaluateComparison(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver,
        string op)
    {
        object? leftValue = ResolveOperandValue(expression.Left, scope, resolver);
        object? rightValue = ResolveOperandValue(expression.Right, scope, resolver);

        if (leftValue is null || rightValue is null)
        {
            return false;
        }

        // Try numeric comparison first
        if (TryConvertToDecimal(leftValue, out decimal leftDecimal) &&
            TryConvertToDecimal(rightValue, out decimal rightDecimal))
        {
            return op switch
            {
                ConditionOperators.GT => leftDecimal > rightDecimal,
                ConditionOperators.GTE => leftDecimal >= rightDecimal,
                ConditionOperators.LT => leftDecimal < rightDecimal,
                ConditionOperators.LTE => leftDecimal <= rightDecimal,
                _ => false,
            };
        }

        // Fall back to string comparison
        string leftStr = Convert.ToString(leftValue, CultureInfo.InvariantCulture) ?? string.Empty;
        string rightStr = Convert.ToString(rightValue, CultureInfo.InvariantCulture) ?? string.Empty;
        int cmp = string.Compare(leftStr, rightStr, StringComparison.Ordinal);

        return op switch
        {
            ConditionOperators.GT => cmp > 0,
            ConditionOperators.GTE => cmp >= 0,
            ConditionOperators.LT => cmp < 0,
            ConditionOperators.LTE => cmp <= 0,
            _ => false,
        };
    }

    /// <summary>
    /// 求值 IS_NULL/IS_NOT_NULL。
    /// </summary>
    private static bool EvaluateNullCheck(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver,
        bool expectNull)
    {
        ConditionOperand? target = expression.Left ?? expression.Operand;
        object? value = ResolveOperandValue(target, scope, resolver);

        bool isNull = value is null ||
                      (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Null);

        return expectNull ? isNull : !isNull;
    }

    /// <summary>
    /// 求值 IS_EMPTY/IS_NOT_EMPTY。
    /// </summary>
    private static bool EvaluateEmptyCheck(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver,
        bool expectEmpty)
    {
        ConditionOperand? target = expression.Left ?? expression.Operand;
        object? value = ResolveOperandValue(target, scope, resolver);

        bool isEmpty = IsEmpty(value);
        return expectEmpty ? isEmpty : !isEmpty;
    }

    /// <summary>
    /// 求值 AND。
    /// </summary>
    private static bool EvaluateLogicalAnd(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        if (expression.Left?.Nested is not null && expression.Right?.Nested is not null)
        {
            return EvaluateExpression(expression.Left.Nested, scope, resolver) &&
                   EvaluateExpression(expression.Right.Nested, scope, resolver);
        }

        bool left = EvaluateExpression(
            expression.Left?.Nested ?? new ConditionExpression { Operator = ConditionOperators.EQ },
            scope, resolver);
        if (!left)
        {
            return false;
        }

        return EvaluateExpression(
            expression.Right?.Nested ?? new ConditionExpression { Operator = ConditionOperators.EQ },
            scope, resolver);
    }

    /// <summary>
    /// 求值 OR。
    /// </summary>
    private static bool EvaluateLogicalOr(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        if (expression.Left?.Nested is not null)
        {
            if (EvaluateExpression(expression.Left.Nested, scope, resolver))
            {
                return true;
            }
        }

        if (expression.Right?.Nested is not null)
        {
            return EvaluateExpression(expression.Right.Nested, scope, resolver);
        }

        return false;
    }

    /// <summary>
    /// 求值 NOT。
    /// </summary>
    private static bool EvaluateLogicalNot(
        ConditionExpression expression,
        RenderScope scope,
        IDataContextResolver resolver)
    {
        if (expression.Operand?.Nested is not null)
        {
            return !EvaluateExpression(expression.Operand.Nested, scope, resolver);
        }

        // NOT with a value operand — treat null/false/0/empty as falsy
        object? value = ResolveOperandValue(expression.Operand, scope, resolver);
        return !IsTruthy(value);
    }

    /// <summary>
    /// 判断两个值是否相等。
    /// </summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // Try numeric comparison
        if (TryConvertToDecimal(left, out decimal leftDecimal) &&
            TryConvertToDecimal(right, out decimal rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        // Try boolean comparison
        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        // String comparison
        string leftStr = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
        string rightStr = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 尝试将对象转换为 decimal。
    /// </summary>
    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        return value switch
        {
            decimal d => (result = d) == d,
            double d => decimal.TryParse(
                d.ToString("G", CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result),
            float f => decimal.TryParse(
                f.ToString("G", CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result),
            int i => (result = i) == i,
            long l => (result = l) == l,
            short s => (result = s) == s,
            byte b => (result = b) == b,
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.Number => jsonElement.TryGetDecimal(out result),
                JsonValueKind.String => decimal.TryParse(
                    jsonElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out result),
                _ => false,
            },
            string s => decimal.TryParse(
                s,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result),
            _ => decimal.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result),
        };
    }

    /// <summary>
    /// 判断值是否为"空"。
    /// </summary>
    private static bool IsEmpty(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return value switch
        {
            string s => s.Length == 0,
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.String => jsonElement.GetString()?.Length == 0,
                JsonValueKind.Array => jsonElement.GetArrayLength() == 0,
                JsonValueKind.Object => false,
                _ => false,
            },
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false,
        };
    }

    /// <summary>
    /// 判断值是否为"真值"。
    /// </summary>
    private static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            decimal d => d != 0,
            double d => d != 0,
            string s => s.Length > 0,
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => false,
                JsonValueKind.Number => jsonElement.GetDecimal() != 0,
                JsonValueKind.String => jsonElement.GetString()?.Length > 0,
                JsonValueKind.Array => jsonElement.GetArrayLength() > 0,
                _ => true,
            },
            _ => true,
        };
    }

    /// <summary>
    /// 将 JsonElement 转换为 .NET 对象。
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDecimal(),
            _ => element.Clone(),
        };
    }
}

/// <summary>
/// 表示条件表达式求值失败。
/// </summary>
public sealed class ConditionEvaluationException : Exception
{
    /// <summary>
    /// 获取条件块键。
    /// </summary>
    public string BlockKey { get; }

    /// <summary>
    /// 初始化条件求值异常。
    /// </summary>
    public ConditionEvaluationException(
        string blockKey,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        BlockKey = blockKey;
    }
}
