namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示 OpenXML 文档内元素的运行时定位信息。
/// 用于在重复块展开后唯一标识每个克隆实例中的元素。
/// </summary>
public sealed record OpenXmlRuntimeLocator
{
    /// <summary>
    /// 获取运行时定位器标识，格式为 {componentNodeKey}/{instanceKey}/{elementKey}。
    /// </summary>
    public required string RuntimeLocatorId { get; init; }

    /// <summary>
    /// 获取组件节点键。
    /// </summary>
    public required string ComponentNodeKey { get; init; }

    /// <summary>
    /// 获取实例键。
    /// </summary>
    public required string InstanceKey { get; init; }

    /// <summary>
    /// 获取元素键。
    /// </summary>
    public required string ElementKey { get; init; }

    /// <summary>
    /// 获取所属的块键（若来自重复块）。
    /// </summary>
    public string? BlockKey { get; init; }

    /// <summary>
    /// 获取数据作用域路径。
    /// </summary>
    public string? DataScopePath { get; init; }
}

/// <summary>
/// 表示运行时模板元素，用于在渲染时将数据绑定到正确的克隆实例。
/// </summary>
public sealed record RuntimeTemplateElement
{
    /// <summary>
    /// 获取运行时定位器标识。
    /// </summary>
    public required string RuntimeLocatorId { get; init; }

    /// <summary>
    /// 获取源模板元素的稳定键。
    /// </summary>
    public required string SourceElementKey { get; init; }

    /// <summary>
    /// 获取组件节点键。
    /// </summary>
    public string ComponentNodeKey { get; init; } = string.Empty;

    /// <summary>
    /// 获取实例键。
    /// </summary>
    public required string InstanceKey { get; init; }

    /// <summary>
    /// 获取数据作用域路径。
    /// </summary>
    public string DataScopePath { get; init; } = string.Empty;

    /// <summary>
    /// 获取运行时定位器。
    /// </summary>
    public required OpenXmlRuntimeLocator Locator { get; init; }
}
