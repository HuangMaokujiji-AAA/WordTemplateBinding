using System.Diagnostics;
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供 DOCX 报告生成端点。
/// </summary>
public static class ReportEndpoints
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// 映射报告生成端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>返回原端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/reports/generate", GenerateAsync);
        return endpoints;
    }

    /// <summary>
    /// 合并演示数据和请求覆盖值并返回独立 DOCX 文件。
    /// </summary>
    /// <param name="request">报告生成请求。</param>
    /// <param name="service">报告业务服务。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回 DOCX 文件结果。</returns>
    private static async Task<IResult> GenerateAsync(
        GenerateReportRequest request,
        ReportWorkflowService service,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?>? values = request.Values?.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value.Clone(),
            StringComparer.Ordinal);
        Stopwatch stopwatch = Stopwatch.StartNew();
        RenderedReport report = await service.GenerateAsync(
            request.TemplateId,
            values,
            cancellationToken);
        stopwatch.Stop();

        logger.CreateLogger("ReportGeneration").LogInformation(
            "报告生成完成，TemplateId={TemplateId}, ElapsedMs={ElapsedMs}",
            request.TemplateId,
            stopwatch.ElapsedMilliseconds);
        return Results.File(
            report.GetBytesCopy(),
            DocxContentType,
            report.FileName);
    }
}
