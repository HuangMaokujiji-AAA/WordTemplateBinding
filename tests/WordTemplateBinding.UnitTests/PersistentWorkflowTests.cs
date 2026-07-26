using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Core.Services;
using WordTemplateBinding.Infrastructure.OpenXml;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证第二阶段领域服务在可重复的内存适配器上形成完整持久化闭环。
/// </summary>
public sealed class PersistentWorkflowTests
{
    /// <summary>
    /// 验证无标记 DOCX 仍作为带警告的有效模板版本保存。
    /// </summary>
    [Fact]
    public async Task CreateTemplate_WithoutMarkers_IsReadyWithWarning()
    {
        TestContext context = new();
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("普通正文");

        await using MemoryStream input = new(bytes, writable: false);
        TemplateVersionView view = await context.Templates.CreateAsync(
            new TemplateCreateRequest
            {
                TemplateCode = "PLAIN",
                TemplateName = "普通模板",
            },
            "plain.docx",
            bytes.Length,
            input,
            actorUserId: 1,
            CancellationToken.None);

        Assert.Equal("READY_WITH_WARNINGS", view.Version.VersionStatus);
        Assert.Empty(view.Elements);
        Assert.Contains(
            view.ParseResult.Warnings,
            warning => warning.Code == "NO_BINDABLE_ELEMENTS");
    }

    /// <summary>
    /// 验证模板元素、快照字段、绑定集、建议、预览和报告生成使用同一组持久化 ID。
    /// </summary>
    [Fact]
    public async Task BindingSetWorkflow_PersistsValidatesPreviewsAndRenders()
    {
        TestContext context = new();
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "报告名称：{{text:ReportTitle}}");
        await using MemoryStream input = new(bytes, writable: false);
        TemplateVersionView view = await context.Templates.CreateAsync(
            new TemplateCreateRequest
            {
                TemplateCode = "REPORT_TITLE",
                TemplateName = "报告标题模板",
            },
            "report.docx",
            bytes.Length,
            input,
            actorUserId: 1,
            CancellationToken.None);
        TemplateElementRecord element = Assert.Single(view.Elements);
        Assert.NotEqual(0UL, element.Id);

        ProjectRecord project = await context.Projects.CreateProjectAsync(
            "P001",
            "测试项目",
            null,
            CancellationToken.None);
        ChapterRecord chapter = await context.Projects.CreateChapterAsync(
            project.Id,
            "C001",
            "第一章",
            null,
            1,
            CancellationToken.None);
        DataSourceRecord source = await context.Sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = project.Id,
                ConnectionId = 1,
                SourceCode = "REPORT_ROWS",
                SourceName = "报告数据",
                SourceType = "DATABASE",
                SourceStatus = "ACTIVE",
                SchemaName = "reporting",
                ObjectType = "TABLE",
                ObjectName = "report_rows",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            actorUserId: 1,
            CancellationToken.None);
        DataSnapshotRecord snapshot = await context.Snapshots.StartAsync(
            source.Id,
            actorUserId: 1,
            CancellationToken.None);
        await context.Fields.ReplaceAsync(
            snapshot.Id,
            new[]
            {
                new DataFieldRecord
                {
                    Id = 0,
                    SnapshotId = snapshot.Id,
                    FieldPath = "rows.ReportTitle",
                    FieldName = "ReportTitle",
                    Comment = "ReportTitle",
                    DataType = DataValueType.String,
                    IsArray = false,
                    IsNullable = false,
                    IsBindable = true,
                    SampleValueJson = "\"年度质量报告\"",
                    DisplayOrder = 1,
                },
            },
            CancellationToken.None);
        await context.Snapshots.CompleteAsync(
            snapshot.Id,
            """{"captureMode":"SCHEMA_AND_SAMPLE","sampleRows":[{"ReportTitle":"年度质量报告"}]}""",
            "{}",
            new string('a', 64),
            1,
            CancellationToken.None);

        BindingSetRecord set = await context.Bindings.GetOrCreateDraftAsync(
            chapter.Id,
            view.Version.Id,
            CancellationToken.None);
        IReadOnlyList<BindingSuggestion> suggestions =
            await context.Bindings.SuggestAsync(
                element.Id,
                source.Id,
                CancellationToken.None);
        Assert.Equal("rows.ReportTitle", Assert.Single(suggestions).FieldPath);

        BindingItemRecord item = await context.Bindings.UpsertAsync(
            set.Id,
            element.Id,
            new BindingItemUpsert
            {
                DataSourceId = source.Id,
                SourcePath = "rows.ReportTitle",
            },
            CancellationToken.None);
        Assert.NotEqual(0UL, item.Id);

        BindingValidationResult validation = await context.Bindings.ValidateAsync(
            set.Id,
            CancellationToken.None);
        Assert.Equal("VALID", validation.Status);

        TemplateVersionView rescanned = await context.Templates.RescanAsync(
            view.Version.Id,
            CancellationToken.None);
        Assert.Equal(element.Id, Assert.Single(rescanned.Elements).Id);
        BindingValidationResult validationAfterRescan =
            await context.Bindings.ValidateAsync(set.Id, CancellationToken.None);
        Assert.Equal("VALID", validationAfterRescan.Status);

        BindingPreview preview = await context.Bindings.PreviewAsync(
            set.Id,
            element.Id,
            CancellationToken.None);
        Assert.Equal("年度质量报告", preview.FormattedValue);

        RenderedReport report = await context.Documents.GenerateReportAsync(
            set.Id,
            CancellationToken.None);
        Assert.Contains(
            "年度质量报告",
            OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()),
            StringComparison.Ordinal);
    }

    private sealed class TestContext
    {
        private readonly InMemoryPersistenceState _state = new();
        private readonly InMemoryTemplateVersionRepository _versions;
        private readonly InMemoryTemplateElementRepository _elements;
        private readonly InMemoryBindingSetRepository _sets;
        private readonly InMemoryBindingItemRepository _items;
        private readonly InMemoryChapterRepository _chapters;

        internal TestContext()
        {
            TemplateProcessingOptions templateOptions = new();
            InMemoryTemplateRepository templates = new(_state);
            _versions = new InMemoryTemplateVersionRepository(_state);
            _elements = new InMemoryTemplateElementRepository(_state);
            Files = new InMemoryFileStorageService(new DatabaseFileStorageOptions());
            Templates = new TemplateCatalogService(
                templates,
                _versions,
                _elements,
                Files,
                TestServiceFactory.CreateScanner(),
                new TemplateElementIdentityResolver(),
                templateOptions,
                new DatabaseFileStorageOptions());
            InMemoryProjectRepository projects = new(_state);
            _chapters = new InMemoryChapterRepository(_state);
            ApplicationIdentityOptions identity = new()
            {
                DefaultActorUserId = "1",
            };
            Projects = new ProjectChapterService(projects, _chapters, identity);
            Sources = new InMemoryDataSourceRepository(_state);
            Snapshots = new InMemoryDataSnapshotRepository(_state);
            Fields = new InMemoryDataFieldRepository(_state);
            _sets = new InMemoryBindingSetRepository(_state);
            _items = new InMemoryBindingItemRepository(_state);
            Bindings = new BindingWorkspaceService(
                _sets,
                _items,
                _versions,
                _elements,
                _chapters,
                Sources,
                Snapshots,
                Fields,
                new BindingSuggestionOptions(),
                identity);
            Documents = new BindingSetDocumentService(
                _sets,
                _items,
                _versions,
                _elements,
                Snapshots,
                Fields,
                Files,
                TestServiceFactory.CreateRenderer(),
                TestServiceFactory.CreateReusableTemplateRenderer(),
                Bindings);
        }

        internal IFileStorageService Files { get; }
        internal TemplateCatalogService Templates { get; }
        internal ProjectChapterService Projects { get; }
        internal InMemoryDataSourceRepository Sources { get; }
        internal InMemoryDataSnapshotRepository Snapshots { get; }
        internal InMemoryDataFieldRepository Fields { get; }
        internal BindingWorkspaceService Bindings { get; }
        internal BindingSetDocumentService Documents { get; }
    }
}
