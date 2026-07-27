using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml.Conditions;
using WordTemplateBinding.Infrastructure.OpenXml.Data;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证条件表达式求值器的各类运算符和失败策略。
/// </summary>
public sealed class ConditionEvaluatorTests
{
    private readonly IConditionEvaluator _evaluator = new ConditionEvaluator();
    private readonly IDataContextResolver _resolver = new JsonDataContextResolver();

    /// <summary>
    /// 创建包含专业数据的作用域。
    /// </summary>
    private static RenderScope CreateScope(
        bool warningEnabled = true, bool advantageEnabled = false)
    {
        string json = $$"""
        {
            "major": {
                "majorId": "080901",
                "majorName": "计算机科学与技术",
                "sections": {
                    "warningEnabled": {{(warningEnabled ? "true" : "false")}},
                    "advantageEnabled": {{(advantageEnabled ? "true" : "false")}}
                },
                "employmentRate": 95.2,
                "studentCount": 120
            }
        }
        """;

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement.Clone();

        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            ["major"] = root.GetProperty("major").Clone(),
            ["$index"] = 0,
            ["$key"] = "major-loop/080901",
        };

        return RenderScope.CreateRoot(variables);
    }

    /// <summary>
    /// 创建条件块定义。
    /// </summary>
    private static ConditionBlockDefinition CreateDefinition(
        string blockKey,
        JsonElement expression,
        ConditionFailureStrategy falseBehavior = ConditionFailureStrategy.STRICT,
        ConditionFailureStrategy onError = ConditionFailureStrategy.STRICT) =>
        new()
        {
            BlockKey = blockKey,
            Expression = expression,
            FalseBehavior = falseBehavior,
            OnError = onError,
        };

    /// <summary>
    /// 解析 JSON 字符串为 JsonElement。
    /// </summary>
    private static JsonElement ParseJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>EQ 比较为 true 时返回 true。</summary>
    [Fact]
    public void Evaluate_EQ_True_ReturnsTrue()
    {
        RenderScope scope = CreateScope(warningEnabled: true);
        JsonElement expression = ParseJson("""
        {
            "operator": "EQ",
            "left": { "path": "major.sections.warningEnabled" },
            "right": { "constant": true }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>EQ 比较为 false 时返回 false。</summary>
    [Fact]
    public void Evaluate_EQ_False_ReturnsFalse()
    {
        RenderScope scope = CreateScope(warningEnabled: false);
        JsonElement expression = ParseJson("""
        {
            "operator": "EQ",
            "left": { "path": "major.sections.warningEnabled" },
            "right": { "constant": true }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.False(result.Result);
    }

    /// <summary>NE 比较返回正确结果。</summary>
    [Fact]
    public void Evaluate_NE_ReturnsCorrect()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "NE",
            "left": { "path": "major.majorName" },
            "right": { "constant": "软件工程" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>GT 数值比较返回正确结果。</summary>
    [Fact]
    public void Evaluate_GT_Numeric_ReturnsCorrect()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "GT",
            "left": { "path": "major.employmentRate" },
            "right": { "constant": 90 }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>LT 数值比较返回正确结果。</summary>
    [Fact]
    public void Evaluate_LT_Numeric_ReturnsCorrect()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "LT",
            "left": { "path": "major.employmentRate" },
            "right": { "constant": 100 }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>GTE 相等时返回 true。</summary>
    [Fact]
    public void Evaluate_GTE_Equal_ReturnsTrue()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "GTE",
            "left": { "path": "major.studentCount" },
            "right": { "constant": 120 }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>IS_NULL 对 null 值返回 true。</summary>
    [Fact]
    public void Evaluate_IS_NULL_NullValue_ReturnsTrue()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "IS_NULL",
            "left": { "path": "major.nonExistent" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>IS_NOT_NULL 对存在的值返回 true。</summary>
    [Fact]
    public void Evaluate_IS_NOT_NULL_ExistingValue_ReturnsTrue()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "IS_NOT_NULL",
            "left": { "path": "major.majorName" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>AND 两者皆 true 时返回 true。</summary>
    [Fact]
    public void Evaluate_AND_BothTrue_ReturnsTrue()
    {
        RenderScope scope = CreateScope(warningEnabled: true, advantageEnabled: true);
        JsonElement expression = ParseJson("""
        {
            "operator": "AND",
            "left": {
                "operator": "EQ",
                "left": { "path": "major.sections.warningEnabled" },
                "right": { "constant": true }
            },
            "right": {
                "operator": "EQ",
                "left": { "path": "major.sections.advantageEnabled" },
                "right": { "constant": true }
            }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>AND 一者为 false 时返回 false。</summary>
    [Fact]
    public void Evaluate_AND_OneFalse_ReturnsFalse()
    {
        RenderScope scope = CreateScope(warningEnabled: true, advantageEnabled: false);
        JsonElement expression = ParseJson("""
        {
            "operator": "AND",
            "left": {
                "operator": "EQ",
                "left": { "path": "major.sections.warningEnabled" },
                "right": { "constant": true }
            },
            "right": {
                "operator": "EQ",
                "left": { "path": "major.sections.advantageEnabled" },
                "right": { "constant": true }
            }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.False(result.Result);
    }

    /// <summary>OR 一者为 true 时返回 true。</summary>
    [Fact]
    public void Evaluate_OR_OneTrue_ReturnsTrue()
    {
        RenderScope scope = CreateScope(warningEnabled: true, advantageEnabled: false);
        JsonElement expression = ParseJson("""
        {
            "operator": "OR",
            "left": {
                "operator": "EQ",
                "left": { "path": "major.sections.warningEnabled" },
                "right": { "constant": true }
            },
            "right": {
                "operator": "EQ",
                "left": { "path": "major.sections.advantageEnabled" },
                "right": { "constant": true }
            }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>NOT true 返回 false。</summary>
    [Fact]
    public void Evaluate_NOT_True_ReturnsFalse()
    {
        RenderScope scope = CreateScope(warningEnabled: true);
        JsonElement expression = ParseJson("""
        {
            "operator": "NOT",
            "operand": {
                "operator": "EQ",
                "left": { "path": "major.sections.warningEnabled" },
                "right": { "constant": true }
            }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.False(result.Result);
    }

    /// <summary>IS_EMPTY 对空数组返回 true。</summary>
    [Fact]
    public void Evaluate_IS_EMPTY_EmptyArray_ReturnsTrue()
    {
        string json = """{ "data": [] }""";
        using JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            ["data"] = doc.RootElement.GetProperty("data").Clone(),
        };
        RenderScope scope = RenderScope.CreateRoot(variables);

        JsonElement expression = ParseJson("""
        {
            "operator": "IS_EMPTY",
            "left": { "path": "data" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }

    /// <summary>无效运算符触发异常。</summary>
    [Fact]
    public void Evaluate_InvalidOperator_Throws()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "INVALID_OP",
            "left": { "path": "major.majorName" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition(
            "test", expression, onError: ConditionFailureStrategy.STRICT);
        Assert.Throws<ConditionEvaluationException>(
            () => _evaluator.Evaluate(def, scope, _resolver));
    }

    /// <summary>FALSE_ON_ERROR 策略在错误时返回成功为 false 的结果。</summary>
    [Fact]
    public void Evaluate_OnErrorFalseOnError_ReturnsSuccessFalse()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "INVALID_OP",
            "left": { "path": "major.majorName" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition(
            "test", expression, onError: ConditionFailureStrategy.FALSE_ON_ERROR);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.False(result.Success);
        Assert.False(result.Result);
    }

    /// <summary>数值比较应使用数值语义而非字符串词法比较。</summary>
    [Fact]
    public void Evaluate_NumericStringComparison_UsesNumericNotLexical()
    {
        RenderScope scope = CreateScope();
        JsonElement expression = ParseJson("""
        {
            "operator": "GT",
            "left": { "path": "major.employmentRate" },
            "right": { "constant": "2" }
        }
        """);
        ConditionBlockDefinition def = CreateDefinition("test", expression);
        ConditionEvaluationResult result = _evaluator.Evaluate(def, scope, _resolver);
        Assert.True(result.Success);
        Assert.True(result.Result);
    }
}
