using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证可复用模板的占位符、Manifest、格式、原子校验和文件命名。
/// </summary>
public sealed class ReusableTemplateRendererTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();
    private readonly IWordReusableTemplateRenderer _renderer =
        TestServiceFactory.CreateReusableTemplateRenderer();

    /// <summary>
    /// 验证小数、整数和显式文字均写为完整字段路径占位符。
    /// </summary>
    /// <param name="source">原模板文字。</param>
    /// <param name="path">绑定路径。</param>
    /// <param name="type">字段类型。</param>
    [Theory]
    [InlineData("平均成绩 88.5", "StudentStatistics.AverageScore", DataValueType.Decimal)]
    [InlineData("学生人数 1200", "StudentStatistics.StudentCount", DataValueType.Integer)]
    [InlineData("标题 {{年度报告}}", "Report.Title", DataValueType.String)]
    public async Task RenderAsync_SupportedTextKinds_WriteFullPathPlaceholder(
        string source,
        string path,
        DataValueType type)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(source);
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(template.Id, scan.MockItems[0], path, type);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });

        Assert.Contains(
            "{{" + path + "}}",
            OpenXmlTestDocumentFactory.ReadBodyText(result.GetBytesCopy()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证黄色高亮候选绑定后仍使用标准双花括号字段路径导出。
    /// </summary>
    [Fact]
    public async Task RenderAsync_YellowHighlight_WritesFullPathPlaceholder()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateHighlightedParagraphDocument(
            ("监测年级为", false),
            ("四", true),
            ("年级", true),
            ("。", false));
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            Assert.Single(scan.MockItems),
            "Report.Grade",
            DataValueType.String);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });

        Assert.Equal(
            "监测年级为{{Report.Grade}}。",
            OpenXmlTestDocumentFactory.ReadBodyText(result.GetBytesCopy()));
        Assert.DoesNotContain(
            OpenXmlTestDocumentFactory.ReadBodyRuns(result.GetBytesCopy()),
            run => run.RunProperties?.GetFirstChild<Highlight>()?.Val?.Value ==
                HighlightColorValues.Yellow);
    }

    /// <summary>
    /// 验证由黄色高亮导出的占位符重新上传时按显式标记恢复候选字段路径。
    /// </summary>
    [Fact]
    public async Task RenderAsync_YellowHighlight_ReuploadPrefersExplicitPlaceholder()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateHighlightedParagraphDocument(
            ("监测年级为", false),
            ("四年级", true),
            ("。", false));
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            Assert.Single(scan.MockItems),
            "Report.Grade",
            DataValueType.String);

        RenderedTemplate rendered = await _renderer.RenderAsync(template, new[] { binding });
        TemplateScanResult rescanned = await _scanner.ScanAsync(rendered.GetBytesCopy());

        MockDataItem item = Assert.Single(rescanned.MockItems);
        Assert.Equal("Report.Grade", item.MockValue);
        Assert.Equal("Report.Grade", item.PlaceholderCandidatePath);
        Assert.Equal("{{Report.Grade}}", item.Locator.OriginalValue);
    }

    /// <summary>
    /// 验证同段多项从后向前替换且未绑定模拟值保持不变。
    /// </summary>
    [Fact]
    public async Task RenderAsync_MultipleAndUnboundValues_ReplacesOnlyBindings()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "2025 年共计 1200 名学生，平均成绩 88.5 分，及格率 96.8。");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding year = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "Report.Year",
            DataValueType.Integer);
        TemplateBinding count = CreateBinding(
            template.Id,
            scan.MockItems[1],
            "StudentStatistics.StudentCount",
            DataValueType.Integer);
        TemplateBinding average = CreateBinding(
            template.Id,
            scan.MockItems[2],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate result = await _renderer.RenderAsync(
            template,
            new[] { year, count, average });

        Assert.Equal(
            "{{Report.Year}} 年共计 {{StudentStatistics.StudentCount}} 名学生，平均成绩 {{StudentStatistics.AverageScore}} 分，及格率 96.8。",
            OpenXmlTestDocumentFactory.ReadBodyText(result.GetBytesCopy()));
    }

    /// <summary>
    /// 验证跨 Run 替换继承首个 Run 的格式。
    /// </summary>
    [Fact]
    public async Task RenderAsync_AcrossRuns_PreservesFirstRunProperties()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateFormattedSplitDocument();
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });
        Run firstRun = OpenXmlTestDocumentFactory.ReadFirstRun(result.GetBytesCopy());

        Assert.Equal("{{StudentStatistics.AverageScore}}", firstRun.InnerText);
        Assert.NotNull(firstRun.RunProperties?.Bold);
        Assert.NotNull(firstRun.RunProperties?.Italic);
        Assert.Equal("28", firstRun.RunProperties?.FontSize?.Val?.Value);
        Assert.Equal("C00000", firstRun.RunProperties?.Color?.Val?.Value);
    }

    /// <summary>
    /// 验证表格和页脚定位均复用同一替换策略。
    /// </summary>
    [Fact]
    public async Task RenderAsync_TableAndFooter_ReplacesSupportedParts()
    {
        byte[] tableBytes = OpenXmlTestDocumentFactory.CreateTableDocument("人数 1200");
        TemplateScanResult tableScan = await _scanner.ScanAsync(tableBytes);
        TemplateDocument tableTemplate = TestServiceFactory.CreateTemplate(tableBytes, tableScan);
        TemplateBinding tableBinding = CreateBinding(
            tableTemplate.Id,
            tableScan.MockItems[0],
            "StudentStatistics.StudentCount",
            DataValueType.Integer);

        byte[] footerBytes = OpenXmlTestDocumentFactory.CreateBodyAndFooterDocument(
            "正文保持",
            "平均成绩 88.5");
        TemplateScanResult footerScan = await _scanner.ScanAsync(footerBytes);
        TemplateDocument footerTemplate = TestServiceFactory.CreateTemplate(footerBytes, footerScan);
        TemplateBinding footerBinding = CreateBinding(
            footerTemplate.Id,
            footerScan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate table = await _renderer.RenderAsync(tableTemplate, new[] { tableBinding });
        RenderedTemplate footer = await _renderer.RenderAsync(footerTemplate, new[] { footerBinding });

        Assert.Contains(
            "{{StudentStatistics.StudentCount}}",
            OpenXmlTestDocumentFactory.ReadBodyText(table.GetBytesCopy()),
            StringComparison.Ordinal);
        Assert.Contains(
            "{{StudentStatistics.AverageScore}}",
            OpenXmlTestDocumentFactory.ReadFooterText(footer.GetBytesCopy()),
            StringComparison.Ordinal);
        Assert.Equal("正文保持", OpenXmlTestDocumentFactory.ReadBodyText(footer.GetBytesCopy()));
    }

    /// <summary>
    /// 验证每次导出从不可变原始字节开始且结果相互独立。
    /// </summary>
    [Fact]
    public async Task RenderAsync_Twice_DoesNotMutateOriginalBytes()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        byte[] original = bytes.ToArray();
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding firstBinding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);
        TemplateBinding secondBinding = firstBinding with { DataPath = "StudentStatistics.PassRate" };

        RenderedTemplate first = await _renderer.RenderAsync(template, new[] { firstBinding });
        RenderedTemplate second = await _renderer.RenderAsync(template, new[] { secondBinding });

        Assert.Equal(original, template.GetOriginalBytesCopy());
        Assert.Contains("AverageScore", OpenXmlTestDocumentFactory.ReadBodyText(first.GetBytesCopy()));
        Assert.Contains("PassRate", OpenXmlTestDocumentFactory.ReadBodyText(second.GetBytesCopy()));
        Assert.DoesNotContain("AverageScore", OpenXmlTestDocumentFactory.ReadBodyText(second.GetBytesCopy()));
    }

    /// <summary>
    /// 验证已经是相同占位符时不会嵌套花括号。
    /// </summary>
    [Fact]
    public async Task RenderAsync_ExistingPlaceholder_IsIdempotent()
    {
        const string placeholder = "{{StudentStatistics.AverageScore}}";
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(placeholder);
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });

        Assert.Equal(placeholder, OpenXmlTestDocumentFactory.ReadBodyText(result.GetBytesCopy()));
    }

    /// <summary>
    /// 验证无效 Locator 会使整个导出失败。
    /// </summary>
    [Fact]
    public async Task RenderAsync_InvalidLocator_FailsExport()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal) with { LocatorId = "missing" };

        await Assert.ThrowsAsync<ReusableTemplateRenderingException>(
            () => _renderer.RenderAsync(template, new[] { binding }));
    }

    /// <summary>
    /// 验证同段落重叠范围在修改前被拒绝。
    /// </summary>
    [Fact]
    public async Task RenderAsync_OverlappingRanges_FailsExport()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("88.5 和 92.3");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        MockDataItem first = scan.MockItems[0];
        MockDataItem overlapping = scan.MockItems[1] with
        {
            LocatorId = "overlap",
            Locator = first.Locator with { OccurrenceIndex = 1 },
        };
        TemplateScanResult overlappingScan = scan with
        {
            MockItems = new[] { first, overlapping },
        };
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, overlappingScan);

        await Assert.ThrowsAsync<ReusableTemplateRenderingException>(() => _renderer.RenderAsync(
            template,
            new[]
            {
                CreateBinding(template.Id, first, "First", DataValueType.Decimal),
                CreateBinding(template.Id, overlapping, "Second", DataValueType.Decimal),
            }));
    }

    /// <summary>
    /// 验证危险占位符字符在打开并修改文档前被拒绝。
    /// </summary>
    [Theory]
    [InlineData("Bad{{Path")]
    [InlineData("Bad}}Path")]
    [InlineData("Bad\nPath")]
    public async Task RenderAsync_InvalidDataPath_FailsExport(string path)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            path,
            DataValueType.Decimal);

        await Assert.ThrowsAsync<ReusableTemplateRenderingException>(
            () => _renderer.RenderAsync(template, new[] { binding }));
    }

    /// <summary>
    /// 验证文件名使用 template 正确拼写且不会重复追加后缀。
    /// </summary>
    [Theory]
    [InlineData("原文件.docx", "原文件-template.docx")]
    [InlineData("report-template.docx", "report-template.docx")]
    [InlineData("REPORT-TEMPLATE.DOCX", "REPORT-TEMPLATE.docx")]
    public async Task RenderAsync_FileName_UsesSingleTemplateSuffix(
        string originalName,
        string expected)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan, originalName);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });

        Assert.Equal(expected, result.FileName);
    }

    /// <summary>
    /// 验证图表本体不变且绑定通过固定命名空间 Manifest 保存。
    /// </summary>
    [Fact]
    public async Task RenderAsync_ChartBinding_PreservesChartAndWritesManifest()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        string originalChartXml = OpenXmlTestDocumentFactory.ReadFirstChartXml(bytes);
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        ChartTemplateItem chart = scan.Charts[0];
        TemplateBinding binding = CreateChartBinding(template.Id, chart);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });
        byte[] renderedBytes = result.GetBytesCopy();
        string manifest = OpenXmlTestDocumentFactory.ReadBindingManifest(renderedBytes);

        Assert.Equal(originalChartXml, OpenXmlTestDocumentFactory.ReadFirstChartXml(renderedBytes));
        Assert.Contains("urn:word-template-binding:bindings:v1", manifest, StringComparison.Ordinal);
        Assert.Contains("ChartData.ScienceScores", manifest, StringComparison.Ordinal);
        Assert.Contains(chart.Locator.PartKey, manifest, StringComparison.Ordinal);
        Assert.Contains(chart.Locator.RelationshipId, manifest, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证导出只更新本系统 Manifest，不覆盖其他软件的 CustomXmlPart。
    /// </summary>
    [Fact]
    public async Task RenderAsync_UnrelatedCustomXmlPart_IsPreserved()
    {
        byte[] source = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");
        byte[] bytes = OpenXmlTestDocumentFactory.AddCustomXmlPart(
            source,
            "<other xmlns=\"urn:other-software\">keep-me</other>");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateBinding(
            template.Id,
            scan.MockItems[0],
            "StudentStatistics.AverageScore",
            DataValueType.Decimal);

        RenderedTemplate result = await _renderer.RenderAsync(template, new[] { binding });
        IReadOnlyList<string> customXml =
            OpenXmlTestDocumentFactory.ReadAllCustomXmlParts(result.GetBytesCopy());

        Assert.Contains(customXml, xml =>
            xml.Contains("urn:other-software", StringComparison.Ordinal));
        Assert.Contains(customXml, xml =>
            xml.Contains("urn:word-template-binding:bindings:v1", StringComparison.Ordinal));
    }

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
            TargetKind = BindingTargetKind.Text,
            LocatorId = item.LocatorId,
            DataPath = path,
            DataType = type,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static TemplateBinding CreateChartBinding(Guid templateId, ChartTemplateItem chart)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TemplateBinding
        {
            TemplateId = templateId,
            TargetKind = BindingTargetKind.Chart,
            LocatorId = chart.LocatorId,
            DataPath = "ChartData.ScienceScores",
            DataType = DataValueType.Array,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
