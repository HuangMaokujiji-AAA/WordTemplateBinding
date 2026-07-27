using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供组件和报告蓝图的 REST API 端点。
/// </summary>
public static class BlueprintEndpoints
{
    /// <summary>
    /// 映射蓝图相关端点。
    /// </summary>
    public static IEndpointRouteBuilder MapBlueprintEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder blueprints = endpoints.MapGroup("/api/report-blueprints")
            .WithTags("Blueprints");

        blueprints.MapGet("/", ListBlueprintsAsync);
        blueprints.MapPost("/", CreateBlueprintAsync);
        blueprints.MapGet("/{id:regex(^[0-9]+$)}", GetBlueprintAsync);

        blueprints.MapGet("/{id:regex(^[0-9]+$)}/versions",
            ListBlueprintVersionsAsync);
        blueprints.MapPost("/{id:regex(^[0-9]+$)}/versions",
            CreateBlueprintVersionAsync);

        RouteGroupBuilder versions = endpoints.MapGroup("/api/report-blueprint-versions")
            .WithTags("Blueprint versions");

        versions.MapGet("/{id:regex(^[0-9]+$)}", GetBlueprintVersionAsync);
        versions.MapGet("/{id:regex(^[0-9]+$)}/nodes", GetNodesAsync);
        versions.MapPut("/{id:regex(^[0-9]+$)}/nodes", UpdateNodesAsync);
        versions.MapPost("/{id:regex(^[0-9]+$)}/validate", ValidateAsync);
        versions.MapPost("/{id:regex(^[0-9]+$)}/publish", PublishAsync);

        return endpoints;
    }

    /// <summary>列出蓝图。</summary>
    private static async Task<IResult> ListBlueprintsAsync(
        string? query,
        int page,
        int pageSize,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        PagedResult<BlueprintRecord> result = await service.ListAsync(
            query, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
            cancellationToken);
        return Results.Ok(new
        {
            items = result.Items,
            result.Total,
            result.Page,
            result.PageSize,
        });
    }

    /// <summary>创建蓝图。</summary>
    private static async Task<IResult> CreateBlueprintAsync(
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        // In a real app, read from request body. For now use auto-generated code.
        string code = $"BP_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        BlueprintRecord record = await service.CreateAsync(
            code, "新建蓝图", null, null, cancellationToken);
        return Results.Ok(record);
    }

    /// <summary>获取蓝图。</summary>
    private static async Task<IResult> GetBlueprintAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        BlueprintRecord? record = await service.GetAsync(id, cancellationToken);
        return record is not null ? Results.Ok(record) : Results.NotFound();
    }

    /// <summary>列出蓝图版本。</summary>
    private static Task<IResult> ListBlueprintVersionsAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Results.Ok(Array.Empty<object>()));
    }

    /// <summary>创建蓝图版本草稿。</summary>
    private static Task<IResult> CreateBlueprintVersionAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Results.Ok(new { message = "使用 POST /api/report-blueprint-versions 创建版本" }));
    }

    /// <summary>获取蓝图版本。</summary>
    private static Task<IResult> GetBlueprintVersionAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Results.Ok(new { id, message = "Blueprint version endpoint" }));
    }

    /// <summary>获取节点列表。</summary>
    private static async Task<IResult> GetNodesAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BlueprintNodeRecord> nodes = await service.GetNodesAsync(id, cancellationToken);
        return Results.Ok(new { items = nodes });
    }

    /// <summary>更新节点列表。</summary>
    private static Task<IResult> UpdateNodesAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Results.Ok(new { message = "使用 PUT 更新节点" }));
    }

    /// <summary>验证蓝图版本。</summary>
    private static async Task<IResult> ValidateAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        BlueprintValidationResult result = await service.ValidateAsync(id, cancellationToken);
        return Results.Ok(result);
    }

    /// <summary>发布蓝图版本。</summary>
    private static async Task<IResult> PublishAsync(
        ulong id,
        BlueprintService service,
        CancellationToken cancellationToken)
    {
        BlueprintVersionRecord published = await service.PublishAsync(id, null, cancellationToken);
        return Results.Ok(published);
    }
}
