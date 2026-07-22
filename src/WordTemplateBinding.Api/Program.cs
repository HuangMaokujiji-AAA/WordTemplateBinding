using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using WordTemplateBinding.Api.Endpoints;
using WordTemplateBinding.Api.Middleware;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Infrastructure.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

TemplateProcessingOptions templateOptions =
    builder.Configuration.GetSection("TemplateProcessing").Get<TemplateProcessingOptions>()
    ?? new TemplateProcessingOptions();
Validator.ValidateObject(
    templateOptions,
    new ValidationContext(templateOptions),
    validateAllProperties: true);

builder.Services.Configure<FormOptions>(options =>
{
    long configuredFileLimit = templateOptions.MaxUploadSizeMb * 1024L * 1024L;
    options.MultipartBodyLengthLimit = configuredFileLimit + 1024L * 1024L;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            context.HttpContext.TraceIdentifier);
    };
});
builder.Services.AddWordTemplateBinding(templateOptions);

WebApplication app = builder.Build();
app.UseMiddleware<ApiExceptionHandler>();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapTemplateEndpoints();
app.MapBindingEndpoints();
app.MapDataSchemaEndpoints();
app.MapReportEndpoints();
app.MapChartAnalysisEndpoints();
app.Run();

/// <summary>
/// 为 WebApplicationFactory 集成测试公开顶级程序入口类型。
/// </summary>
public partial class Program
{
}
