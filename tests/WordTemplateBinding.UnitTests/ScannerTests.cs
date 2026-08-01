using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证跨 Run 小数识别、重复值定位和异常模板处理。
/// </summary>
public sealed class ScannerTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();

    /// <summary>
    /// 验证单个 Text 节点中的小数可以被识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_SingleTextDecimal_ReturnsOneItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5 分");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("88.5", item.MockValue);
        Assert.Equal(5, item.Locator.StartOffset);
        Assert.Equal(43, item.LocatorId.Length);
    }

    /// <summary>
    /// 验证同一段落中的多个小数分别生成定位。
    /// </summary>
    [Fact]
    public async Task ScanAsync_MultipleDecimalsInParagraph_ReturnsAllItems()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "一班 88.5，二班 92.30。");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(new[] { "88.5", "92.30" }, result.MockItems.Select(item => item.MockValue));
        Assert.Equal(new[] { 0, 1 }, result.MockItems.Select(item => item.Locator.OccurrenceIndex));
    }

    /// <summary>
    /// 验证相同数字在不同位置具有不同 LocatorId。
    /// </summary>
    [Fact]
    public async Task ScanAsync_RepeatedValues_ReturnsDistinctLocatorIds()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateMultipleParagraphDocument(
            "一班 88.5",
            "二班 88.5",
            "三班 88.5");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(3, result.MockItems.Count);
        Assert.Equal(3, result.MockItems.Select(item => item.LocatorId).Distinct().Count());
    }

    /// <summary>
    /// 验证数字跨两个 Run 时仍按完整文本识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_NumberAcrossTwoRuns_ReturnsOneItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("成绩 ", "88.", "5");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal("88.5", Assert.Single(result.MockItems).MockValue);
    }

    /// <summary>
    /// 验证数字跨三个 Text 节点时仍按完整文本识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_NumberAcrossThreeTextNodes_ReturnsOneItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("88", ".", "5");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal("88.5", Assert.Single(result.MockItems).MockValue);
    }

    /// <summary>
    /// 验证黄色高亮文字和完整数字按高亮范围识别，并沿用可确定的数值类型。
    /// </summary>
    [Fact]
    public async Task ScanAsync_YellowHighlights_ReturnsExactRangesWithInferredTypes()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateHighlightedParagraphDocument(
            ("四年级", true),
            ("学生平均分为", false),
            ("543", true),
            ("分，比例为", false),
            ("9.2%", true));

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(
            new[] { "四年级", "543", "9.2%" },
            result.MockItems.Select(item => item.MockValue));
        Assert.Equal(
            new[] { MockDataType.String, MockDataType.Integer, MockDataType.String },
            result.MockItems.Select(item => item.DataType));
        Assert.Equal(
            new[] { "四年级", "543", "9.2%" },
            result.MockItems.Select(item => item.Locator.OriginalValue));
    }

    /// <summary>
    /// 验证 Word w:shd 黄色填充与标准 Highlight 使用相同的显式标记语义。
    /// </summary>
    [Fact]
    public async Task ScanAsync_YellowShading_ReturnsExplicitRange()
    {
        byte[] bytes =
            OpenXmlTestDocumentFactory.CreateShadedParagraphDocument("底纹标记");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("底纹标记", item.MockValue);
        Assert.Equal("YellowHighlight", item.RecognitionKind);
        Assert.Equal(MockDataType.String, item.DataType);
    }

    /// <summary>
    /// 验证连续的黄色 Run 会合并为一个人工标记范围。
    /// </summary>
    [Fact]
    public async Task ScanAsync_YellowNumberAcrossRuns_ReturnsOneMergedItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateHighlightedParagraphDocument(
            ("成绩为", false),
            ("5", true),
            ("1", true),
            ("8", true),
            ("分", false));

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("518", item.MockValue);
        Assert.Equal(MockDataType.Integer, item.DataType);
        Assert.Equal(3, item.Locator.Length);
    }

    /// <summary>
    /// 验证黄色高亮优先于与其部分重叠的整段数字正则，同时保留其他自动识别结果。
    /// </summary>
    [Fact]
    public async Task ScanAsync_PartialYellowNumber_DropsOverlappingRegexCandidate()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateHighlightedParagraphDocument(
            ("值 1", false),
            ("23", true),
            ("，另一个值 45.6", false));

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(
            new[] { "23", "45.6" },
            result.MockItems.Select(item => item.MockValue));
        Assert.Equal(
            new[] { MockDataType.Integer, MockDataType.Decimal },
            result.MockItems.Select(item => item.DataType));
        Assert.DoesNotContain(
            result.MockItems,
            item => item.Locator.OriginalValue == "123");
    }

    /// <summary>
    /// 验证表格单元格普通段落中的小数被纳入扫描。
    /// </summary>
    [Fact]
    public async Task ScanAsync_DecimalInsideTable_ReturnsOneItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateTableDocument("表格成绩 76.5");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal("76.5", Assert.Single(result.MockItems).MockValue);
    }

    /// <summary>验证学院专业表会作为一个数组表格识别，并生成列映射与学院上下文。</summary>
    [Fact]
    public async Task ScanAsync_CollegeMajorTable_ReturnsBindableTable()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateBusinessTableDocument(
            "2. 电子工程学院（大气探测学院）基本信息",
            new[]
            {
                "序号", "专业代码", "专业名称", "学位门类", "设置年份",
                "是否新专业", "专业学生数", "专任教师数", "监测结果",
            },
            new[]
            {
                "1", "070601", "大气科学", "理学", "1979",
                "否", "1031", "76", "特色",
            });

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        TableTemplateItem table = Assert.Single(result.Tables);
        Assert.True(table.IsBindable);
        Assert.Equal("majorColleges", table.SuggestedSourcePath);
        Assert.Equal("电子工程学院（大气探测学院）", table.ContextLabel);
        Assert.Equal("majorCode", table.Columns[1].SuggestedField);
        Assert.Equal("monitoringDisplay", table.Columns[8].SuggestedField);
        Assert.DoesNotContain(result.MockItems, item => item.MockValue == "070601");
    }

    /// <summary>验证项目内的 2024 高校报告模板可完整识别 19 张业务表格。</summary>
    [Fact]
    public async Task ScanAsync_HigherEducationReferenceTemplate_RecognizesAllTables()
    {
        string path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../docs/四川省高校本科专业教学质量监测报告_彩色_2024(成都信息工程大学)-1.docx"));
        byte[] bytes = await File.ReadAllBytesAsync(path);

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(19, result.Tables.Count);
        Assert.All(result.Tables, table => Assert.True(table.IsBindable));
        Assert.Equal(
            16,
            result.Tables.Count(table => table.SuggestedSourcePath == "majorColleges"));
        Assert.Contains(
            result.Tables,
            table => table.ContextLabel == "人工智能学院（区块链产业学院）");
    }

    /// <summary>
    /// 验证不含小数点的普通整数会作为整数模拟数据识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_IntegerOnly_ReturnsIntegerItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("学生人数 1200");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("1200", item.MockValue);
        Assert.Equal(MockDataType.Integer, item.DataType);
    }

    /// <summary>
    /// 验证负数和千位分隔小数均可识别。
    /// </summary>
    /// <param name="text">测试段落文本。</param>
    /// <param name="expected">期望识别值。</param>
    [Theory]
    [InlineData("增长率 -12.5", "-12.5")]
    [InlineData("总额 1,234.56", "1,234.56")]
    public async Task ScanAsync_SupportedDecimalFormats_ReturnsExpected(
        string text,
        string expected)
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(text);

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(expected, Assert.Single(result.MockItems).MockValue);
    }

    /// <summary>
    /// 验证小数紧贴中文或百分号时仍能完整识别。
    /// </summary>
    /// <param name="text">测试段落文本。</param>
    /// <param name="expected">期望识别值。</param>
    [Theory]
    [InlineData("平均成绩88.5分", "88.5")]
    [InlineData("增长率-12.5%", "-12.5")]
    [InlineData("总额1,234.56元", "1,234.56")]
    public async Task ScanAsync_DecimalWithoutSpaces_ReturnsCompleteValue(
        string text,
        string expected)
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument(text));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal(expected, item.MockValue);
        Assert.Equal(MockDataType.Decimal, item.DataType);
    }

    /// <summary>
    /// 验证版本号、ASCII 标识符和格式错误的数字不会被部分识别。
    /// </summary>
    /// <param name="text">测试段落文本。</param>
    [Theory]
    [InlineData("版本v1.2.3")]
    [InlineData("编号ABC12.5X")]
    [InlineData("异常1,23.45元")]
    [InlineData("异常12.5.6")]
    public async Task ScanAsync_AmbiguousOrMalformedNumber_ReturnsNoItems(string text)
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument(text));

        Assert.Empty(result.MockItems);
    }

    /// <summary>
    /// 验证整数支持无空格、负号和千位分隔格式。
    /// </summary>
    /// <param name="text">测试段落文本。</param>
    /// <param name="expected">期望识别值。</param>
    [Theory]
    [InlineData("学生人数1200人", "1200")]
    [InlineData("库存-10件", "-10")]
    [InlineData("总人数1,234人", "1,234")]
    public async Task ScanAsync_SupportedIntegerFormats_ReturnsExpected(
        string text,
        string expected)
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument(text));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal(expected, item.MockValue);
        Assert.Equal(MockDataType.Integer, item.DataType);
    }

    /// <summary>
    /// 验证截图中的紧邻中文整数和百分比小数均由现有正则完整识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_ChineseTextAndPercentages_ReturnsAllNumbers()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument(
            "平均分为226，高于全省36分；比例分别为7.3%、19.2%、32.4%、47.3%。");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        Assert.Equal(
            new[] { "226", "36", "7.3", "19.2", "32.4", "47.3" },
            result.MockItems.Select(item => item.MockValue));
    }

    /// <summary>
    /// 验证默认页脚中的数字会被扫描并获得 FooterPart 定位。
    /// </summary>
    [Fact]
    public async Task ScanAsync_NumberInsideFooter_ReturnsFooterItem()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateBodyAndFooterDocument(
            "正文没有模拟值",
            "页脚统计值 88.5 分");

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("88.5", item.MockValue);
        Assert.Equal(DocumentPartKind.Footer, item.Locator.PartKind);
        Assert.StartsWith("/word/footer", item.Locator.PartKey, StringComparison.Ordinal);
        Assert.EndsWith(".xml", item.Locator.PartKey, StringComparison.Ordinal);
        Assert.Equal(0, item.Locator.ParagraphIndex);
        Assert.Equal(1, item.PreviewParagraphIndex);
    }

    /// <summary>
    /// 验证显式文字标记以内部文字作为模拟值、以完整标记作为替换范围。
    /// </summary>
    [Fact]
    public async Task ScanAsync_ExplicitTextMarker_ReturnsStringItem()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument("标题{{text:年度报告}}结束"));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("年度报告", item.MockValue);
        Assert.Equal(MockDataType.String, item.DataType);
        Assert.Equal("{{text:年度报告}}", item.Locator.OriginalValue);
        Assert.Equal("{{text:年度报告}}".Length, item.Locator.Length);
    }

    /// <summary>
    /// 验证不带 text: 前缀的双花括号文字标记也会被识别。
    /// </summary>
    [Theory]
    [InlineData("{{学习态度}}", "学习态度")]
    [InlineData("{{维度}}", "维度")]
    public async Task ScanAsync_PlainTextMarker_ReturnsStringItem(
        string marker,
        string expectedValue)
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument(marker));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal(expectedValue, item.MockValue);
        Assert.Equal(MockDataType.String, item.DataType);
        Assert.Equal(marker, item.Locator.OriginalValue);
        Assert.Equal(marker.Length, item.Locator.Length);
    }

    /// <summary>
    /// 验证空双花括号不会成为可自动绑定的字段路径候选。
    /// </summary>
    [Fact]
    public async Task ScanAsync_EmptyPlaceholder_ReturnsNoItems()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument("{{}}"));

        Assert.Empty(result.MockItems);
    }

    /// <summary>
    /// 验证显式文字标记跨 Run 时仍作为一个字符串模拟数据识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_ExplicitTextMarkerAcrossRuns_ReturnsOneItem()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument("{{text:", "年度", "报告}}"));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("年度报告", item.MockValue);
        Assert.Equal(MockDataType.String, item.DataType);
    }

    /// <summary>
    /// 验证文字标记内部的数字不会产生重叠的数字模拟数据。
    /// </summary>
    [Fact]
    public async Task ScanAsync_NumberInsideTextMarker_ReturnsOnlyStringItem()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument("版本{{text:第12.5版}}"));

        MockDataItem item = Assert.Single(result.MockItems);
        Assert.Equal("第12.5版", item.MockValue);
        Assert.Equal(MockDataType.String, item.DataType);
    }

    /// <summary>
    /// 验证旧的双方括号文字语法不再被识别。
    /// </summary>
    [Fact]
    public async Task ScanAsync_LegacyTextMarker_ReturnsNoItems()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateParagraphDocument("标题[[text:年度报告]]"));

        Assert.Empty(result.MockItems);
    }

    /// <summary>
    /// 验证 Word 原生图表会返回稳定部件定位、分类和系列缓存。
    /// </summary>
    [Fact]
    public async Task ScanAsync_NativeChart_ReturnsBindableChartMetadata()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();

        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.Equal("bar", chart.ChartType);
        Assert.Equal(new[] { "四年级", "八年级" }, chart.Categories);
        Assert.Equal(new[] { "你县", "全省" }, chart.Series.Select(series => series.Name));
        Assert.StartsWith("/word/charts/chart", chart.Locator.PartKey, StringComparison.Ordinal);
        Assert.True(chart.IsBindable);
        Assert.Empty(result.MockItems);
    }

    /// <summary>
    /// 验证相同模板重复扫描得到稳定的 LocatorId。
    /// </summary>
    [Fact]
    public async Task ScanAsync_SameTemplateTwice_ReturnsStableLocatorId()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateParagraphDocument("平均成绩 88.5");

        TemplateScanResult first = await _scanner.ScanAsync(bytes);
        TemplateScanResult second = await _scanner.ScanAsync(bytes);

        Assert.Equal(
            Assert.Single(first.MockItems).LocatorId,
            Assert.Single(second.MockItems).LocatorId);
    }

    /// <summary>
    /// 验证损坏字节会转换为明确的无效模板异常。
    /// </summary>
    [Fact]
    public async Task ScanAsync_CorruptedBytes_ThrowsInvalidTemplate()
    {
        byte[] bytes = { 1, 2, 3, 4, 5 };

        await Assert.ThrowsAsync<InvalidTemplateFileException>(
            () => _scanner.ScanAsync(bytes));
    }
}
