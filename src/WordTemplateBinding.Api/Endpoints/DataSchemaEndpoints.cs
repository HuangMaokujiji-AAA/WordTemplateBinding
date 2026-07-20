using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供演示数据字段树和搜索端点。
/// </summary>
public static class DataSchemaEndpoints
{
    private const int SearchResultLimit = 200;

    /// <summary>
    /// 映射数据字段端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>返回原端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapDataSchemaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/data-schema", GetAsync);
        return endpoints;
    }

    /// <summary>
    /// 返回完整字段树或最多两百个扁平搜索结果。
    /// </summary>
    /// <param name="query">可选搜索文本。</param>
    /// <param name="provider">数据字段来源。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回字段树响应。</returns>
    private static async Task<IResult> GetAsync(
        string? query,
        IDataSchemaProvider provider,
        CancellationToken cancellationToken)
    {
        int leafCount = await provider.GetLeafCountAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            IReadOnlyList<DataFieldNode> nodes = await provider.GetSchemaAsync(cancellationToken);
            return Results.Ok(new DataSchemaResponse(
                null,
                leafCount,
                leafCount,
                false,
                ApiContractMapper.ToResponse(nodes)));
        }

        DataSchemaSearchResult searchResult = await provider.SearchAsync(
            query,
            SearchResultLimit,
            cancellationToken);
        return Results.Ok(new DataSchemaResponse(
            query.Trim(),
            leafCount,
            searchResult.MatchCount,
            searchResult.IsTruncated,
            ApiContractMapper.ToResponse(searchResult.Nodes)));
    }
}
