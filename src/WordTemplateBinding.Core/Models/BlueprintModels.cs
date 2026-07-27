using System.Text.Json;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示蓝图节点类型。
/// </summary>
public enum BlueprintNodeType
{
    /// <summary>静态组件。</summary>
    STATIC_COMPONENT = 1,

    /// <summary>重复组件。</summary>
    REPEAT_COMPONENT = 2,

    /// <summary>条件组件。</summary>
    CONDITIONAL_COMPONENT = 3,

    /// <summary>分组节点。</summary>
    GROUP = 4,

    /// <summary>插槽引用。</summary>
    SLOT_REFERENCE = 5,
}

/// <summary>
/// 表示蓝图版本状态。
/// </summary>
public enum BlueprintVersionStatus
{
    /// <summary>草稿。</summary>
    DRAFT = 1,

    /// <summary>已发布。</summary>
    PUBLISHED = 2,

    /// <summary>已归档。</summary>
    ARCHIVED = 3,
}

/// <summary>
/// 表示组件升级兼容性评估结果。
/// </summary>
public enum ComponentUpgradeCompatibility
{
    /// <summary>完全兼容。</summary>
    COMPATIBLE = 1,

    /// <summary>兼容但有警告。</summary>
    COMPATIBLE_WITH_WARNINGS = 2,

    /// <summary>不兼容，存在破坏性变更。</summary>
    BREAKING = 3,
}

// ============================================================
// 蓝图RenderPlan模型
// ============================================================

/// <summary>
/// 表示一个完整的蓝图渲染计划。
/// </summary>
public sealed record BlueprintRenderPlan
{
    /// <summary>
    /// 获取蓝图版本ID。
    /// </summary>
    public ulong BlueprintVersionId { get; init; }

    /// <summary>
    /// 获取主模板版本ID。
    /// </summary>
    public ulong MasterTemplateVersionId { get; init; }

    /// <summary>
    /// 获取组件渲染节点列表。
    /// </summary>
    public IReadOnlyList<ComponentRenderNode> Nodes { get; init; }
        = Array.Empty<ComponentRenderNode>();

    /// <summary>
    /// 获取固定的模板版本映射（模板ID → 模板版本ID）。
    /// </summary>
    public IReadOnlyDictionary<ulong, ulong> FixedTemplateVersions { get; init; }
        = new Dictionary<ulong, ulong>();
}

/// <summary>
/// 表示蓝图中的一个组件渲染节点。
/// </summary>
public sealed record ComponentRenderNode
{
    /// <summary>
    /// 获取节点稳定键。
    /// </summary>
    public string NodeKey { get; init; } = string.Empty;

    /// <summary>
    /// 获取节点类型。
    /// </summary>
    public string NodeType { get; init; } = string.Empty;

    /// <summary>
    /// 获取组件模板版本ID。
    /// </summary>
    public ulong? TemplateVersionId { get; init; }

    /// <summary>
    /// 获取目标插槽键。
    /// </summary>
    public string? TargetSlotKey { get; init; }

    /// <summary>
    /// 获取数据作用域路径。
    /// </summary>
    public string? DataScopePath { get; init; }

    /// <summary>
    /// 获取循环项别名。
    /// </summary>
    public string? ItemAlias { get; init; }

    /// <summary>
    /// 获取实例键路径。
    /// </summary>
    public string? ItemKeyPath { get; init; }

    /// <summary>
    /// 获取条件配置。
    /// </summary>
    public JsonElement? Condition { get; init; }

    /// <summary>
    /// 获取装配配置。
    /// </summary>
    public JsonElement? AssemblyConfig { get; init; }

    /// <summary>
    /// 获取子节点列表。
    /// </summary>
    public IReadOnlyList<ComponentRenderNode> Children { get; init; }
        = Array.Empty<ComponentRenderNode>();
}

// ============================================================
// 持久化模型
// ============================================================

/// <summary>
/// 表示组件契约记录。
/// </summary>
public sealed record ComponentContractRecord
{
    /// <summary>
    /// 获取模板版本ID。
    /// </summary>
    public required ulong TemplateVersionId { get; init; }

    /// <summary>
    /// 获取组件业务键。
    /// </summary>
    public required string ComponentKey { get; init; }

    /// <summary>
    /// 获取契约版本号。
    /// </summary>
    public uint ContractVersion { get; init; } = 1;

    /// <summary>
    /// 获取输入数据Schema（JSON）。
    /// </summary>
    public required string InputSchemaJson { get; init; }

    /// <summary>
    /// 获取输出元素清单（JSON）。
    /// </summary>
    public string? OutputManifestJson { get; init; }

    /// <summary>
    /// 获取输出插槽定义（JSON）。
    /// </summary>
    public string? SlotSchemaJson { get; init; }

    /// <summary>
    /// 获取内部Repeat块定义（JSON）。
    /// </summary>
    public string? RepeatSchemaJson { get; init; }

    /// <summary>
    /// 获取内部Condition块定义（JSON）。
    /// </summary>
    public string? ConditionSchemaJson { get; init; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// 表示报告蓝图记录。
/// </summary>
public sealed record BlueprintRecord
{
    /// <summary>
    /// 获取蓝图ID。
    /// </summary>
    public required ulong Id { get; init; }

    /// <summary>
    /// 获取蓝图编码。
    /// </summary>
    public required string BlueprintCode { get; init; }

    /// <summary>
    /// 获取蓝图名称。
    /// </summary>
    public required string BlueprintName { get; init; }

    /// <summary>
    /// 获取蓝图描述。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 获取蓝图状态。
    /// </summary>
    public required string BlueprintStatus { get; init; }

    /// <summary>
    /// 获取当前版本号。
    /// </summary>
    public uint CurrentVersionNo { get; init; }

    /// <summary>
    /// 获取乐观锁版本。
    /// </summary>
    public uint RowVersion { get; init; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 获取更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// 表示蓝图版本记录。
/// </summary>
public sealed record BlueprintVersionRecord
{
    /// <summary>
    /// 获取蓝图版本ID。
    /// </summary>
    public required ulong Id { get; init; }

    /// <summary>
    /// 获取蓝图ID。
    /// </summary>
    public required ulong BlueprintId { get; init; }

    /// <summary>
    /// 获取版本号。
    /// </summary>
    public required uint VersionNo { get; init; }

    /// <summary>
    /// 获取版本状态。
    /// </summary>
    public required string VersionStatus { get; init; }

    /// <summary>
    /// 获取主模板版本ID。
    /// </summary>
    public required ulong MasterTemplateVersionId { get; init; }

    /// <summary>
    /// 获取蓝图配置（JSON）。
    /// </summary>
    public string? ConfigJson { get; init; }

    /// <summary>
    /// 获取依赖哈希。
    /// </summary>
    public string? DependencyHash { get; init; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 获取发布时间。
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>
/// 表示蓝图节点记录。
/// </summary>
public sealed record BlueprintNodeRecord
{
    /// <summary>
    /// 获取节点ID。
    /// </summary>
    public required ulong Id { get; init; }

    /// <summary>
    /// 获取蓝图版本ID。
    /// </summary>
    public required ulong BlueprintVersionId { get; init; }

    /// <summary>
    /// 获取父节点ID。
    /// </summary>
    public ulong? ParentNodeId { get; init; }

    /// <summary>
    /// 获取节点稳定键。
    /// </summary>
    public required string NodeKey { get; init; }

    /// <summary>
    /// 获取节点名称。
    /// </summary>
    public required string NodeName { get; init; }

    /// <summary>
    /// 获取节点类型。
    /// </summary>
    public required string NodeType { get; init; }

    /// <summary>
    /// 获取组件模板版本ID。
    /// </summary>
    public ulong? TemplateVersionId { get; init; }

    /// <summary>
    /// 获取目标插槽键。
    /// </summary>
    public string? TargetSlotKey { get; init; }

    /// <summary>
    /// 获取数据作用域路径。
    /// </summary>
    public string? DataScopePath { get; init; }

    /// <summary>
    /// 获取循环项别名。
    /// </summary>
    public string? ItemAlias { get; init; }

    /// <summary>
    /// 获取实例键路径。
    /// </summary>
    public string? ItemKeyPath { get; init; }

    /// <summary>
    /// 获取条件配置（JSON）。
    /// </summary>
    public string? ConditionConfigJson { get; init; }

    /// <summary>
    /// 获取装配配置（JSON）。
    /// </summary>
    public string? AssemblyConfigJson { get; init; }

    /// <summary>
    /// 获取排序键。
    /// </summary>
    public decimal SortKey { get; init; }

    /// <summary>
    /// 获取是否启用。
    /// </summary>
    public bool IsEnabled { get; init; }
}
