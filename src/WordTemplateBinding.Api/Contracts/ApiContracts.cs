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
    int BindingCount,
    IReadOnlyList<MockItemResponse> MockItems,
    PreviewResponse Preview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    string DataPath);

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
    string LocatorId,
    string DataPath,
    DataValueType DataType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    IReadOnlyList<DataFieldNodeResponse> Children);

/// <summary>
/// 表示生成报告的请求。
/// </summary>
internal sealed record GenerateReportRequest(
    Guid TemplateId,
    Dictionary<string, JsonElement>? Values);

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
                    binding is not null,
                    binding?.DataPath,
                    binding?.DataType);
            })
            .ToList()
            .AsReadOnly();

        return new TemplateResponse(
            template.Id,
            template.OriginalFileName,
            template.ContentHash,
            mockItems.Count,
            bindings.Count,
            mockItems,
            ToResponse(template.ScanResult.Preview),
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
            binding.LocatorId,
            binding.DataPath,
            binding.DataType,
            binding.CreatedAt,
            binding.UpdatedAt);

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
