using System.ComponentModel.DataAnnotations;
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
        return services.AddWordTemplateBinding(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Mode"] = "InMemory",
                })
                .Build(),
            options);
    }

    /// <summary>
    /// 注册模板扫描、渲染、持久化仓储和两阶段业务服务。
    /// </summary>
    public static IServiceCollection AddWordTemplateBinding(
        this IServiceCollection services,
        IConfiguration configuration,
        TemplateProcessingOptions options)
    {
        PersistenceOptions persistence =
            configuration.GetSection(PersistenceOptions.SectionName)
                .Get<PersistenceOptions>()
            ?? new PersistenceOptions();
        DatabaseFileStorageOptions fileOptions =
            configuration.GetSection(DatabaseFileStorageOptions.SectionName)
                .Get<DatabaseFileStorageOptions>()
            ?? new DatabaseFileStorageOptions();
        DataSourceOptions dataSourceOptions =
            configuration.GetSection(DataSourceOptions.SectionName)
                .Get<DataSourceOptions>()
            ?? new DataSourceOptions();
        ApplicationIdentityOptions identityOptions =
            configuration.GetSection(ApplicationIdentityOptions.SectionName)
                .Get<ApplicationIdentityOptions>()
            ?? new ApplicationIdentityOptions();
        BindingSuggestionOptions suggestionOptions =
            configuration.GetSection(BindingSuggestionOptions.SectionName)
                .Get<BindingSuggestionOptions>()
            ?? new BindingSuggestionOptions();
        ValidateOptions(persistence);
        ValidateOptions(fileOptions);
        ValidateOptions(dataSourceOptions);

        services.AddSingleton(options);
        services.AddSingleton(persistence);
        services.AddSingleton(fileOptions);
        services.AddSingleton(dataSourceOptions);
        services.AddSingleton(identityOptions);
        services.AddSingleton(suggestionOptions);
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IDataSchemaProvider, InMemoryDataSchemaProvider>();
        services.AddSingleton<IDataValueProvider, InMemoryDataValueProvider>();
        services.AddSingleton<ICurrentUserContext, DevelopmentCurrentUserContext>();
        services.AddSingleton<IDevelopmentDataSourceInitializer, JsonDevelopmentDataSourceInitializer>();
        services.AddSingleton<ILocatorIdGenerator, LocatorIdGenerator>();
        services.AddSingleton<IDocumentPreviewBuilder, DocumentPreviewBuilder>();
        services.AddSingleton<IDataValueFormatter, DataValueFormatter>();
        if (options.EnableAutomaticDecimalRecognition)
        {
            services.AddSingleton<IMockDataRecognizer, DecimalNumberRecognizer>();
        }

        if (options.EnableAutomaticIntegerRecognition)
        {
            services.AddSingleton<IMockDataRecognizer, IntegerNumberRecognizer>();
        }

        if (options.EnableYellowHighlightRecognition ||
            options.EnableYellowShadingRecognition)
        {
            services.AddSingleton<IMockDataRecognizer, YellowHighlightRecognizer>();
        }

        if (options.EnableExplicitPlaceholderRecognition)
        {
            services.AddSingleton<IMockDataRecognizer, ExplicitTextRecognizer>();
        }

        services.AddSingleton<IWordTemplateScanner, WordTemplateScanner>();
        services.AddSingleton<ITemplateElementIdentityResolver, TemplateElementIdentityResolver>();
        services.AddSingleton<IWordReportRenderer, WordReportRenderer>();
        services.AddSingleton<IWordReusableTemplateRenderer, WordReusableTemplateRenderer>();
        services.AddSingleton<
            IDataConnectionCredentialResolver,
            ConfigurationDataConnectionCredentialResolver>();
        services.AddSingleton<IDataSourceConnectionFactory, MySqlDataSourceConnectionFactory>();
        services.AddSingleton<IDatabaseSchemaIntrospector, MySqlSchemaIntrospector>();

        if (string.Equals(persistence.Mode, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            AddInMemoryPersistence(services);
            services.AddSingleton<ITemplateStore, InMemoryTemplateStore>();
            services.AddSingleton<IBindingStore, InMemoryBindingStore>();
            services.AddSingleton<ITemplateAutoBindingResolver, TemplateAutoBindingResolver>();
            services.AddScoped<TemplateWorkflowService>();
            services.AddScoped<BindingWorkflowService>();
            services.AddScoped<ReportWorkflowService>();
            services.AddScoped<ReusableTemplateWorkflowService>();
        }
        else
        {
            AddMySqlPersistence(services);
        }

        services.AddSingleton<IContextualDataSchemaProvider, PersistentDataSchemaProvider>();
        services.AddScoped<TemplateCatalogService>();
        services.AddScoped<ProjectChapterService>();
        services.AddScoped<DataConnectionService>();
        services.AddScoped<DataSourceWorkspaceService>();
        services.AddScoped<BindingWorkspaceService>();
        services.AddScoped<IBindingCandidateResolver, BindingCandidateResolver>();
        services.AddScoped<BindingSetDocumentService>();
        return services;
    }

    private static void AddMySqlPersistence(IServiceCollection services)
    {
        services.AddSingleton<MySqlFileObjectRepository>();
        services.AddSingleton<MySqlFileChunkRepository>();
        services.AddSingleton<MySqlUploadSessionRepository>();
        services.AddSingleton<IFileStorageService, DatabaseFileStorageService>();
        services.AddSingleton<ITemplateRepository, MySqlTemplateRepository>();
        services.AddSingleton<ITemplateVersionRepository, MySqlTemplateVersionRepository>();
        services.AddSingleton<ITemplateElementRepository, MySqlTemplateElementRepository>();
        services.AddSingleton<IProjectRepository, MySqlProjectRepository>();
        services.AddSingleton<IChapterRepository, MySqlChapterRepository>();
        services.AddSingleton<IDataConnectionRepository, MySqlDataConnectionRepository>();
        services.AddSingleton<IDataSourceRepository, MySqlDataSourceRepository>();
        services.AddSingleton<IDataSnapshotRepository, MySqlDataSnapshotRepository>();
        services.AddSingleton<IDataFieldRepository, MySqlDataFieldRepository>();
        services.AddSingleton<IBindingSetRepository, MySqlBindingSetRepository>();
        services.AddSingleton<IBindingItemRepository, MySqlBindingItemRepository>();
        services.AddSingleton<IAuditLogWriter, MySqlAuditLogWriter>();
    }

    private static void AddInMemoryPersistence(IServiceCollection services)
    {
        services.AddSingleton<InMemoryPersistenceState>();
        services.AddSingleton<IFileStorageService, InMemoryFileStorageService>();
        services.AddSingleton<ITemplateRepository, InMemoryTemplateRepository>();
        services.AddSingleton<ITemplateVersionRepository, InMemoryTemplateVersionRepository>();
        services.AddSingleton<ITemplateElementRepository, InMemoryTemplateElementRepository>();
        services.AddSingleton<IProjectRepository, InMemoryProjectRepository>();
        services.AddSingleton<IChapterRepository, InMemoryChapterRepository>();
        services.AddSingleton<IDataConnectionRepository, InMemoryDataConnectionRepository>();
        services.AddSingleton<IDataSourceRepository, InMemoryDataSourceRepository>();
        services.AddSingleton<IDataSnapshotRepository, InMemoryDataSnapshotRepository>();
        services.AddSingleton<IDataFieldRepository, InMemoryDataFieldRepository>();
        services.AddSingleton<IBindingSetRepository, InMemoryBindingSetRepository>();
        services.AddSingleton<IBindingItemRepository, InMemoryBindingItemRepository>();
        services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();
    }

    private static void ValidateOptions(object options) =>
        Validator.ValidateObject(
            options,
            new ValidationContext(options),
            validateAllProperties: true);
}
