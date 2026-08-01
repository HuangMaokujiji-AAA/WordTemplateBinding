using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WordTemplateBinding.Core.Exceptions;

namespace WordTemplateBinding.Api.Middleware;

/// <summary>
/// 将业务异常和未预期异常统一转换为 ProblemDetails 响应。
/// </summary>
public sealed class ApiExceptionHandler
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionHandler> _logger;

    /// <summary>
    /// 初始化 API 全局异常处理器。
    /// </summary>
    /// <param name="next">请求管道中的下一个中间件。</param>
    /// <param name="logger">结构化日志记录器。</param>
    public ApiExceptionHandler(
        RequestDelegate next,
        ILogger<ApiExceptionHandler> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// 执行请求，并将下游异常转换为 ProblemDetails 响应。
    /// </summary>
    /// <param name="httpContext">当前 HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception exception)
        {
            if (httpContext.Response.HasStarted)
            {
                throw;
            }

            await HandleAsync(httpContext, exception);
        }
    }

    private async Task HandleAsync(HttpContext httpContext, Exception exception)
    {
        (int statusCode, string title, string errorCode, string detail) = MapException(exception);
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "处理请求时发生未预期错误，TraceId={TraceId}", httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(
                "请求处理失败，ErrorCode={ErrorCode}, TraceId={TraceId}",
                errorCode,
                httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;
        ProblemDetails problemDetails = new()
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            cancellationToken: httpContext.RequestAborted);
    }

    /// <summary>
    /// 将异常映射为稳定状态码、标题、错误代码和安全详情。
    /// </summary>
    /// <param name="exception">当前异常。</param>
    /// <returns>返回 ProblemDetails 映射信息。</returns>
    private static (int StatusCode, string Title, string ErrorCode, string Detail) MapException(
        Exception exception)
    {
        return exception switch
        {
            TemplateTooLargeException business => (
                StatusCodes.Status413PayloadTooLarge,
                "模板文件过大",
                business.ErrorCode,
                business.Message),
            TemplateNotFoundException or LocatorNotFoundException or DataFieldNotFoundException
                when exception is WordTemplateBindingException business => (
                    StatusCodes.Status404NotFound,
                    "资源不存在",
                    business.ErrorCode,
                    business.Message),
            WorkspaceException business
                when business.ErrorCode.EndsWith(
                    "_not_found",
                    StringComparison.Ordinal) => (
                    StatusCodes.Status404NotFound,
                    "资源不存在",
                    business.ErrorCode,
                    business.Message),
            WorkspaceException business
                when business.ErrorCode.EndsWith(
                    "_conflict",
                    StringComparison.Ordinal) => (
                    StatusCodes.Status409Conflict,
                    "资源冲突",
                    business.ErrorCode,
                    business.Message),
            TemplatePersistenceException business
                when business.ErrorCode.EndsWith(
                    "_not_found",
                    StringComparison.Ordinal) => (
                    StatusCodes.Status404NotFound,
                    "资源不存在",
                    business.ErrorCode,
                    business.Message),
            DatabaseFileException business
                when business.ErrorCode.EndsWith(
                    "_not_found",
                    StringComparison.Ordinal) => (
                    StatusCodes.Status404NotFound,
                    "资源不存在",
                    business.ErrorCode,
                    business.Message),
            BindingValidationException or EmptyBindingsException or
                EmptyReusableTemplateBindingsException or ReusableTemplateRenderingException
                when exception is WordTemplateBindingException business => (
                    StatusCodes.Status409Conflict,
                    "当前状态不允许该操作",
                    business.ErrorCode,
                    business.Message),
            InvalidTemplateFileException or NoMockDataFoundException or
                DataValueConversionException or MissingDataValueException
                when exception is WordTemplateBindingException business => (
                    StatusCodes.Status400BadRequest,
                    "请求无法处理",
                    business.ErrorCode,
                    business.Message),
            ReportRenderingException business => (
                StatusCodes.Status500InternalServerError,
                "报告生成失败",
                business.ErrorCode,
                business.Message),
            BadHttpRequestException badRequest => (
                StatusCodes.Status400BadRequest,
                "请求无法处理",
                "bad_request",
                badRequest.Message),
            WordTemplateBindingException business => (
                StatusCodes.Status400BadRequest,
                "请求无法处理",
                business.ErrorCode,
                business.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                "服务器内部错误",
                "unexpected_error",
                "服务器处理请求时发生未预期错误。"),
        };
    }
}
