using System.Diagnostics;
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供模板上传、获取和重新扫描端点。
/// </summary>
public static class TemplateEndpoints
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// 映射模板管理端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>返回原端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/templates");
        group.MapPost("/upload", UploadAsync);
        group.MapGet("/{templateId:guid}", GetAsync);
        group.MapPost("/{templateId:guid}/rescan", RescanAsync);
        group.MapPost("/{templateId:guid}/export-reusable", ExportReusableAsync);
        return endpoints;
    }

    /// <summary>
    /// 读取上传文件并交给模板业务服务保存。
    /// </summary>
    /// <param name="file">上传的 DOCX 文件。</param>
    /// <param name="service">模板业务服务。</param>
    /// <param name="bindingService">绑定业务服务。</param>
    /// <param name="options">模板处理配置。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板扫描结果。</returns>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        TemplateProcessingOptions options,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        if (file.Length > options.MaxUploadSizeMb * 1024L * 1024L)
        {
            throw new TemplateTooLargeException(options.MaxUploadSizeMb);
        }

        using MemoryStream stream = new(capacity: (int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(stream, cancellationToken);
        Stopwatch stopwatch = Stopwatch.StartNew();
        TemplateDocument template = await service.UploadAsync(
            file.FileName,
            stream.ToArray(),
            cancellationToken);
        IReadOnlyList<TemplateBinding> bindings =
            await bindingService.GetByTemplateAsync(template.Id, cancellationToken);
        stopwatch.Stop();

        logger.CreateLogger("TemplateUpload").LogInformation(
            "模板上传完成，TemplateId={TemplateId}, FileName={FileName}, MockItemCount={MockItemCount}, ElapsedMs={ElapsedMs}",
            template.Id,
            template.OriginalFileName,
            template.ScanResult.MockItems.Count,
            stopwatch.ElapsedMilliseconds);
        return Results.Ok(ApiContractMapper.ToResponse(template, bindings));
    }

    /// <summary>
    /// 获取模板、预览和当前绑定状态。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="service">模板业务服务。</param>
    /// <param name="bindingService">绑定业务服务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板响应。</returns>
    private static async Task<IResult> GetAsync(
        Guid templateId,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        CancellationToken cancellationToken)
    {
        TemplateDocument template = await service.GetAsync(templateId, cancellationToken);
        IReadOnlyList<TemplateBinding> bindings =
            await bindingService.GetByTemplateAsync(templateId, cancellationToken);
        return Results.Ok(ApiContractMapper.ToResponse(template, bindings));
    }

    /// <summary>
    /// 从不可变原始字节重新扫描模板并清理失效绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="service">模板业务服务。</param>
    /// <param name="bindingService">绑定业务服务。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回更新后的模板响应。</returns>
    private static async Task<IResult> RescanAsync(
        Guid templateId,
        TemplateWorkflowService service,
        BindingWorkflowService bindingService,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        TemplateDocument template = await service.RescanAsync(templateId, cancellationToken);
        IReadOnlyList<TemplateBinding> bindings =
            await bindingService.GetByTemplateAsync(templateId, cancellationToken);
        logger.CreateLogger("TemplateRescan").LogInformation(
            "模板重新扫描完成，TemplateId={TemplateId}, MockItemCount={MockItemCount}, BindingCount={BindingCount}",
            template.Id,
            template.ScanResult.MockItems.Count,
            bindings.Count);
        return Results.Ok(ApiContractMapper.ToResponse(template, bindings));
    }

    /// <summary>
    /// 将当前绑定写成字段路径占位符和图表清单并返回独立 DOCX。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="service">复用模板导出业务服务。</param>
    /// <param name="logger">结构化日志记录器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回可复用 DOCX 文件。</returns>
    private static async Task<IResult> ExportReusableAsync(
        Guid templateId,
        ReusableTemplateWorkflowService service,
        ILoggerFactory logger,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        RenderedTemplate renderedTemplate = await service.ExportAsync(
            templateId,
            cancellationToken);
        stopwatch.Stop();
        logger.CreateLogger("ReusableTemplateExport").LogInformation(
            "复用模板导出完成，TemplateId={TemplateId}, ElapsedMs={ElapsedMs}",
            templateId,
            stopwatch.ElapsedMilliseconds);
        return Results.File(
            renderedTemplate.GetBytesCopy(),
            DocxContentType,
            renderedTemplate.FileName);
    }
}
