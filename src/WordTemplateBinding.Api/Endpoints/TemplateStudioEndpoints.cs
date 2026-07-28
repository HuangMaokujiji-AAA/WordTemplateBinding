#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class TemplateStudioEndpoints
{
    public static IEndpointRouteBuilder MapTemplateStudioEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder studio = endpoints
            .MapGroup("/api/template-studio")
            .WithTags("Template studio");
        studio.MapGet(
            "/{templateId:regex(^[0-9]+$)}",
            GetWorkspaceAsync);

        endpoints.MapGet(
            "/api/template-releases",
            ListPublishedTemplatesAsync)
            .WithTags("Template releases");
        return endpoints;
    }

    private static async Task<IResult> GetWorkspaceAsync(
        string templateId,
        string? versionId,
        string? bindingSetId,
        TemplateStudioService service,
        CancellationToken cancellationToken)
    {
        TemplateStudioWorkspace workspace = await service.GetAsync(
            DatabaseIdParser.Required(templateId, nameof(templateId)),
            DatabaseIdParser.Optional(versionId, nameof(versionId)),
            DatabaseIdParser.Optional(bindingSetId, nameof(bindingSetId)),
            cancellationToken);
        return Results.Ok(new
        {
            versionView = PersistentApiMapper.VersionView(
                workspace.VersionView),
            segments = workspace.Segments.Select(
                PersistentApiMapper.Segment),
            outline = PersistentApiMapper.SegmentOutline(
                workspace.Outline),
            summary = new
            {
                workspace.Summary.SegmentCount,
                workspace.Summary.ElementCount,
                workspace.Summary.ValidElementCount,
                workspace.Summary.WarningElementCount,
                workspace.Summary.UnsupportedElementCount,
                workspace.Summary.ChartCount,
                workspace.Summary.BoundElementCount,
                workspace.Summary.RequiredMissingCount,
            },
        });
    }

    private static IResult ListPublishedTemplatesAsync() =>
        Results.Ok(new
        {
            items = Array.Empty<object>(),
            publishingAvailable = false,
            message = "模板发布将在发布闭环启用后提供；READY 解析版本不会被误当作正式发布版本。",
        });
}

#pragma warning restore CS1591
