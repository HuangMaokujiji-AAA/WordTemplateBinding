#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapProjects(endpoints);
        MapChapterEndpoints(endpoints);
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
}

#pragma warning restore CS1591

