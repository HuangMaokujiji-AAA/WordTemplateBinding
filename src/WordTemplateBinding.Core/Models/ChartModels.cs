using WordTemplateBinding.Core.Enums;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示 Word 原生图表部件的稳定位置。
/// </summary>
public sealed record ChartLocator
{
    /// <summary>
    /// 获取 ChartPart 在 DOCX 包中的稳定 URI。
    /// </summary>
    public required string PartKey { get; init; }

    /// <summary>
    /// 获取主文档到图表部件的关系标识。
    /// </summary>
    public required string RelationshipId { get; init; }

    /// <summary>
    /// 获取图表在主文档中的出现顺序。
    /// </summary>
    public required int DocumentOrder { get; init; }
}

/// <summary>
/// 表示模板图表中的一个数据系列。
/// </summary>
public sealed record ChartSeriesTemplate
{
    /// <summary>
    /// 获取系列顺序。
    /// </summary>
    public required int SeriesIndex { get; init; }

    /// <summary>
    /// 获取系列名称。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 获取扫描时图表缓存中的数值。
    /// </summary>
    public required IReadOnlyList<decimal?> Values { get; init; }
}

/// <summary>
/// 表示扫描得到的一个可绑定 Word 原生图表。
/// </summary>
public sealed record ChartTemplateItem
{
    /// <summary>
    /// 获取由图表定位信息生成的稳定标识。
    /// </summary>
    public required string LocatorId { get; init; }

    /// <summary>
    /// 获取图表定位信息。
    /// </summary>
    public required ChartLocator Locator { get; init; }

    /// <summary>
    /// 获取图表类型。
    /// </summary>
    public required string ChartType { get; init; }

    /// <summary>
    /// 获取图表标题；没有内置标题时使用顺序生成的名称。
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 获取分类轴缓存值。
    /// </summary>
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>
    /// 获取图表系列。
    /// </summary>
    public required IReadOnlyList<ChartSeriesTemplate> Series { get; init; }

    /// <summary>
    /// 获取该图表是否具有可写的系列缓存。
    /// </summary>
    public required bool IsBindable { get; init; }

    /// <summary>
    /// 获取当前响应视图中是否已经绑定。
    /// </summary>
    public required bool IsBound { get; init; }

    /// <summary>
    /// 获取当前响应视图中绑定的集合字段路径。
    /// </summary>
    public string? BoundDataPath { get; init; }

    /// <summary>
    /// 获取后端深度图表分析快照；PartKey 解析失败时为 null。
    /// </summary>
    public ChartAnalysisSnapshot? Analysis { get; init; }

    /// <summary>
    /// 获取精简图表数据定义，用于绑定配置和写回。
    /// </summary>
    public ChartDataDefinition? DataDefinition { get; init; }
}
