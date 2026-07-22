using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供图表分析 JSON 查看和下载端点，主要用于开发调试和假接口数据制作。
/// </summary>
public static class ChartAnalysisEndpoints
{
    /// <summary>
    /// 映射图表分析端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>返回原端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapChartAnalysisEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/templates/{templateId:guid}/charts/{locatorId}/analysis",
            GetChartAnalysisAsync);
        endpoints.MapGet(
            "/api/templates/{templateId:guid}/charts/{locatorId}/analysis/download",
            DownloadChartAnalysisAsync);
        endpoints.MapGet(
            "/api/templates/{templateId:guid}/charts/analysis",
            GetAllChartsAnalysisAsync);
        return endpoints;
    }

    /// <summary>
    /// 返回单张图表的完整分析 JSON。
    /// </summary>
    private static async Task<IResult> GetChartAnalysisAsync(
        Guid templateId,
        string locatorId,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        CancellationToken cancellationToken)
    {
        TemplateDocument template = await service.GetAsync(templateId, cancellationToken);
        IReadOnlyList<TemplateBinding> bindings =
            await bindingService.GetByTemplateAsync(templateId, cancellationToken);

        ChartTemplateItem? chartItem = template.ScanResult.Charts
            .FirstOrDefault(c => string.Equals(c.LocatorId, locatorId, StringComparison.Ordinal));
        if (chartItem is null)
        {
            throw new TemplateNotFoundException(templateId);
        }

        if (chartItem.Analysis is null)
        {
            return Results.Problem(
                detail: $"图表 {locatorId} 的分析结果不可用（ChartPart 可能已损坏）。",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(chartItem.Analysis);
    }

    /// <summary>
    /// 下载单张图表的分析 JSON 文件。
    /// </summary>
    private static async Task<IResult> DownloadChartAnalysisAsync(
        Guid templateId,
        string locatorId,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        CancellationToken cancellationToken)
    {
        TemplateDocument template = await service.GetAsync(templateId, cancellationToken);

        ChartTemplateItem? chartItem = template.ScanResult.Charts
            .FirstOrDefault(c => string.Equals(c.LocatorId, locatorId, StringComparison.Ordinal));
        if (chartItem?.Analysis is null)
        {
            return Results.Problem(
                detail: $"图表 {locatorId} 的分析结果不可用。",
                statusCode: StatusCodes.Status404NotFound);
        }

        string safeFileName = SanitizeFileName(chartItem.Title);
        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(
                    chartItem.Analysis,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    })),
            "application/json",
            $"{safeFileName}-analysis.json");
    }

    /// <summary>
    /// 返回当前模板全部图表的缩略分析信息。
    /// </summary>
    private static async Task<IResult> GetAllChartsAnalysisAsync(
        Guid templateId,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        CancellationToken cancellationToken)
    {
        TemplateDocument template = await service.GetAsync(templateId, cancellationToken);
        IReadOnlyList<TemplateBinding> bindings =
            await bindingService.GetByTemplateAsync(templateId, cancellationToken);
        TemplateResponse templateResponse = ApiContractMapper.ToResponse(template, bindings);

        return Results.Ok(new ChartsAnalysisResponse(
            template.Id,
            template.OriginalFileName,
            templateResponse.Charts.Count,
            templateResponse.Charts));
    }

    private static string SanitizeFileName(string title)
    {
        HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
        char[] safe = title
            .Where(c => !invalidChars.Contains(c) && !char.IsControl(c))
            .ToArray();
        string sanitized = new string(safe).Trim();
        if (sanitized.Length > 80)
        {
            sanitized = sanitized[..80];
        }
        return string.IsNullOrWhiteSpace(sanitized) ? "chart" : sanitized;
    }
}
