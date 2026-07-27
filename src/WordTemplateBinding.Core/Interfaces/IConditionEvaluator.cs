using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 定义条件表达式求值能力。
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// 在给定作用域中求值条件表达式。
    /// </summary>
    /// <param name="definition">条件块定义。</param>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="resolver">数据上下文解析器。</param>
    /// <returns>返回求值结果。</returns>
    ConditionEvaluationResult Evaluate(
        ConditionBlockDefinition definition,
        RenderScope scope,
        IDataContextResolver resolver);
}
