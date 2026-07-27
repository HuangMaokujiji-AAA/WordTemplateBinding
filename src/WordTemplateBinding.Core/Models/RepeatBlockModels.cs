namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示重复块类型。
/// </summary>
public enum RepeatBlockType
{
    /// <summary>复制表格行。</summary>
    REPEAT_ROW = 1,

    /// <summary>复制段落、表格或图表块。</summary>
    REPEAT_BLOCK = 2,

    /// <summary>在蓝图中重复整个组件。</summary>
    REPEAT_COMPONENT = 3,
}

/// <summary>
/// 表示空集合时的处理策略。
/// </summary>
public enum EmptyBehavior
{
    /// <summary>删除原型内容控件。</summary>
    REMOVE_PROTOTYPE = 1,

    /// <summary>保留原型内容控件和内部内容。</summary>
    KEEP_PROTOTYPE = 2,

    /// <summary>插入空行（仅适用于 REPEAT_ROW）。</summary>
    INSERT_EMPTY_ROW = 3,

    /// <summary>报错并终止生成。</summary>
    ERROR = 4,
}

/// <summary>
/// 表示重复块的分页策略。
/// </summary>
public enum PageBreakStrategy
{
    /// <summary>不插入分页符。</summary>
    NONE = 1,

    /// <summary>在每个实例前插入分页符。</summary>
    BEFORE_EACH = 2,

    /// <summary>除第一个实例外，在每个实例前插入分页符。</summary>
    BEFORE_EACH_EXCEPT_FIRST = 3,

    /// <summary>在每个实例后插入分页符。</summary>
    AFTER_EACH = 4,

    /// <summary>除最后一个实例外，在每个实例后插入分页符。</summary>
    AFTER_EACH_EXCEPT_LAST = 5,
}

/// <summary>
/// 表示一个重复块的定义配置。
/// </summary>
public sealed record RepeatBlockDefinition
{
    /// <summary>
    /// 获取重复块在模板中的稳定键。
    /// </summary>
    public required string BlockKey { get; init; }

    /// <summary>
    /// 获取重复块类型。
    /// </summary>
    public required RepeatBlockType BlockType { get; init; }

    /// <summary>
    /// 获取数据源路径，例如 "majors"。
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 获取每个迭代项的别名，例如 "major"。
    /// </summary>
    public required string ItemAlias { get; init; }

    /// <summary>
    /// 获取用于生成稳定实例键的数据路径，例如 "majorId"。
    /// </summary>
    public required string ItemKeyPath { get; init; }

    /// <summary>
    /// 获取空集合时的处理策略。
    /// </summary>
    public EmptyBehavior EmptyBehavior { get; init; } = EmptyBehavior.REMOVE_PROTOTYPE;

    /// <summary>
    /// 获取分页策略（仅 REPEAT_BLOCK 有效）。
    /// </summary>
    public PageBreakStrategy PageBreak { get; init; } = PageBreakStrategy.NONE;
}

/// <summary>
/// 表示一次重复展开操作的结果。
/// </summary>
public sealed record RepeatExpansionResult
{
    /// <summary>
    /// 获取生成的实例数量。
    /// </summary>
    public required int InstanceCount { get; init; }

    /// <summary>
    /// 获取每个实例的运行时定位键到根元素的映射。
    /// </summary>
    public required IReadOnlyDictionary<string, object> InstanceRoots { get; init; }

    /// <summary>
    /// 获取展开期间注册的运行时模板元素。
    /// </summary>
    public required IReadOnlyList<RuntimeTemplateElement> RuntimeElements { get; init; }
}
