using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using WordTemplateBinding.Api.Endpoints;
using WordTemplateBinding.Api.Middleware;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Infrastructure.Database;
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
PersistenceOptions persistenceOptions =
    builder.Configuration.GetSection(PersistenceOptions.SectionName)
        .Get<PersistenceOptions>()
    ?? new PersistenceOptions();
Validator.ValidateObject(
    persistenceOptions,
    new ValidationContext(persistenceOptions),
    validateAllProperties: true);
if (string.Equals(
        persistenceOptions.Mode,
        "InMemory",
        StringComparison.OrdinalIgnoreCase) &&
    !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "InMemory 持久化仅允许自动化测试使用；实际运行请配置 Persistence:Mode=MySql。");
}

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
builder.Services.AddReportPlatformDatabase(builder.Configuration);
builder.Services.AddWordTemplateBinding(builder.Configuration, templateOptions);

WebApplication app = builder.Build();
if (string.Equals(
        persistenceOptions.Mode,
        "MySql",
        StringComparison.OrdinalIgnoreCase))
{
    IReportPlatformDatabaseConnectionFactory database =
        app.Services.GetRequiredService<IReportPlatformDatabaseConnectionFactory>();
    if (!database.IsConfigured)
    {
        throw new InvalidOperationException(
            $"MySQL 持久化缺少必要配置：{string.Join(", ", database.MissingSettings)}。");
    }
}

app.UseMiddleware<ApiExceptionHandler>();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPersistentTemplateEndpoints();
app.MapWorkspaceEndpoints();
if (string.Equals(
        persistenceOptions.Mode,
        "InMemory",
        StringComparison.OrdinalIgnoreCase))
{
    app.MapTemplateEndpoints();
    app.MapBindingEndpoints();
    app.MapDataSchemaEndpoints();
    app.MapReportEndpoints();
    app.MapChartAnalysisEndpoints();
}

app.MapDatabaseEndpoints();
app.Run();

/// <summary>
/// 为 WebApplicationFactory 集成测试公开顶级程序入口类型。
/// </summary>
public partial class Program
{
}
