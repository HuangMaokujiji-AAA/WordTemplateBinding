#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class BindingSetEndpoints
{
    public static IEndpointRouteBuilder MapBindingSetEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapBindings(endpoints);
        return endpoints;
    }

    private static void MapBindings(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder sets = endpoints.MapGroup("/api/binding-sets")
            .WithTags("Binding sets");
        sets.MapPost("/", async (
            CreateBindingSetRequest request,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.BindingSet(
                await service.GetOrCreateDraftAsync(
                    DatabaseIdParser.Required(request.ChapterId, nameof(request.ChapterId)),
                    DatabaseIdParser.Required(
                        request.TemplateVersionId,
                        nameof(request.TemplateVersionId)),
                    cancellationToken))));
        sets.MapGet("/{bindingSetId}/items", async (
            string bindingSetId,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(
                    DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                    cancellationToken))
                .Select(PersistentApiMapper.BindingItem)));
        sets.MapPut("/{bindingSetId}/items/{templateElementId}", async (
            string bindingSetId,
            string templateElementId,
            UpsertBindingItemRequest request,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.BindingItem(await service.UpsertAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                DatabaseIdParser.Required(templateElementId, nameof(templateElementId)),
                new BindingItemUpsert
                {
                    DataSourceId = DatabaseIdParser.Required(
                        request.DataSourceId,
                        nameof(request.DataSourceId)),
                    SourcePath = request.SourcePath,
                    TargetProperty = request.TargetProperty,
                    SourceKind = request.SourceKind,
                    TransformConfigJson = request.TransformConfigJson,
                    FormatConfigJson = request.FormatConfigJson,
                    FallbackValueJson = request.FallbackValueJson,
                    IsRequired = request.IsRequired,
                },
                cancellationToken))));
        sets.MapDelete("/{bindingSetId}/items/{templateElementId}", async (
            string bindingSetId,
            string templateElementId,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new
            {
                deleted = await service.DeleteAsync(
                    DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                    DatabaseIdParser.Required(templateElementId, nameof(templateElementId)),
                    cancellationToken),
            }));
        sets.MapPost("/{bindingSetId}/validate", async (
            string bindingSetId,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ValidateAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                cancellationToken)));
        sets.MapPost("/{bindingSetId}/resolve-candidates", async (
            string bindingSetId,
            string dataSourceId,
            IBindingCandidateResolver resolver,
            CancellationToken cancellationToken) =>
            Results.Ok(await resolver.ResolveAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)),
                cancellationToken)));
        sets.MapPost("/{bindingSetId}/reports", async (
            string bindingSetId,
            BindingSetDocumentService service,
            CancellationToken cancellationToken) =>
        {
            RenderedReport report = await service.GenerateReportAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                cancellationToken);
            return Results.File(
                report.GetBytesCopy(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                report.FileName);
        });
        sets.MapPost("/{bindingSetId}/export-reusable", async (
            string bindingSetId,
            BindingSetDocumentService service,
            CancellationToken cancellationToken) =>
        {
            RenderedTemplate template = await service.ExportReusableTemplateAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                cancellationToken);
            return Results.File(
                template.GetBytesCopy(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                template.FileName);
        });
        sets.MapGet("/{bindingSetId}/preview/{templateElementId}", async (
            string bindingSetId,
            string templateElementId,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
        {
            BindingPreview preview = await service.PreviewAsync(
                DatabaseIdParser.Required(bindingSetId, nameof(bindingSetId)),
                DatabaseIdParser.Required(templateElementId, nameof(templateElementId)),
                cancellationToken);
            return Results.Ok(new
            {
                templateElementId = preview.TemplateElementId.ToString(),
                preview.DisplayName,
                preview.SourcePath,
                rawValue = PersistentJson.Parse(preview.RawValueJson),
                preview.FormattedValue,
                dataType = preview.DataType.ToString(),
                snapshotId = preview.SnapshotId.ToString(),
            });
        });

        endpoints.MapGet("/api/template-elements/{templateElementId}/suggestions", async (
            string templateElementId,
            string dataSourceId,
            BindingWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.SuggestAsync(
                DatabaseIdParser.Required(templateElementId, nameof(templateElementId)),
                DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)),
                cancellationToken)));
    }

    private static class PersistentJson
    {
        internal static object? Parse(string? json) =>
            string.IsNullOrWhiteSpace(json)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<
                    System.Text.Json.JsonElement>(json);
    }
}

#pragma warning restore CS1591

