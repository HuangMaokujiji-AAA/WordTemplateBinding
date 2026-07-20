using WordTemplateBinding.Core.Enums;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示数据字段树中的一个节点。
/// </summary>
public sealed record DataFieldNode
{
    /// <summary>
    /// 获取字段显示名称。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 获取字段的唯一数据路径。
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 获取字段值类型。
    /// </summary>
    public required DataValueType Type { get; init; }

    /// <summary>
    /// 获取节点是否表示集合。
    /// </summary>
    public required bool IsCollection { get; init; }

    /// <summary>
    /// 获取节点是否为叶子字段。
    /// </summary>
    public required bool IsLeaf { get; init; }

    /// <summary>
    /// 获取当前阶段是否允许将该节点绑定到模拟数据。
    /// </summary>
    public required bool IsBindable { get; init; }

    /// <summary>
    /// 获取子节点。
    /// </summary>
    public IReadOnlyList<DataFieldNode> Children { get; init; } = Array.Empty<DataFieldNode>();
}

/// <summary>
/// 表示可按路径查找的数据字段定义。
/// </summary>
public sealed record DataFieldDefinition
{
    /// <summary>
    /// 获取字段名称。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 获取字段路径。
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 获取字段类型。
    /// </summary>
    public required DataValueType Type { get; init; }

    /// <summary>
    /// 获取当前阶段是否允许绑定。
    /// </summary>
    public required bool IsBindable { get; init; }
}

/// <summary>
/// 表示数据字段搜索结果。
/// </summary>
public sealed record DataSchemaSearchResult
{
    /// <summary>
    /// 获取返回的匹配节点。
    /// </summary>
    public required IReadOnlyList<DataFieldNode> Nodes { get; init; }

    /// <summary>
    /// 获取未截断前的匹配总数。
    /// </summary>
    public required int MatchCount { get; init; }

    /// <summary>
    /// 获取结果是否因数量限制被截断。
    /// </summary>
    public required bool IsTruncated { get; init; }
}
