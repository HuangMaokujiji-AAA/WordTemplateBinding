using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供模板绑定关系的创建、查询和删除端点。
/// </summary>
public static class BindingEndpoints
{
    /// <summary>
    /// 映射绑定管理端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>返回原端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapBindingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/bindings", UpsertAsync);
        endpoints.MapGet("/api/templates/{templateId:guid}/bindings", GetAsync);
        endpoints.MapDelete(
            "/api/templates/{templateId:guid}/bindings/{locatorId}",
            DeleteAsync);
        return endpoints;
    }

    /// <summary>
    /// 创建或覆盖指定模拟数据的绑定。
    /// </summary>
    /// <param name="request">绑定请求。</param>
    /// <param name="service">绑定业务服务。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回保存后的绑定。</returns>
    private static async Task<IResult> UpsertAsync(
        UpsertBindingRequest request,
        BindingWorkflowService service,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        if (request.TemplateId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.LocatorId) ||
            string.IsNullOrWhiteSpace(request.DataPath))
        {
            throw new BindingValidationException("TemplateId、LocatorId 和 DataPath 均不能为空。");
        }

        WordTemplateBinding.Core.Models.ChartBindingMapping? chartMapping = null;
        if (request.ChartMapping is not null)
        {
            chartMapping = new WordTemplateBinding.Core.Models.ChartBindingMapping
            {
                Mode = request.ChartMapping.Mode,
                CategoryField = request.ChartMapping.CategoryField,
                SeriesMappings = request.ChartMapping.SeriesMappings.Select(sm =>
                    new WordTemplateBinding.Core.Models.ChartSeriesFieldMapping
                    {
                        SeriesIndex = sm.SeriesIndex,
                        SeriesKey = sm.SeriesKey,
                        ValueField = sm.ValueField,
                        SeriesNameField = sm.SeriesNameField,
                    }).ToList().AsReadOnly(),
            };
        }

        TemplateBinding binding = await service.UpsertAsync(
            request.TemplateId,
            request.LocatorId.Trim(),
            request.DataPath.Trim(),
            cancellationToken,
            chartMapping);
        logger.CreateLogger("BindingUpsert").LogInformation(
            "模板绑定已保存，TemplateId={TemplateId}, LocatorId={LocatorId}, DataPath={DataPath}",
            binding.TemplateId,
            binding.LocatorId,
            binding.DataPath);
        return Results.Ok(new BindingOperationResponse(
            true,
            ApiContractMapper.ToResponse(binding)));
    }

    /// <summary>
    /// 获取指定模板的全部绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="service">绑定业务服务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回绑定响应列表。</returns>
    private static async Task<IResult> GetAsync(
        Guid templateId,
        BindingWorkflowService service,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TemplateBinding> bindings =
            await service.GetByTemplateAsync(templateId, cancellationToken);
        return Results.Ok(bindings.Select(ApiContractMapper.ToResponse).ToList().AsReadOnly());
    }

    /// <summary>
    /// 删除指定模拟数据的当前绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="locatorId">模拟数据定位标识。</param>
    /// <param name="service">绑定业务服务。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回是否删除成功。</returns>
    private static async Task<IResult> DeleteAsync(
        Guid templateId,
        string locatorId,
        BindingWorkflowService service,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        bool deleted = await service.DeleteAsync(templateId, locatorId, cancellationToken);
        logger.CreateLogger("BindingDelete").LogInformation(
            "模板绑定删除完成，TemplateId={TemplateId}, LocatorId={LocatorId}, Deleted={Deleted}",
            templateId,
            locatorId,
            deleted);
        return Results.Ok(new DeleteBindingResponse(true, deleted));
    }
}
