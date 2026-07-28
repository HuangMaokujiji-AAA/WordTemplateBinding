#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class DataConnectionEndpoints
{
    public static IEndpointRouteBuilder MapDataConnectionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapConnections(endpoints);
        return endpoints;
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
}

#pragma warning restore CS1591

