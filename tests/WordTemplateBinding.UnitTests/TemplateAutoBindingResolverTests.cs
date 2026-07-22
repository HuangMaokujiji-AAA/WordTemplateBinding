using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;
using WordTemplateBinding.Infrastructure.DataSchema;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证复用模板文本占位符和图表 Manifest 的自动绑定恢复规则。
/// </summary>
public sealed class TemplateAutoBindingResolverTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();

    /// <summary>
    /// 验证标准和 text: 兼容语法均按完整路径精确恢复。
    /// </summary>
    /// <param name="placeholder">显式占位符。</param>
    [Theory]
    [InlineData("{{StudentStatistics.AverageScore}}")]
    [InlineData("{{text:StudentStatistics.AverageScore}}")]
    public async Task ResolveAsync_KnownTextPlaceholder_RestoresBinding(string placeholder)
    {
        (TemplateImportSummary summary, IReadOnlyList<TemplateBinding> bindings) =
            await ResolveTextAsync(placeholder);

        TemplateBinding binding = Assert.Single(bindings);
        Assert.Equal("StudentStatistics.AverageScore", binding.DataPath);
        Assert.Equal(DataValueType.Decimal, binding.DataType);
        Assert.Equal(BindingTargetKind.Text, binding.TargetKind);
        Assert.Equal(1, summary.TextBindingsRestored);
    }

    /// <summary>
    /// 验证普通显式文字或未知字段保持未绑定并列入未恢复路径。
    /// </summary>
    [Theory]
    [InlineData("{{年度报告}}", "年度报告")]
    [InlineData("{{OldModule.RemovedField}}", "OldModule.RemovedField")]
    [InlineData("{{studentStatistics.AverageScore}}", "studentStatistics.AverageScore")]
    public async Task ResolveAsync_UnknownOrWrongCase_RemainsUnbound(
        string placeholder,
        string expectedPath)
    {
        (TemplateImportSummary summary, IReadOnlyList<TemplateBinding> bindings) =
            await ResolveTextAsync(placeholder);

        Assert.Empty(bindings);
        Assert.Contains(expectedPath, summary.UnresolvedPlaceholders);
    }

    /// <summary>
    /// 验证同一个字段路径出现多次时按 Locator 分别恢复。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RepeatedPath_RestoresEveryLocator()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateMultipleParagraphDocument(
            "{{Report.Title}}",
            "{{Report.Title}}",
            "{{Report.Title}}");
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes);

        TemplateImportSummary summary = await resolver.ResolveAsync(template);
        IReadOnlyList<TemplateBinding> bindings = await store.GetByTemplateAsync(template.Id);

        Assert.Equal(3, summary.TextBindingsRestored);
        Assert.Equal(3, bindings.Count);
        Assert.Single(bindings.Select(binding => binding.DataPath).Distinct());
    }

    /// <summary>
    /// 验证集合字段不能自动绑定到文本占位符。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ArrayTextPlaceholder_ReturnsCompatibilityWarning()
    {
        (TemplateImportSummary summary, IReadOnlyList<TemplateBinding> bindings) =
            await ResolveTextAsync("{{ChartData.ScienceScores}}");

        Assert.Empty(bindings);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("不兼容", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证已有 Locator 绑定不会重复创建或覆盖。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ExistingBinding_DoesNotDuplicate()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("{{Report.Title}}");
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes);
        MockDataItem item = template.ScanResult.MockItems[0];
        DateTimeOffset createdAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(new TemplateBinding
        {
            TemplateId = template.Id,
            TargetKind = BindingTargetKind.Text,
            LocatorId = item.LocatorId,
            DataPath = "Report.Title",
            DataType = DataValueType.String,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });

        TemplateImportSummary summary = await resolver.ResolveAsync(template);
        TemplateBinding binding = Assert.Single(await store.GetByTemplateAsync(template.Id));

        Assert.Equal(0, summary.TextBindingsRestored);
        Assert.Equal(createdAt, binding.CreatedAt);
    }

    /// <summary>
    /// 验证图表清单按定位恢复 Array 字段绑定。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ChartManifest_RestoresArrayBinding()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = scan.Charts[0];
        TemplateScanResult manifestScan = scan with
        {
            BindingManifest = new ReusableTemplateManifest
            {
                ChartBindings = new[]
                {
                    CreateManifestBinding(chart, "ChartData.ScienceScores"),
                },
            },
        };
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes, manifestScan);

        TemplateImportSummary summary = await resolver.ResolveAsync(template);
        TemplateBinding binding = Assert.Single(await store.GetByTemplateAsync(template.Id));

        Assert.Equal(1, summary.ChartBindingsRestored);
        Assert.Equal(BindingTargetKind.Chart, binding.TargetKind);
        Assert.Equal(DataValueType.Array, binding.DataType);
    }

    /// <summary>
    /// 验证图表定位不存在或字段不再是 Array 时不错误绑定。
    /// </summary>
    [Theory]
    [InlineData(true, "ChartData.ScienceScores")]
    [InlineData(false, "StudentStatistics.AverageScore")]
    public async Task ResolveAsync_InvalidChartManifest_RemainsUnbound(
        bool useMissingLocator,
        string dataPath)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = scan.Charts[0];
        ReusableTemplateChartBinding manifest = CreateManifestBinding(chart, dataPath);
        if (useMissingLocator)
        {
            manifest = manifest with
            {
                PartKey = "/word/charts/missing.xml",
                RelationshipId = "missing",
                DocumentOrder = 99,
            };
        }

        TemplateScanResult manifestScan = scan with
        {
            BindingManifest = new ReusableTemplateManifest
            {
                ChartBindings = new[] { manifest },
            },
        };
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes, manifestScan);

        TemplateImportSummary summary = await resolver.ResolveAsync(template);

        Assert.Empty(await store.GetByTemplateAsync(template.Id));
        Assert.NotEmpty(summary.Warnings);
    }

    /// <summary>
    /// 验证 Manifest 不存在不会影响普通模板。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_NoManifest_LeavesChartUnboundWithoutWarning()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes);

        TemplateImportSummary summary = await resolver.ResolveAsync(template);

        Assert.Empty(await store.GetByTemplateAsync(template.Id));
        Assert.Empty(summary.Warnings);
    }

    /// <summary>
    /// 验证损坏 Manifest 只产生警告，文本占位符仍然恢复。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_DamagedManifest_TextRecoveryContinues()
    {
        byte[] source = OpenXmlTestDocumentFactory.CreateParagraphDocument("{{Report.Title}}");
        byte[] bytes = OpenXmlTestDocumentFactory.AddCustomXmlPart(
            source,
            "<wtb:bindings xmlns:wtb=\"urn:word-template-binding:bindings:v1\"><broken>");
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes);

        TemplateImportSummary summary = await resolver.ResolveAsync(template);

        Assert.Single(await store.GetByTemplateAsync(template.Id));
        Assert.Equal(1, summary.TextBindingsRestored);
        Assert.Contains(summary.Warnings, warning =>
            warning.Contains("损坏", StringComparison.Ordinal));
    }

    private async Task<(TemplateImportSummary Summary, IReadOnlyList<TemplateBinding> Bindings)>
        ResolveTextAsync(string placeholder)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(placeholder);
        (TemplateDocument template, IBindingStore store, ITemplateAutoBindingResolver resolver) =
            await CreateResolverAsync(bytes);
        TemplateImportSummary summary = await resolver.ResolveAsync(template);
        return (summary, await store.GetByTemplateAsync(template.Id));
    }

    private async Task<(TemplateDocument Template, IBindingStore Store, ITemplateAutoBindingResolver Resolver)>
        CreateResolverAsync(byte[] bytes, TemplateScanResult? suppliedScan = null)
    {
        TemplateScanResult scan = suppliedScan ?? await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        IBindingStore store = new InMemoryBindingStore();
        ITemplateAutoBindingResolver resolver = new TemplateAutoBindingResolver(
            store,
            new InMemoryDataSchemaProvider(),
            SystemClock.Instance);
        return (template, store, resolver);
    }

    private static ReusableTemplateChartBinding CreateManifestBinding(
        ChartTemplateItem chart,
        string dataPath) =>
        new()
        {
            DataPath = dataPath,
            PartKey = chart.Locator.PartKey,
            RelationshipId = chart.Locator.RelationshipId,
            DocumentOrder = chart.Locator.DocumentOrder,
        };
}
