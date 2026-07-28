#pragma warning disable CS1591

namespace WordTemplateBinding.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapProjectEndpoints();
        endpoints.MapDataConnectionEndpoints();
        endpoints.MapDataSourceEndpoints();
        endpoints.MapBindingSetEndpoints();
        return endpoints;
    }
}

#pragma warning restore CS1591
