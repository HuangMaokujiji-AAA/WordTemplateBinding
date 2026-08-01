#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class DataSourceEndpoints
{
    public static IEndpointRouteBuilder MapDataSourceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapSources(endpoints);
        MapHigherEducation(endpoints);
        return endpoints;
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
        sources.MapPost("/higher-education", async (
            CreateHigherEducationDataSourceRequest request,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
        {
            HigherEducationDataSourceResult result =
                await service.CreateHigherEducationAsync(
                    DatabaseIdParser.Required(request.ProjectId, nameof(request.ProjectId)),
                    request.CollectionYear,
                    request.SchoolCode,
                    request.SourceCode,
                    request.SourceName,
                    cancellationToken);
            return Results.Created(
                $"/api/data-sources/{result.Source.Id}",
                new
                {
                    source = PersistentApiMapper.DataSource(result.Source),
                    snapshot = PersistentApiMapper.Snapshot(result.Snapshot),
                });
        });
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

    private static void MapHigherEducation(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/higher-education")
            .WithTags("Higher education monitoring");
        group.MapGet("/years", async (
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListHigherEducationYearsAsync(cancellationToken)));
        group.MapGet("/schools", async (
            string collectionYear,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListHigherEducationSchoolsAsync(
                collectionYear,
                cancellationToken)));
        group.MapGet("/report", async (
            string collectionYear,
            string schoolCode,
            DataSourceWorkspaceService service,
            CancellationToken cancellationToken) =>
        {
            HigherEducationReportData report =
                await service.GetHigherEducationReportAsync(
                    collectionYear,
                    schoolCode,
                    cancellationToken);
            return Results.Ok(new
            {
                report.CollectionYear,
                report.SchoolCode,
                report.SchoolName,
                report.RowCount,
                data = report.Content,
            });
        });
    }
}

#pragma warning restore CS1591
