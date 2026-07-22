using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;
using WordTemplateBinding.Infrastructure.DataSchema;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证内存存储、字段树规模与绑定业务规则。
/// </summary>
public sealed class SchemaStoreAndBindingTests
{
    /// <summary>
    /// 验证字段路径唯一且叶子字段数量达到大数据量目标。
    /// </summary>
    [Fact]
    public async Task SchemaProvider_HasUniquePathsAndAboutThreeThousandLeaves()
    {
        InMemoryDataSchemaProvider provider = new();
        IReadOnlyList<DataFieldNode> roots = await provider.GetSchemaAsync();
        List<DataFieldNode> leaves = Flatten(roots).Where(node => node.IsLeaf).ToList();

        Assert.True(leaves.Count >= 3000);
        Assert.Equal(leaves.Count, leaves.Select(node => node.Path).Distinct().Count());
        Assert.NotNull(await provider.FindByPathAsync("StudentStatistics.AverageScore"));
    }

    /// <summary>
    /// 验证字段搜索忽略大小写并按数量限制截断。
    /// </summary>
    [Fact]
    public async Task SchemaProvider_Search_ReturnsMatchesAndTruncation()
    {
        InMemoryDataSchemaProvider provider = new();

        DataSchemaSearchResult result = await provider.SearchAsync("metric", 20);

        Assert.Equal(20, result.Nodes.Count);
        Assert.True(result.MatchCount >= 3000);
        Assert.True(result.IsTruncated);
    }

    /// <summary>
    /// 验证演示数据包含可用于报告生成的数值字段。
    /// </summary>
    [Fact]
    public async Task ValueProvider_ContainsNumericDemoValues()
    {
        InMemoryDataValueProvider provider = new();

        IReadOnlyDictionary<string, object?> values = await provider.GetValuesAsync();

        Assert.Equal(92.3m, values["StudentStatistics.AverageScore"]);
        Assert.True(values.Count >= 3000);
    }

    /// <summary>
    /// 验证模板存储不会暴露内部原始字节数组。
    /// </summary>
    [Fact]
    public async Task TemplateStore_ReturnsDefensiveByteCopies()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("成绩 88.5");
        TemplateScanResult scan = await TestServiceFactory.CreateScanner().ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        InMemoryTemplateStore store = new();
        await store.SaveAsync(template);

        TemplateDocument stored = Assert.IsType<TemplateDocument>(await store.GetAsync(template.Id));
        byte[] exposed = stored.GetOriginalBytesCopy();
        exposed[0] ^= 0xFF;
        TemplateDocument reread = Assert.IsType<TemplateDocument>(await store.GetAsync(template.Id));

        Assert.Equal(bytes, reread.GetOriginalBytesCopy());
    }

    /// <summary>
    /// 验证同一 Locator 的绑定可以新增、覆盖和删除。
    /// </summary>
    [Fact]
    public async Task BindingWorkflow_UpsertUpdateDelete_MaintainsSingleCurrentBinding()
    {
        (BindingWorkflowService service, TemplateDocument template, MockDataItem item) =
            await CreateBindingServiceAsync();

        TemplateBinding first = await service.UpsertAsync(
            template.Id,
            item.LocatorId,
            "StudentStatistics.AverageScore");
        TemplateBinding updated = await service.UpsertAsync(
            template.Id,
            item.LocatorId,
            "StudentStatistics.StudentCount");
        IReadOnlyList<TemplateBinding> bindings = await service.GetByTemplateAsync(template.Id);

        Assert.Single(bindings);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal("StudentStatistics.StudentCount", bindings[0].DataPath);
        Assert.True(await service.DeleteAsync(template.Id, item.LocatorId));
        Assert.Empty(await service.GetByTemplateAsync(template.Id));
    }

    /// <summary>
    /// 验证不存在的字段和不兼容字段不能保存绑定。
    /// </summary>
    [Fact]
    public async Task BindingWorkflow_InvalidFieldOrType_ThrowsValidationErrors()
    {
        (BindingWorkflowService service, TemplateDocument template, MockDataItem item) =
            await CreateBindingServiceAsync();

        await Assert.ThrowsAsync<DataFieldNotFoundException>(
            () => service.UpsertAsync(template.Id, item.LocatorId, "Missing.Path"));
        await Assert.ThrowsAsync<BindingValidationException>(
            () => service.UpsertAsync(template.Id, item.LocatorId, "Report.Title"));
    }

    /// <summary>
    /// 验证整数和显式文字模拟数据可以绑定兼容字段，并拒绝跨类别绑定。
    /// </summary>
    [Fact]
    public async Task BindingWorkflow_IntegerAndString_UseCompatibilityMatrix()
    {
        (BindingWorkflowService integerService, TemplateDocument integerTemplate, MockDataItem integerItem) =
            await CreateBindingServiceAsync("学生人数1200人");
        TemplateBinding integerBinding = await integerService.UpsertAsync(
            integerTemplate.Id,
            integerItem.LocatorId,
            "StudentStatistics.StudentCount");

        (BindingWorkflowService stringService, TemplateDocument stringTemplate, MockDataItem stringItem) =
            await CreateBindingServiceAsync("标题{{text:年度报告}}");
        TemplateBinding stringBinding = await stringService.UpsertAsync(
            stringTemplate.Id,
            stringItem.LocatorId,
            "Report.Title");

        Assert.Equal(DataValueType.Integer, integerBinding.DataType);
        Assert.Equal(DataValueType.String, stringBinding.DataType);
        await Assert.ThrowsAsync<BindingValidationException>(
            () => integerService.UpsertAsync(
                integerTemplate.Id,
                integerItem.LocatorId,
                "Report.Title"));
        await Assert.ThrowsAsync<BindingValidationException>(
            () => stringService.UpsertAsync(
                stringTemplate.Id,
                stringItem.LocatorId,
                "StudentStatistics.AverageScore"));
    }

    /// <summary>
    /// 验证重新上传的字段路径占位符可改绑到另一个兼容标量字段。
    /// </summary>
    [Fact]
    public async Task BindingWorkflow_ReusablePathPlaceholder_CanChangeScalarBinding()
    {
        (BindingWorkflowService service, TemplateDocument template, MockDataItem item) =
            await CreateBindingServiceAsync("{{StudentStatistics.AverageScore}}");

        TemplateBinding binding = await service.UpsertAsync(
            template.Id,
            item.LocatorId,
            "StudentStatistics.PassRate");

        Assert.Equal("StudentStatistics.PassRate", binding.DataPath);
        Assert.Equal(DataValueType.Decimal, binding.DataType);
    }

    /// <summary>
    /// 验证图表只能绑定可用的集合字段。
    /// </summary>
    [Fact]
    public async Task BindingWorkflow_Chart_RequiresArrayField()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult scan = await TestServiceFactory.CreateScanner().ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        ITemplateStore templateStore = new InMemoryTemplateStore();
        IBindingStore bindingStore = new InMemoryBindingStore();
        await templateStore.SaveAsync(template);
        BindingWorkflowService service = new(
            templateStore,
            bindingStore,
            new InMemoryDataSchemaProvider(),
            SystemClock.Instance);
        ChartTemplateItem chart = Assert.Single(scan.Charts);

        TemplateBinding binding = await service.UpsertAsync(
            template.Id,
            chart.LocatorId,
            "ChartData.ScienceScores");

        Assert.Equal(BindingTargetKind.Chart, binding.TargetKind);
        Assert.Equal(DataValueType.Array, binding.DataType);
        await Assert.ThrowsAsync<BindingValidationException>(() => service.UpsertAsync(
            template.Id,
            chart.LocatorId,
            "StudentStatistics.AverageScore"));
    }

    /// <summary>
    /// 创建包含已保存模板的绑定业务服务。
    /// </summary>
    /// <returns>返回业务服务、模板和模拟数据项。</returns>
    private static async Task<(BindingWorkflowService Service, TemplateDocument Template, MockDataItem Item)>
        CreateBindingServiceAsync(string paragraphText = "成绩 88.5")
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(paragraphText);
        TemplateScanResult scan = await TestServiceFactory.CreateScanner().ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        ITemplateStore templateStore = new InMemoryTemplateStore();
        IBindingStore bindingStore = new InMemoryBindingStore();
        await templateStore.SaveAsync(template);
        BindingWorkflowService service = new(
            templateStore,
            bindingStore,
            new InMemoryDataSchemaProvider(),
            SystemClock.Instance);
        return (service, template, Assert.Single(scan.MockItems));
    }

    /// <summary>
    /// 深度优先展开字段树。
    /// </summary>
    /// <param name="nodes">字段节点。</param>
    /// <returns>返回包含分支和叶子的扁平序列。</returns>
    private static IEnumerable<DataFieldNode> Flatten(IEnumerable<DataFieldNode> nodes)
    {
        foreach (DataFieldNode node in nodes)
        {
            yield return node;
            foreach (DataFieldNode child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
