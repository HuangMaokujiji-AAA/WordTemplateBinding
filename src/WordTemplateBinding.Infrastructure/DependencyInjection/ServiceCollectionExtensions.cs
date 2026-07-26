using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Core.Services;
using WordTemplateBinding.Infrastructure.Database;
using WordTemplateBinding.Infrastructure.DataSchema;
using WordTemplateBinding.Infrastructure.OpenXml;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.Infrastructure.DependencyInjection;

/// <summary>
/// 提供第一阶段基础设施和业务服务的依赖注入注册。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Report Platform MySQL 连接工厂。
    /// </summary>
    /// <param name="services">应用服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>返回原服务集合以支持链式调用。</returns>
    public static IServiceCollection AddReportPlatformDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ReportPlatformDatabaseOptions options =
            configuration.GetSection(ReportPlatformDatabaseOptions.SectionName)
                .Get<ReportPlatformDatabaseOptions>()
            ?? new ReportPlatformDatabaseOptions();

        services.AddSingleton(options);
        services.AddSingleton<
            IReportPlatformDatabaseConnectionFactory,
            ReportPlatformDatabaseConnectionFactory>();
        return services;
    }

    /// <summary>
    /// 注册模板扫描、渲染、内存存储、演示数据和业务编排服务。
    /// </summary>
    /// <param name="services">应用服务集合。</param>
    /// <param name="options">已经验证的模板处理配置。</param>
    /// <returns>返回原服务集合以支持链式调用。</returns>
    public static IServiceCollection AddWordTemplateBinding(
        this IServiceCollection services,
        TemplateProcessingOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ITemplateStore, InMemoryTemplateStore>();
        services.AddSingleton<IBindingStore, InMemoryBindingStore>();
        services.AddSingleton<IDataSchemaProvider, InMemoryDataSchemaProvider>();
        services.AddSingleton<IDataValueProvider, InMemoryDataValueProvider>();
        services.AddSingleton<ILocatorIdGenerator, LocatorIdGenerator>();
        services.AddSingleton<IDocumentPreviewBuilder, DocumentPreviewBuilder>();
        services.AddSingleton<IDataValueFormatter, DataValueFormatter>();
        services.AddSingleton<IMockDataRecognizer, DecimalNumberRecognizer>();
        services.AddSingleton<IMockDataRecognizer, IntegerNumberRecognizer>();
        services.AddSingleton<IMockDataRecognizer, YellowHighlightRecognizer>();
        services.AddSingleton<IMockDataRecognizer, ExplicitTextRecognizer>();
        services.AddSingleton<IWordTemplateScanner, WordTemplateScanner>();
        services.AddSingleton<ITemplateAutoBindingResolver, TemplateAutoBindingResolver>();
        services.AddSingleton<IWordReportRenderer, WordReportRenderer>();
        services.AddSingleton<IWordReusableTemplateRenderer, WordReusableTemplateRenderer>();
        services.AddScoped<TemplateWorkflowService>();
        services.AddScoped<BindingWorkflowService>();
        services.AddScoped<ReportWorkflowService>();
        services.AddScoped<ReusableTemplateWorkflowService>();
        return services;
    }
}
