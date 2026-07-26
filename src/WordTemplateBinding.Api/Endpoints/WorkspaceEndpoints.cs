#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapProjects(endpoints);
        MapChapterEndpoints(endpoints);
        MapConnections(endpoints);
        MapSources(endpoints);
        MapBindings(endpoints);
        return endpoints;
    }

    private static void MapProjects(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder projects = endpoints.MapGroup("/api/projects")
            .WithTags("Projects");

        // GET /api/projects — list or search projects
        projects.MapGet("/", async (
            string? query,
            string? status,
            int page,
            int pageSize,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
        {
            PagedResult<ProjectRecord> result = await service.QueryProjectsAsync(
                query, status,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
                cancellationToken);
            return Results.Ok(new
            {
                items = result.Items.Select(PersistentApiMapper.Project),
                result.Total,
                result.Page,
                result.PageSize,
            });
        });

        // POST /api/projects — create project
        projects.MapPost("/", async (
            CreateProjectRequest request,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
        {
            ProjectRecord project = await service.CreateProjectAsync(
                request.ProjectCode,
                request.ProjectName,
                request.Description,
                cancellationToken);
            return Results.Created(
                $"/api/projects/{project.Id}",
                PersistentApiMapper.Project(project));
        });

        // GET /api/projects/{projectId} — get project detail
        projects.MapGet("/{projectId:regex(^[0-9]+$)}", async (
            string projectId,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Project(await service.GetProjectAsync(
                DatabaseIdParser.Required(projectId, nameof(projectId)),
                cancellationToken))));

        // PATCH /api/projects/{projectId} — update project
        projects.MapPatch("/{projectId:regex(^[0-9]+$)}", async (
            string projectId,
            UpdateProjectRequest request,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Project(await service.UpdateProjectAsync(
                DatabaseIdParser.Required(projectId, nameof(projectId)),
                request.ProjectName,
                request.Description,
                request.ProjectStatus,
                request.RowVersion,
                cancellationToken))));

        // DELETE /api/projects/{projectId} — archive project
        projects.MapDelete("/{projectId:regex(^[0-9]+$)}", async (
            string projectId,
            uint rowVersion,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Project(await service.ArchiveProjectAsync(
                DatabaseIdParser.Required(projectId, nameof(projectId)),
                rowVersion,
                cancellationToken))));

        // POST /api/projects/{projectId}/restore — restore archived project
        projects.MapPost("/{projectId:regex(^[0-9]+$)}/restore", async (
            string projectId,
            uint rowVersion,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Project(await service.RestoreProjectAsync(
                DatabaseIdParser.Required(projectId, nameof(projectId)),
                rowVersion,
                cancellationToken))));

        // Chapter endpoints under /api/projects/{projectId}
        projects.MapGet("/{projectId:regex(^[0-9]+$)}/chapters", async (
            string projectId,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListChaptersAsync(
                    DatabaseIdParser.Required(projectId, nameof(projectId)),
                    cancellationToken))
                .Select(PersistentApiMapper.Chapter)));

        projects.MapPost("/{projectId:regex(^[0-9]+$)}/chapters", async (
            string projectId,
            CreateChapterRequest request,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Created(
                $"/api/chapters",
                PersistentApiMapper.Chapter(await service.CreateChapterAsync(
                    DatabaseIdParser.Required(projectId, nameof(projectId)),
                    request.ChapterCode,
                    request.Title,
                    DatabaseIdParser.Optional(request.ParentId, nameof(request.ParentId)),
                    request.SortKey,
                    cancellationToken))));

        // Chapter sorting
        projects.MapPut("/{projectId:regex(^[0-9]+$)}/chapters/order", async (
            string projectId,
            List<ChapterOrderItem> items,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
        {
            var orderItems = items.Select(i => (
                ChapterId: DatabaseIdParser.Required(i.ChapterId, nameof(i.ChapterId)),
                ParentId: DatabaseIdParser.Optional(i.ParentId, nameof(i.ParentId)),
                SortKey: i.SortKey
            )).ToList().AsReadOnly();
            await service.ReorderChaptersAsync(
                DatabaseIdParser.Required(projectId, nameof(projectId)),
                orderItems,
                cancellationToken);
            return Results.Ok(new { reordered = orderItems.Count });
        });

        // Development data source initialization
        projects.MapPost("/{projectId:regex(^[0-9]+$)}/development-data-source/initialize",
            async (
                string projectId,
                DevDataSourceInitRequest request,
                IDevelopmentDataSourceInitializer initializer,
                CancellationToken cancellationToken) =>
            {
                DevelopmentDataSourceInitializationResult result =
                    await initializer.EnsureInitializedAsync(
                        DatabaseIdParser.Required(projectId, nameof(projectId)),
                        request.ForceRefresh,
                        cancellationToken);
                return Results.Ok(new
                {
                    projectId = result.ProjectId.ToString(),
                    dataSourceId = result.DataSourceId.ToString(),
                    snapshotId = result.SnapshotId.ToString(),
                    result.FieldCount,
                    result.Created,
                    result.Refreshed,
                });
            });
    }

    // Chapter management under /api/chapters
    private static void MapChapterEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder chapters = endpoints.MapGroup("/api/chapters")
            .WithTags("Chapters");

        chapters.MapGet("/{chapterId:regex(^[0-9]+$)}", async (
            string chapterId,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Chapter(await service.GetChapterAsync(
                DatabaseIdParser.Required(chapterId, nameof(chapterId)),
                cancellationToken))));

        chapters.MapPatch("/{chapterId:regex(^[0-9]+$)}", async (
            string chapterId,
            UpdateChapterRequest request,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Chapter(await service.UpdateChapterAsync(
                DatabaseIdParser.Required(chapterId, nameof(chapterId)),
                request.ChapterCode,
                request.Title,
                request.RowVersion,
                cancellationToken))));

        chapters.MapDelete("/{chapterId:regex(^[0-9]+$)}", async (
            string chapterId,
            uint rowVersion,
            ProjectChapterService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteChapterAsync(
                DatabaseIdParser.Required(chapterId, nameof(chapterId)),
                rowVersion,
                cancellationToken);
            return Results.Ok(new { deleted = true });
        });
    }

    private static void MapConnections(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder connections = endpoints.MapGroup("/api/data-connections")
            .WithTags("Data connections");
        connections.MapGet("/", async (
            string? projectId,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(
                    DatabaseIdParser.Optional(projectId, nameof(projectId)),
                    cancellationToken))
                .Select(PersistentApiMapper.Connection)));
        connections.MapPost("/", async (
            CreateDataConnectionRequest request,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Created(
                "/api/data-connections",
                PersistentApiMapper.Connection(await service.CreateAsync(
                    DatabaseIdParser.Optional(request.ProjectId, nameof(request.ProjectId)),
                    request.ConnectionName,
                    request.ConnectionType,
                    request.Config,
                    request.CredentialRef,
                    cancellationToken))));
        connections.MapPost("/{connectionId}/test", async (
            string connectionId,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.TestAsync(
                DatabaseIdParser.Required(connectionId, nameof(connectionId)),
                cancellationToken)));
        connections.MapGet("/{connectionId}/schemas", async (
            string connectionId,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListSchemasAsync(
                DatabaseIdParser.Required(connectionId, nameof(connectionId)),
                cancellationToken)));
        connections.MapGet("/{connectionId}/objects", async (
            string connectionId,
            string? schema,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListObjectsAsync(
                DatabaseIdParser.Required(connectionId, nameof(connectionId)),
                schema,
                cancellationToken)));
        connections.MapGet("/{connectionId}/columns", async (
            string connectionId,
            string schema,
            string objectName,
            DataConnectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListColumnsAsync(
                DatabaseIdParser.Required(connectionId, nameof(connectionId)),
                schema,
                objectName,
                cancellationToken)));
    }

    private static void MapSources(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder sources = endpoints.MapGroup("/api/data-sources")
            .WithTags("Data sources");
        sources.MapGet("/", async (
            string projectId,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(
                    DatabaseIdParser.Required(projectId, nameof(projectId)),
                    cancellationToken))
                .Select(PersistentApiMapper.DataSource)));
        sources.MapPost("/", async (
            CreateDataSourceRequest request,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Created(
                "/api/data-sources",
                PersistentApiMapper.DataSource(await service.CreateAsync(
                    DatabaseIdParser.Required(request.ProjectId, nameof(request.ProjectId)),
                    DatabaseIdParser.Required(request.ConnectionId, nameof(request.ConnectionId)),
                    request.SourceCode,
                    request.SourceName,
                    request.SourceType,
                    request.SchemaName,
                    request.ObjectType,
                    request.ObjectName,
                    cancellationToken))));
        sources.MapPost("/{dataSourceId}/refresh", async (
            string dataSourceId,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Snapshot(await service.RefreshAsync(
                DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)),
                cancellationToken))));
        sources.MapGet("/{dataSourceId}/snapshot", async (
            string dataSourceId,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(PersistentApiMapper.Snapshot(
                await service.GetLatestReadySnapshotAsync(
                    DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)),
                    cancellationToken))));
        sources.MapGet("/{dataSourceId}/fields", async (
            string dataSourceId,
            string? query,
            int limit,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListFieldsAsync(
                    DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)),
                    query,
                    limit <= 0 ? 200 : Math.Min(limit, 1000),
                    cancellationToken))
                .Select(PersistentApiMapper.Field)));
        sources.MapGet("/{dataSourceId}/schema", async (
            string dataSourceId,
            string? query,
            IContextualDataSchemaProvider provider,
            CancellationToken cancellationToken) =>
        {
            DataSchemaContext context = new(
                DatabaseIdParser.Required(dataSourceId, nameof(dataSourceId)));
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.Ok(await provider.GetSchemaAsync(context, cancellationToken));
            }

            return Results.Ok(await provider.SearchAsync(
                context,
                query,
                200,
                cancellationToken));
        });
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
