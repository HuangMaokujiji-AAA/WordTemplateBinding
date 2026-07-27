namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示渲染过程中的数据作用域，支持父子嵌套和循环变量。
/// </summary>
public sealed record RenderScope
{
    /// <summary>
    /// 获取父作用域；根作用域为 <see langword="null"/>。
    /// </summary>
    public RenderScope? Parent { get; init; }

    /// <summary>
    /// 获取当前作用域内的变量映射。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// 获取当前实例的稳定键（仅在循环作用域内有效）。
    /// </summary>
    public string InstanceKey { get; init; } = string.Empty;

    /// <summary>
    /// 获取当前实例在其集合中的索引（仅在循环作用域内有效）。
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// 使用指定的变量创建根作用域。
    /// </summary>
    /// <param name="variables">根变量映射。</param>
    /// <returns>返回没有父级的根作用域。</returns>
    public static RenderScope CreateRoot(
        IReadOnlyDictionary<string, object?> variables) =>
        new()
        {
            Parent = null,
            Variables = variables,
            InstanceKey = string.Empty,
            Index = null,
        };

    /// <summary>
    /// 在当前作用域下创建一个子循环作用域。
    /// </summary>
    /// <param name="itemAlias">循环项的变量名。</param>
    /// <param name="item">当前迭代项。</param>
    /// <param name="index">当前迭代索引。</param>
    /// <param name="instanceKey">稳定实例键。</param>
    /// <returns>返回包含父级引用的子作用域。</returns>
    public RenderScope CreateChild(
        string itemAlias,
        object? item,
        int index,
        string instanceKey)
    {
        Dictionary<string, object?> childVars = new(StringComparer.Ordinal)
        {
            [itemAlias] = item,
            ["$index"] = index,
            ["$key"] = instanceKey,
            ["$parent"] = this,
        };

        return new RenderScope
        {
            Parent = this,
            Variables = childVars,
            InstanceKey = instanceKey,
            Index = index,
        };
    }

    /// <summary>
    /// 在当前作用域下创建一个带有额外变量的子作用域。
    /// </summary>
    /// <param name="extraVariables">额外的变量。</param>
    /// <returns>返回包含父级引用的子作用域。</returns>
    public RenderScope CreateChild(
        IReadOnlyDictionary<string, object?> extraVariables)
    {
        Dictionary<string, object?> merged = new(Variables, StringComparer.Ordinal);
        foreach ((string key, object? value) in extraVariables)
        {
            merged[key] = value;
        }

        return new RenderScope
        {
            Parent = Parent,
            Variables = merged,
            InstanceKey = InstanceKey,
            Index = Index,
        };
    }
}
