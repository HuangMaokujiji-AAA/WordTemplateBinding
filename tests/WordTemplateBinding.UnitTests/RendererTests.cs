using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证 Word 局部替换、格式保留和原模板不可变约束。
/// </summary>
public sealed class RendererTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();
    private readonly IWordReportRenderer _renderer = TestServiceFactory.CreateRenderer();

    /// <summary>
    /// 验证单个 Text 节点中的模拟值可以被局部替换。
    /// </summary>
    [Fact]
    public async Task RenderAsync_SingleText_ReplacesValue()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5 分");
        (TemplateDocument template, TemplateBinding binding) =
            await CreateTemplateAndBindingAsync(bytes);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 92.3m });

        string text = OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy());
        Assert.Contains("92.3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("88.5", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证跨三个 Run 的数字替换后文本正确且文档可重新打开。
    /// </summary>
    [Fact]
    public async Task RenderAsync_AcrossThreeRuns_ReplacesValue()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("88", ".", "5");
        (TemplateDocument template, TemplateBinding binding) =
            await CreateTemplateAndBindingAsync(bytes);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 91.25m });

        Assert.Equal("91.25", OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()));
    }

    /// <summary>
    /// 验证同一段落多个替换按逆序执行后位置仍然正确。
    /// </summary>
    [Fact]
    public async Task RenderAsync_MultipleValuesInParagraph_ReplacesAll()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "一班 88.5，二班 92.30。");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding first = CreateBinding(template.Id, scan.MockItems[0], "First", DataValueType.Decimal);
        TemplateBinding second = CreateBinding(template.Id, scan.MockItems[1], "Second", DataValueType.Decimal);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { first, second },
            new Dictionary<string, object?>
            {
                ["First"] = 7.25m,
                ["Second"] = 100.125m,
            });

        Assert.Equal(
            "一班 7.25，二班 100.125。",
            OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()));
    }

    /// <summary>
    /// 验证三个相同值可以分别替换为不同数据。
    /// </summary>
    [Fact]
    public async Task RenderAsync_ThreeRepeatedValues_ReplacesIndependently()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateMultipleParagraphDocument(
            "一班 88.5",
            "二班 88.5",
            "三班 88.5");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        List<TemplateBinding> bindings = scan.MockItems.Select(
            (item, index) => CreateBinding(
                template.Id,
                item,
                $"Value{index}",
                DataValueType.Decimal)).ToList();
        Dictionary<string, object?> values = new()
        {
            ["Value0"] = 90.1m,
            ["Value1"] = 91.2m,
            ["Value2"] = 92.3m,
        };

        RenderedReport report = await _renderer.RenderAsync(template, bindings, values);
        string text = OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy());

        Assert.Contains("一班 90.1", text, StringComparison.Ordinal);
        Assert.Contains("二班 91.2", text, StringComparison.Ordinal);
        Assert.Contains("三班 92.3", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证报告生成不会修改原始模板字节，且连续生成结果相互独立。
    /// </summary>
    [Fact]
    public async Task RenderAsync_Twice_DoesNotMutateOriginalTemplate()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        byte[] originalSnapshot = bytes.ToArray();
        (TemplateDocument template, TemplateBinding binding) =
            await CreateTemplateAndBindingAsync(bytes);

        RenderedReport first = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 90.1m });
        RenderedReport second = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 99.9m });

        Assert.Equal(originalSnapshot, template.GetOriginalBytesCopy());
        Assert.Contains("90.1", OpenXmlTestDocumentFactory.ReadBodyText(first.GetBytesCopy()));
        Assert.Contains("99.9", OpenXmlTestDocumentFactory.ReadBodyText(second.GetBytesCopy()));
        Assert.DoesNotContain("90.1", OpenXmlTestDocumentFactory.ReadBodyText(second.GetBytesCopy()));
    }

    /// <summary>
    /// 验证跨 Run 替换保留首个 Run 的字体、字号和强调属性。
    /// </summary>
    [Fact]
    public async Task RenderAsync_FormattedSplitValue_PreservesFirstRunProperties()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateFormattedSplitDocument();
        (TemplateDocument template, TemplateBinding binding) =
            await CreateTemplateAndBindingAsync(bytes);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 95.5m });
        Run firstRun = OpenXmlTestDocumentFactory.ReadFirstRun(report.GetBytesCopy());

        Assert.Equal("95.5", firstRun.InnerText);
        Assert.NotNull(firstRun.RunProperties?.Bold);
        Assert.NotNull(firstRun.RunProperties?.Italic);
        Assert.Equal("28", firstRun.RunProperties?.FontSize?.Val?.Value);
        Assert.Equal("C00000", firstRun.RunProperties?.Color?.Val?.Value);
        Assert.Equal("Arial", firstRun.RunProperties?.RunFonts?.Ascii?.Value);
    }

    /// <summary>
    /// 验证表格单元格内的模拟值可以正常替换。
    /// </summary>
    [Fact]
    public async Task RenderAsync_ValueInsideTable_ReplacesValue()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateTableDocument("成绩 76.5");
        (TemplateDocument template, TemplateBinding binding) =
            await CreateTemplateAndBindingAsync(bytes);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 88.8m });

        Assert.Contains(
            "成绩 88.8",
            OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证无空格整数模拟值可以替换为整数字段值。
    /// </summary>
    [Fact]
    public async Task RenderAsync_IntegerWithoutSpaces_ReplacesValue()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("学生人数1200人");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            Assert.Single(scan.MockItems),
            "StudentStatistics.StudentCount",
            DataValueType.Integer);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = 1350 });

        Assert.Equal(
            "学生人数1350人",
            OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()));
    }

    /// <summary>
    /// 验证字符串字段值会替换整个显式文字标记而不保留标记语法。
    /// </summary>
    [Fact]
    public async Task RenderAsync_ExplicitTextMarker_ReplacesWholeMarker()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "标题{{text:年度报告}}结束");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            Assert.Single(scan.MockItems),
            "Report.Title",
            DataValueType.String);

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = "半年度报告" });

        Assert.Equal(
            "标题半年度报告结束",
            OpenXmlTestDocumentFactory.ReadBodyText(report.GetBytesCopy()));
    }

    /// <summary>
    /// 验证不存在的 LocatorId 会产生明确异常。
    /// </summary>
    [Fact]
    public async Task RenderAsync_MissingLocator_ThrowsLocatorNotFound()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("成绩 88.5");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = new()
        {
            TemplateId = template.Id,
            LocatorId = "missing",
            DataPath = "Value",
            DataType = DataValueType.Decimal,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await Assert.ThrowsAsync<LocatorNotFoundException>(
            () => _renderer.RenderAsync(
                template,
                new[] { binding },
                new Dictionary<string, object?> { ["Value"] = 1.2m }));
    }

    /// <summary>
    /// 创建包含一个小数模拟数据的模板和绑定。
    /// </summary>
    /// <param name="bytes">原始 DOCX 字节。</param>
    /// <returns>返回模板和绑定。</returns>
    private async Task<(TemplateDocument Template, TemplateBinding Binding)>
        CreateTemplateAndBindingAsync(byte[] bytes)
    {
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            Assert.Single(scan.MockItems),
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);
        return (template, binding);
    }

    /// <summary>
    /// 创建指向指定模拟数据的测试绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="item">模拟数据项。</param>
    /// <param name="path">字段路径。</param>
    /// <param name="type">字段类型。</param>
    /// <returns>返回测试绑定。</returns>
    private static TemplateBinding CreateBinding(
        Guid templateId,
        MockDataItem item,
        string path,
        DataValueType type)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TemplateBinding
        {
            TemplateId = templateId,
            LocatorId = item.LocatorId,
            DataPath = path,
            DataType = type,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
