#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 后端对 Word 原生图表的完整结构化分析快照。
/// 该模型独立于前端 ParsedWordChart，专门服务于数据绑定、Word 写回和开发调试。
/// </summary>
public sealed record ChartAnalysisSnapshot
{
    public required string SchemaVersion { get; init; }

    public required ChartIdentitySnapshot Identity { get; init; }

    public required ChartSourceSnapshot Source { get; init; }

    public required ChartDefinitionSnapshot Chart { get; init; }

    public required IReadOnlyList<ChartPlotGroupSnapshot> PlotGroups { get; init; }

    public required IReadOnlyList<ChartAxisSnapshot> Axes { get; init; }

    public required IReadOnlyList<ChartCategorySnapshot> Categories { get; init; }

    public required IReadOnlyList<ChartSeriesSnapshot> Series { get; init; }

    public required ChartDataTableSnapshot DataTable { get; init; }

    public required ChartBindingContract BindingContract { get; init; }

    public required ChartAnalysisDiagnostics Diagnostics { get; init; }
}

/// <summary>
/// 图表的稳定标识信息，与 ChartLocator / ChartTemplateItem / TemplateBinding / 图表 Manifest 保持一致。
/// </summary>
public sealed record ChartIdentitySnapshot
{
    public required string LocatorId { get; init; }

    public required string PartKey { get; init; }

    public required string RelationshipId { get; init; }

    public required int DocumentOrder { get; init; }
}

/// <summary>
/// 图表数据源信息，包括部件路径、公式、嵌入工作簿等。
/// </summary>
public sealed record ChartSourceSnapshot
{
    public required string ChartPartPath { get; init; }

    public string? ChartRelationshipPartPath { get; init; }

    public string? ExternalDataRelationshipId { get; init; }

    public string? EmbeddedWorkbookPath { get; init; }

    public bool EmbeddedWorkbookDetected { get; init; }

    public IReadOnlyList<ChartFormulaSnapshot> Formulas { get; init; } = Array.Empty<ChartFormulaSnapshot>();

    public IReadOnlyList<ChartCacheSummary> Caches { get; init; } = Array.Empty<ChartCacheSummary>();
}

/// <summary>
/// 表示一条图表公式引用。
/// </summary>
public sealed record ChartFormulaSnapshot
{
    public required string Role { get; init; }

    public int? SeriesIndex { get; init; }

    public required string Formula { get; init; }

    public string? SheetName { get; init; }

    public string? RangeAddress { get; init; }
}

/// <summary>
/// 表示图表缓存摘要。
/// </summary>
public sealed record ChartCacheSummary
{
    public required string Location { get; init; }

    public required int PointCount { get; init; }

    public bool HasSparsePoints { get; init; }
}

/// <summary>
/// 图表基本定义信息。
/// </summary>
public sealed record ChartDefinitionSnapshot
{
    public required string Type { get; init; }

    public required string TypeLabel { get; init; }

    public string? Title { get; init; }

    public bool SupportedForBinding { get; init; }

    public int WidthEmu { get; init; }

    public int HeightEmu { get; init; }
}

/// <summary>
/// 表示一个图表组（如柱形图组、折线图组等）。
/// </summary>
public sealed record ChartPlotGroupSnapshot
{
    public required string Id { get; init; }

    public required int Order { get; init; }

    public required string Type { get; init; }

    public string? Grouping { get; init; }

    public string? BarDirection { get; init; }

    public IReadOnlyList<string> SeriesKeys { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AxisIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 表示一个坐标轴。
/// </summary>
public sealed record ChartAxisSnapshot
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Role { get; init; }

    public string? Position { get; init; }

    public string? Title { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    public decimal? MajorUnit { get; init; }

    public decimal? MinorUnit { get; init; }

    public string? NumberFormat { get; init; }

    public bool Reversed { get; init; }

    public bool Visible { get; init; }

    public string? CrossAxisId { get; init; }
}

/// <summary>
/// 表示一个分类项，保留原始索引。
/// </summary>
public sealed record ChartCategorySnapshot
{
    public required int Index { get; init; }

    public string? Value { get; init; }

    public required string DisplayValue { get; init; }

    public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();

    public string? SourceFormula { get; init; }

    public string? NumberFormat { get; init; }

    public bool IsMissing { get; init; }
}

/// <summary>
/// 表示一个数据点，保留原始索引。
/// </summary>
public sealed record ChartDataPointSnapshot
{
    public required int Index { get; init; }

    public object? Value { get; init; }

    public string? DisplayValue { get; init; }

    public string? NumberFormat { get; init; }

    public bool IsMissing { get; init; }
}

/// <summary>
/// 表示一个图表系列的完整分析结果。
/// </summary>
public sealed record ChartSeriesSnapshot
{
    public required string Key { get; init; }

    public required int SeriesIndex { get; init; }

    public required int Order { get; init; }

    public required string Name { get; init; }

    public required string ChartType { get; init; }

    public required string PlotGroupId { get; init; }

    public required string AxisRole { get; init; }

    public IReadOnlyList<string> AxisIds { get; init; } = Array.Empty<string>();

    public string? NameFormula { get; init; }

    public string? CategoryFormula { get; init; }

    public string? ValueFormula { get; init; }

    public string? XValueFormula { get; init; }

    public string? YValueFormula { get; init; }

    public string? BubbleSizeFormula { get; init; }

    public IReadOnlyList<ChartDataPointSnapshot> Values { get; init; } = Array.Empty<ChartDataPointSnapshot>();

    public IReadOnlyList<ChartDataPointSnapshot> XValues { get; init; } = Array.Empty<ChartDataPointSnapshot>();

    public IReadOnlyList<ChartDataPointSnapshot> YValues { get; init; } = Array.Empty<ChartDataPointSnapshot>();

    public IReadOnlyList<ChartDataPointSnapshot> BubbleSizes { get; init; } = Array.Empty<ChartDataPointSnapshot>();

    public string? NumberFormat { get; init; }

    public string? DataLabelFormula { get; init; }
}

/// <summary>
/// 标准化二维数据表。
/// </summary>
public sealed record ChartDataTableSnapshot
{
    public required string Orientation { get; init; }

    public required IReadOnlyList<ChartDataColumnSnapshot> Columns { get; init; }

    public required IReadOnlyList<ChartDataRowSnapshot> Rows { get; init; }

    public required int RowCount { get; init; }

    public required int ColumnCount { get; init; }
}

/// <summary>
/// 标准化数据列定义。
/// </summary>
public sealed record ChartDataColumnSnapshot
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Role { get; init; }

    public required string ValueType { get; init; }

    public string? SeriesKey { get; init; }
}

/// <summary>
/// 标准化数据行。
/// </summary>
public sealed record ChartDataRowSnapshot
{
    public required int Index { get; init; }

    public required IReadOnlyDictionary<string, object?> Cells { get; init; }

    public required IReadOnlyDictionary<string, bool> Missing { get; init; }
}

/// <summary>
/// 图表绑定契约，描述后端 Writer 所需的输入数据格式。
/// </summary>
public sealed record ChartBindingContract
{
    public required string Mode { get; init; }

    public required string CategoryProperty { get; init; }

    public required IReadOnlyList<ChartBindingSeriesField> SeriesFields { get; init; }

    public required object SampleReplacementPayload { get; init; }

    public required ChartReportRequestExample ReportRequestExample { get; init; }
}

/// <summary>
/// 绑定契约中的系列字段定义。
/// </summary>
public sealed record ChartBindingSeriesField
{
    public required string SeriesKey { get; init; }

    public required int SeriesIndex { get; init; }

    public required string OriginalName { get; init; }

    public required string PayloadProperty { get; init; }

    public required string ValueType { get; init; }

    public required bool Required { get; init; }
}

/// <summary>
/// 供开发人员使用的不完整的报告生成请求示例。
/// </summary>
public sealed record ChartReportRequestExample
{
    public required string TemplateId { get; init; }

    public string? BoundDataPath { get; init; }

    public required string SuggestedDataPath { get; init; }

    public required IReadOnlyDictionary<string, object?> Values { get; init; }
}

/// <summary>
/// 图表分析诊断信息汇总。
/// </summary>
public sealed record ChartAnalysisDiagnostics
{
    public bool HasErrors { get; init; }

    public bool HasWarnings { get; init; }

    public int CompletenessScore { get; init; }

    public IReadOnlyList<ChartDiagnosticItem> Items { get; init; } = Array.Empty<ChartDiagnosticItem>();
}

/// <summary>
/// 一条图表分析诊断信息。
/// </summary>
public sealed record ChartDiagnosticItem
{
    public required string Code { get; init; }

    public required string Level { get; init; }

    public required string Message { get; init; }

    public string? Path { get; init; }

    public int? SeriesIndex { get; init; }

    public required bool Recoverable { get; init; }
}
#pragma warning restore CS1591
