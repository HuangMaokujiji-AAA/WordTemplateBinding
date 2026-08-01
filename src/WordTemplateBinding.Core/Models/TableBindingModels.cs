#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Models;

/// <summary>Word 主文档中表格的稳定位置。</summary>
public sealed record TableLocator
{
    public required string PartKey { get; init; }

    public required int TableIndex { get; init; }

    public required int FirstParagraphIndex { get; init; }

    public required string HeaderSignature { get; init; }
}

/// <summary>扫描时识别出的表格列。</summary>
public sealed record TableColumnTemplate
{
    public required int ColumnIndex { get; init; }

    public required string Header { get; init; }

    public string? SuggestedField { get; init; }
}

/// <summary>扫描得到的可绑定 Word 表格。</summary>
public sealed record TableTemplateItem
{
    public required string LocatorId { get; init; }

    public required TableLocator Locator { get; init; }

    public required string Title { get; init; }

    public string? ContextLabel { get; init; }

    public string? SuggestedSourcePath { get; init; }

    public required int HeaderRowCount { get; init; }

    public required int TemplateRowCount { get; init; }

    public required IReadOnlyList<TableColumnTemplate> Columns { get; init; }

    public required bool IsBindable { get; init; }

    public required bool IsBound { get; init; }

    public string? BoundDataPath { get; init; }
}

/// <summary>表格单列与数组行对象字段的映射。</summary>
public sealed record TableColumnBinding
{
    public required int ColumnIndex { get; init; }

    public required string SourceField { get; init; }

    public string? Header { get; init; }

    public string? FallbackValue { get; init; }
}

/// <summary>数组数据写入 Word 表格时使用的映射与过滤规则。</summary>
public sealed record TableBindingMapping
{
    public int HeaderRowCount { get; init; } = 1;

    public required IReadOnlyList<TableColumnBinding> Columns { get; init; }

    public string? FilterField { get; init; }

    public string? FilterValue { get; init; }
}
#pragma warning restore CS1591
