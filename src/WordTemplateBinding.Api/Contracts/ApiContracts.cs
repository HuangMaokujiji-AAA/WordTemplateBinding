using System.Text.Json;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Api.Contracts;

/// <summary>
/// 表示模板上传或查询接口的响应。
/// </summary>
internal sealed record TemplateResponse(
    Guid TemplateId,
    string FileName,
    string ContentHash,
    int MockItemCount,
    int ChartCount,
    int BindingCount,
    IReadOnlyList<MockItemResponse> MockItems,
    IReadOnlyList<ChartItemResponse> Charts,
    PreviewResponse Preview,
    TemplateImportSummaryResponse ImportSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 表示复用模板上传或重扫后的自动绑定恢复摘要。
/// </summary>
internal sealed record TemplateImportSummaryResponse(
    int TextBindingsRestored,
    int ChartBindingsRestored,
    IReadOnlyList<string> UnresolvedPlaceholders,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 表示 API 返回的模拟数据项及其当前绑定状态。
/// </summary>
internal sealed record MockItemResponse(
    string LocatorId,
    string MockValue,
    MockDataType DataType,
    TextLocatorResponse Locator,
    string ParagraphText,
    int PreviewParagraphIndex,
    string? PlaceholderCandidatePath,
    bool IsBound,
    string? BoundDataPath,
    DataValueType? BoundDataType);

/// <summary>
/// 表示 API 返回的结构化文本定位。
/// </summary>
internal sealed record TextLocatorResponse(
    DocumentPartKind PartKind,
    string PartKey,
    int ParagraphIndex,
    int StartOffset,
    int Length,
    int OccurrenceIndex,
    string OriginalValue,
    string ContextHash);

/// <summary>
/// 表示 API 返回的可绑定 Word 原生图表。
/// </summary>
internal sealed record ChartItemResponse(
    string LocatorId,
    ChartLocatorResponse Locator,
    string ChartType,
    string Title,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ChartSeriesResponse> Series,
    bool IsBindable,
    bool IsBound,
    string? BoundDataPath,
    DataValueType? BoundDataType,
    ChartAnalysisSnapshot? Analysis,
    ChartDataDefinition? DataDefinition,
    ChartBindingMappingResponse? ChartMapping);

/// <summary>
/// 表示 API 返回的图表定位。
/// </summary>
internal sealed record ChartLocatorResponse(
    string PartKey,
    string RelationshipId,
    int DocumentOrder);

/// <summary>
/// 表示 API 返回的图表系列摘要。
/// </summary>
internal sealed record ChartSeriesResponse(
    int SeriesIndex,
    string Name,
    IReadOnlyList<decimal?> Values);

/// <summary>
/// 表示 API 返回的结构化预览。
/// </summary>
internal sealed record PreviewResponse(IReadOnlyList<PreviewParagraphResponse> Paragraphs);

/// <summary>
/// 表示 API 返回的预览段落。
/// </summary>
internal sealed record PreviewParagraphResponse(
    int ParagraphIndex,
    string Text,
    IReadOnlyList<PreviewHighlightResponse> Highlights);

/// <summary>
/// 表示 API 返回的预览高亮范围。
/// </summary>
internal sealed record PreviewHighlightResponse(
    string LocatorId,
    int StartOffset,
    int Length,
    string MockValue);

/// <summary>
/// 表示创建或覆盖绑定的请求。
/// </summary>
internal sealed record UpsertBindingRequest(
    Guid TemplateId,
    string LocatorId,
    string DataPath,
    ChartBindingMappingRequest? ChartMapping);

/// <summary>
/// 表示绑定请求中的图表字段映射。
/// </summary>
internal sealed record ChartBindingMappingRequest(
    string Mode,
    string CategoryField,
    IReadOnlyList<ChartSeriesFieldMappingRequest> SeriesMappings);

/// <summary>
/// 表示绑定请求中的单个系列字段映射。
/// </summary>
internal sealed record ChartSeriesFieldMappingRequest(
    int SeriesIndex,
    string SeriesKey,
    string ValueField,
    string? SeriesNameField);

/// <summary>
/// 表示绑定接口响应。
/// </summary>
internal sealed record BindingOperationResponse(bool Success, BindingResponse Binding);

/// <summary>
/// 表示删除绑定接口响应。
/// </summary>
internal sealed record DeleteBindingResponse(bool Success, bool Deleted);

/// <summary>
/// 表示一条绑定关系的 API 响应。
/// </summary>
internal sealed record BindingResponse(
    Guid TemplateId,
    BindingTargetKind TargetKind,
    string LocatorId,
    string DataPath,
    DataValueType DataType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ChartBindingMappingResponse? ChartMapping);

/// <summary>
/// 表示 API 返回的图表字段映射。
/// </summary>
internal sealed record ChartBindingMappingResponse(
    string Mode,
    string CategoryField,
    IReadOnlyList<ChartSeriesFieldMappingResponse> SeriesMappings);

/// <summary>
/// 表示 API 返回的单个系列字段映射。
/// </summary>
internal sealed record ChartSeriesFieldMappingResponse(
    int SeriesIndex,
    string SeriesKey,
    string TemplateSeriesName,
    string ValueField,
    string? SeriesNameField);

/// <summary>
/// 表示字段树或字段搜索接口响应。
/// </summary>
internal sealed record DataSchemaResponse(
    string? Query,
    int TotalLeafCount,
    int MatchCount,
    bool IsTruncated,
    IReadOnlyList<DataFieldNodeResponse> Nodes);

/// <summary>
/// 表示 API 返回的数据字段节点。
/// </summary>
internal sealed record DataFieldNodeResponse(
    string Name,
    string Path,
    DataValueType Type,
    bool IsCollection,
    bool IsLeaf,
    bool IsBindable,
    string? Comment,
    bool? IsNullable,
    string? SampleValueJson,
    IReadOnlyList<DataFieldNodeResponse> Children);

/// <summary>
/// 表示生成报告的请求。
/// </summary>
internal sealed record GenerateReportRequest(
    Guid TemplateId,
    Dictionary<string, JsonElement>? Values);

/// <summary>
/// 表示全部图表分析 JSON 响应。
/// </summary>
internal sealed record ChartsAnalysisResponse(
    Guid TemplateId,
    string FileName,
    int ChartCount,
    IReadOnlyList<ChartItemResponse> Charts);

/// <summary>
/// 提供领域模型到 API DTO 的集中映射。
/// </summary>
internal static class ApiContractMapper
{
    /// <summary>
    /// 将模板与绑定快照合并为模板响应。
    /// </summary>
    /// <param name="template">模板快照。</param>
    /// <param name="bindings">当前绑定快照。</param>
    /// <returns>返回模板 API 响应。</returns>
    internal static TemplateResponse ToResponse(
        TemplateDocument template,
        IReadOnlyList<TemplateBinding> bindings)
    {
        IReadOnlyDictionary<string, TemplateBinding> bindingsByLocator = bindings
            .ToDictionary(binding => binding.LocatorId, StringComparer.Ordinal);
        IReadOnlyList<MockItemResponse> mockItems = template.ScanResult.MockItems
            .Select(item =>
            {
                bindingsByLocator.TryGetValue(item.LocatorId, out TemplateBinding? binding);
                return new MockItemResponse(
                    item.LocatorId,
                    item.MockValue,
                    item.DataType,
                    ToResponse(item.Locator),
                    item.ParagraphText,
                    item.PreviewParagraphIndex,
                    item.PlaceholderCandidatePath,
                    binding is not null,
                    binding?.DataPath,
                    binding?.DataType);
            })
            .ToList()
            .AsReadOnly();
        IReadOnlyList<ChartItemResponse> charts = template.ScanResult.Charts
            .Select(item =>
            {
                bindingsByLocator.TryGetValue(item.LocatorId, out TemplateBinding? binding);
                return new ChartItemResponse(
                    item.LocatorId,
                    new ChartLocatorResponse(
                        item.Locator.PartKey,
                        item.Locator.RelationshipId,
                        item.Locator.DocumentOrder),
                    item.ChartType,
                    item.Title,
                    item.Categories,
                    item.Series.Select(series => new ChartSeriesResponse(
                            series.SeriesIndex,
                            series.Name,
                            series.Values))
                        .ToList()
                        .AsReadOnly(),
                    item.IsBindable,
                    binding is not null,
                    binding?.DataPath,
                    binding?.DataType,
                    item.Analysis,
                    item.DataDefinition,
                    binding?.ChartMapping is not null
                        ? new ChartBindingMappingResponse(
                            binding.ChartMapping.Mode,
                            binding.ChartMapping.CategoryField,
                            binding.ChartMapping.SeriesMappings.Select(sm =>
                                new ChartSeriesFieldMappingResponse(
                                    sm.SeriesIndex,
                                    sm.SeriesKey,
                                    sm.TemplateSeriesName,
                                    sm.ValueField,
                                    sm.SeriesNameField)).ToList().AsReadOnly())
                        : null);
            })
            .ToList()
            .AsReadOnly();

        return new TemplateResponse(
            template.Id,
            template.OriginalFileName,
            template.ContentHash,
            mockItems.Count,
            charts.Count,
            bindings.Count,
            mockItems,
            charts,
            ToResponse(template.ScanResult.Preview),
            new TemplateImportSummaryResponse(
                template.ImportSummary.TextBindingsRestored,
                template.ImportSummary.ChartBindingsRestored,
                template.ImportSummary.UnresolvedPlaceholders,
                template.ImportSummary.Warnings),
            template.CreatedAt,
            template.UpdatedAt);
    }

    /// <summary>
    /// 将绑定领域模型转换为 API 响应。
    /// </summary>
    /// <param name="binding">绑定领域模型。</param>
    /// <returns>返回绑定响应。</returns>
    internal static BindingResponse ToResponse(TemplateBinding binding) =>
        new(
            binding.TemplateId,
            binding.TargetKind,
            binding.LocatorId,
            binding.DataPath,
            binding.DataType,
            binding.CreatedAt,
            binding.UpdatedAt,
            binding.ChartMapping is not null
                ? new ChartBindingMappingResponse(
                    binding.ChartMapping.Mode,
                    binding.ChartMapping.CategoryField,
                    binding.ChartMapping.SeriesMappings.Select(sm =>
                        new ChartSeriesFieldMappingResponse(
                            sm.SeriesIndex,
                            sm.SeriesKey,
                            sm.TemplateSeriesName,
                            sm.ValueField,
                            sm.SeriesNameField)).ToList().AsReadOnly())
                : null);

    /// <summary>
    /// 将数据字段树转换为 API 响应树。
    /// </summary>
    /// <param name="nodes">字段节点。</param>
    /// <returns>返回字段节点响应。</returns>
    internal static IReadOnlyList<DataFieldNodeResponse> ToResponse(
        IReadOnlyList<DataFieldNode> nodes) =>
        nodes.Select(node => new DataFieldNodeResponse(
                node.Name,
                node.Path,
                node.Type,
                node.IsCollection,
                node.IsLeaf,
                node.IsBindable,
                node.Comment,
                node.IsNullable,
                node.SampleValueJson,
                ToResponse(node.Children)))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// 将结构化定位模型转换为 API 响应。
    /// </summary>
    /// <param name="locator">结构化定位模型。</param>
    /// <returns>返回定位响应。</returns>
    private static TextLocatorResponse ToResponse(TextLocator locator) =>
        new(
            locator.PartKind,
            locator.PartKey,
            locator.ParagraphIndex,
            locator.StartOffset,
            locator.Length,
            locator.OccurrenceIndex,
            locator.OriginalValue,
            locator.ContextHash);

    /// <summary>
    /// 将预览领域模型转换为 API 响应。
    /// </summary>
    /// <param name="preview">结构化文档预览。</param>
    /// <returns>返回预览响应。</returns>
    private static PreviewResponse ToResponse(DocumentPreview preview) =>
        new(preview.Paragraphs.Select(paragraph => new PreviewParagraphResponse(
                paragraph.ParagraphIndex,
                paragraph.Text,
                paragraph.Highlights.Select(highlight => new PreviewHighlightResponse(
                        highlight.LocatorId,
                        highlight.StartOffset,
                        highlight.Length,
                        highlight.MockValue))
                    .ToList()
                    .AsReadOnly()))
            .ToList()
            .AsReadOnly());
}
