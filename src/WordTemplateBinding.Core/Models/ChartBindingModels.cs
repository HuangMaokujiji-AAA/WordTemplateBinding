#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 精简图表数据定义，仅包含绑定和写回所需的核心结构。
/// 不包含颜色、坐标轴、图例、样式等保持不变的元素。
/// </summary>
public sealed record ChartDataDefinition
{
    public required string SchemaVersion { get; init; }

    public required string LocatorId { get; init; }

    public required string PartKey { get; init; }

    public required string RelationshipId { get; init; }

    public required int DocumentOrder { get; init; }

    public required string ChartType { get; init; }

    public required string DataMode { get; init; }

    public required ChartCategoryDefinition Category { get; init; }

    public required IReadOnlyList<ChartSeriesDefinition> Series { get; init; }

    public required IReadOnlyList<ChartDataRowSnapshot> CurrentData { get; init; }

    public required string WriteCapability { get; init; }

    public IReadOnlyList<ChartDiagnosticItem> Diagnostics { get; init; } = Array.Empty<ChartDiagnosticItem>();
}

/// <summary>
/// 分类定义：名称、公式位置、当前值。
/// </summary>
public sealed record ChartCategoryDefinition
{
    public required string Name { get; init; }

    public string? Formula { get; init; }

    public string? SheetName { get; init; }

    public string? StartCell { get; init; }

    public string? EndCell { get; init; }

    public required IReadOnlyList<string?> Values { get; init; }
}

/// <summary>
/// 系列定义：名称、公式位置、当前值。
/// </summary>
public sealed record ChartSeriesDefinition
{
    public required int SeriesIndex { get; init; }

    public required string SeriesKey { get; init; }

    public required string Name { get; init; }

    public string? NameFormula { get; init; }

    public string? NameSheetName { get; init; }

    public string? NameCell { get; init; }

    public string? ValueFormula { get; init; }

    public string? ValueSheetName { get; init; }

    public string? ValueStartCell { get; init; }

    public string? ValueEndCell { get; init; }

    public required IReadOnlyList<decimal?> Values { get; init; }

    public string? NumberFormat { get; init; }
}

/// <summary>
/// 图表字段映射配置：将数据源字段映射到图表的分类和系列。
/// </summary>
public sealed record ChartBindingMapping
{
    public required string Mode { get; init; }

    public required string CategoryField { get; init; }

    public required IReadOnlyList<ChartSeriesFieldMapping> SeriesMappings { get; init; }
}

/// <summary>
/// 单个系列的字段映射。
/// </summary>
public sealed record ChartSeriesFieldMapping
{
    public required int SeriesIndex { get; init; }

    public required string SeriesKey { get; init; }

    public string TemplateSeriesName { get; init; } = string.Empty;

    public required string ValueField { get; init; }

    public string? SeriesNameField { get; init; }
}

/// <summary>
/// 标准化图表数据：经过绑定映射转换后，可直接用于 Writer 的数据结构。
/// </summary>
public sealed record NormalizedChartData
{
    public required IReadOnlyList<string?> Categories { get; init; }

    public required IReadOnlyList<NormalizedChartSeries> Series { get; init; }
}

/// <summary>
/// 标准化系列数据。
/// </summary>
public sealed record NormalizedChartSeries
{
    public required int SeriesIndex { get; init; }

    public required string SeriesKey { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<decimal?> Values { get; init; }
}
#pragma warning restore CS1591
