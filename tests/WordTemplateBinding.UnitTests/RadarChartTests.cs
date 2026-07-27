using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;

#pragma warning disable CS1591
namespace WordTemplateBinding.UnitTests;

public sealed class RadarChartTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();
    private readonly IWordReportRenderer _renderer = TestServiceFactory.CreateRenderer();

    [Theory]
    [InlineData("standard")]
    [InlineData("marker")]
    [InlineData("filled")]
    public async Task Analyze_RadarStyle_IsParsedAndBindable(string style)
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument(style));

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.Equal("radar", chart.ChartType);
        Assert.True(chart.IsBindable);
        Assert.True(chart.Analysis!.Chart.SupportedForBinding);
        Assert.Equal(style, Assert.Single(chart.Analysis.PlotGroups).RadarStyle);
        Assert.Equal("cache-only", chart.DataDefinition!.WriteCapability);
        Assert.Equal(new[] { "指标A", "指标B", "指标C", "指标D", "指标E" }, chart.Categories);
        Assert.Equal(new[] { "学校值", "全省值" }, chart.Series.Select(item => item.Name));
        Assert.Equal(0m, chart.Analysis.Chart.RadarMinimum);
        Assert.Equal(100m, chart.Analysis.Chart.RadarMaximum);
        Assert.Contains(chart.Analysis.Source.Formulas, item =>
            item.Role == "Category" && item.Formula == "RadarData!$B$1:$F$1");
        Assert.Contains(chart.Analysis.Source.Formulas, item =>
            item.Role == "Value" && item.Formula == "RadarData!$B$2:$F$2");
    }

    [Fact]
    public async Task Analyze_UnknownRadarStyle_FallsBackWithWarning()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument("customRadar"));

        ChartAnalysisSnapshot analysis = Assert.Single(result.Charts).Analysis!;
        Assert.Equal("standard", Assert.Single(analysis.PlotGroups).RadarStyle);
        Assert.Contains(analysis.Diagnostics.Items, item => item.Code == "radar_unknown_style");
    }

    [Fact]
    public async Task Analyze_RadarWithoutExplicitRange_DerivesReadableRange()
    {
        TemplateScanResult result = await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument(includeAxisRange: false));

        ChartDefinitionSnapshot definition = Assert.Single(result.Charts).Analysis!.Chart;
        Assert.Equal(0m, definition.RadarMinimum);
        Assert.Equal(100m, definition.RadarMaximum);
    }

    [Fact]
    public async Task Analyze_RadarWithoutCategories_IsNotBindable()
    {
        ChartTemplateItem chart = Assert.Single((await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument(includeCategories: false))).Charts);

        Assert.False(chart.IsBindable);
        Assert.False(chart.Analysis!.Chart.SupportedForBinding);
        Assert.Equal("unsupported", chart.DataDefinition!.WriteCapability);
        Assert.Contains(chart.Analysis.Diagnostics.Items, item => item.Code == "radar_missing_categories");
    }

    [Fact]
    public async Task Analyze_RadarWithoutValueCache_IsNotBindable()
    {
        ChartTemplateItem chart = Assert.Single((await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument(includeValueCache: false))).Charts);

        Assert.False(chart.IsBindable);
        Assert.False(chart.Analysis!.Chart.SupportedForBinding);
        Assert.Equal("unsupported", chart.DataDefinition!.WriteCapability);
        Assert.Contains(chart.Analysis.Diagnostics.Items, item => item.Code == "radar_missing_value_cache");
    }

    [Fact]
    public async Task Analyze_RadarSeriesLengthMismatch_ProducesDiagnostic()
    {
        ChartTemplateItem chart = Assert.Single((await _scanner.ScanAsync(
            OpenXmlTestDocumentFactory.CreateRadarChartDocument(seriesLengthMismatch: true))).Charts);

        Assert.True(chart.IsBindable);
        Assert.Contains(chart.Analysis!.Diagnostics.Items, item =>
            item.Code == "radar_series_length_mismatch" && item.SeriesIndex == 1);
    }

    [Fact]
    public async Task Render_RadarBinding_UpdatesCachesFormulasAndPreservesNativeStyle()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateRadarChartDocument("marker");
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = Assert.Single(scan.Charts);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        string originalShapeXml = ReadFirstSeriesShapeXml(bytes);

        TemplateBinding binding = new()
        {
            TemplateId = template.Id,
            TargetKind = BindingTargetKind.Chart,
            LocatorId = chart.LocatorId,
            DataPath = "Radar.Scores",
            DataType = DataValueType.Array,
            ChartMapping = new ChartBindingMapping
            {
                Mode = "category-series",
                CategoryField = "Category",
                SeriesMappings = new[]
                {
                    new ChartSeriesFieldMapping
                    {
                        SeriesIndex = 0,
                        SeriesKey = "series-0",
                        ValueField = "School",
                        SeriesNameField = "SchoolName",
                    },
                    new ChartSeriesFieldMapping
                    {
                        SeriesIndex = 1,
                        SeriesKey = "series-1",
                        ValueField = "Province",
                        SeriesNameField = "ProvinceName",
                    },
                },
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            new IReadOnlyDictionary<string, object?>[]
            {
                Row("培养质量", 82m, 75m),
                Row("师资水平", 91m, 80m),
                Row("科研能力", 76m, 72m),
                Row("社会服务", 68m, 70m),
                Row("创新能力", null, 77m),
                Row("国际影响", 73m, 69m),
            };

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = rows });
        byte[] output = report.GetBytesCopy();

        using (MemoryStream stream = new(output, writable: false))
        using (WordprocessingDocument document = WordprocessingDocument.Open(stream, false))
        {
            ChartPart part = Assert.Single(document.MainDocumentPart!.ChartParts);
            C.RadarChart radar = Assert.Single(part.ChartSpace.Descendants<C.RadarChart>());
            Assert.Equal("marker", radar.GetFirstChild<C.RadarStyle>()!
                .GetAttribute("val", string.Empty).Value);
            Assert.NotNull(part.ChartSpace.Descendants<C.Legend>().SingleOrDefault());
            Assert.Equal(originalShapeXml, part.ChartSpace
                .Descendants<C.RadarChartSeries>()
                .First()
                .GetFirstChild<C.ChartShapeProperties>()!.OuterXml);

            C.RadarChartSeries[] series = part.ChartSpace
                .Descendants<C.RadarChartSeries>()
                .ToArray();
            string[] categories = series[0]
                .GetFirstChild<C.CategoryAxisData>()!
                .Descendants<C.StringPoint>()
                .OrderBy(point => point.Index?.Value)
                .Select(point => point.NumericValue?.Text ?? string.Empty)
                .ToArray();
            Assert.Equal(6U, series[0].GetFirstChild<C.CategoryAxisData>()!
                .Descendants<C.PointCount>().Single().Val!.Value);
            Assert.Equal(
                new[] { "培养质量", "师资水平", "科研能力", "社会服务", "创新能力", "国际影响" },
                categories);
            Assert.Equal(
                "RadarData!$B$1:$G$1",
                series[0].GetFirstChild<C.CategoryAxisData>()!
                    .Descendants<C.Formula>().Single().Text);
            Assert.Equal(
                "RadarData!$B$2:$G$2",
                series[0].GetFirstChild<C.Values>()!
                    .Descendants<C.Formula>().Single().Text);
            Assert.Equal(6U, series[0].GetFirstChild<C.Values>()!
                .Descendants<C.PointCount>().Single().Val!.Value);

            C.NumericPoint missing = series[0].GetFirstChild<C.Values>()!
                .Descendants<C.NumericPoint>()
                .Single(point => point.Index?.Value == 4U);
            Assert.Equal(string.Empty, missing.NumericValue?.Text);
        }

        ChartTemplateItem rescanned = Assert.Single((await _scanner.ScanAsync(output)).Charts);
        Assert.Equal("radar", rescanned.ChartType);
        Assert.True(rescanned.IsBindable);
        Assert.Equal(new[] { "新学校值", "新全省值" }, rescanned.Series.Select(item => item.Name));
        Assert.Null(rescanned.Series[0].Values[4]);
        Assert.Equal("marker", Assert.Single(rescanned.Analysis!.PlotGroups).RadarStyle);
    }

    [Fact]
    public async Task Render_RadarBinding_SynchronizesEmbeddedWorkbook()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateRadarChartDocument(
            includeEmbeddedWorkbook: true);
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = Assert.Single(scan.Charts);
        Assert.Equal("workbook-and-cache", chart.DataDefinition!.WriteCapability);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateMappedBinding(template, chart);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            new IReadOnlyDictionary<string, object?>[]
            {
                Row("培养质量", 82m, 75m),
                Row("师资水平", 91m, 80m),
                Row("科研能力", 76m, 72m),
                Row("社会服务", 68m, 70m),
                Row("创新能力", 85m, 77m),
                Row("国际影响", 73m, 69m),
            };

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = rows });

        using MemoryStream documentStream = new(report.GetBytesCopy(), writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(documentStream, false);
        ChartPart chartPart = Assert.Single(document.MainDocumentPart!.ChartParts);
        EmbeddedPackagePart packagePart = Assert.IsType<EmbeddedPackagePart>(
            chartPart.Parts.Single(pair => pair.OpenXmlPart is EmbeddedPackagePart).OpenXmlPart);
        using Stream workbookStream = packagePart.GetStream(FileMode.Open, FileAccess.Read);
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookStream, false);
        WorkbookPart workbookPart = workbook.WorkbookPart!;
        S.Sheet sheet = Assert.Single(workbookPart.Workbook.Sheets!.Elements<S.Sheet>());
        WorksheetPart worksheetPart = Assert.IsType<WorksheetPart>(
            workbookPart.GetPartById(sheet.Id!.Value!));
        S.SheetData data = worksheetPart.Worksheet.GetFirstChild<S.SheetData>()!;

        Assert.Equal("培养质量", ReadCell(data, "B1"));
        Assert.Equal("国际影响", ReadCell(data, "G1"));
        Assert.Equal("新学校值", ReadCell(data, "A2"));
        Assert.Equal("73", ReadCell(data, "G2"));
        Assert.Equal("新全省值", ReadCell(data, "A3"));
        Assert.Equal("69", ReadCell(data, "G3"));
    }

    [Fact]
    public async Task Render_RadarBinding_ReducesCategoriesAndClearsOldPoints()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateRadarChartDocument(
            includeEmbeddedWorkbook: true);
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = Assert.Single(scan.Charts);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateMappedBinding(template, chart);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            new IReadOnlyDictionary<string, object?>[]
            {
                Row("培养质量", 82m, 75m),
                Row("师资水平", 91m, 80m),
                Row("科研能力", 76m, 72m),
            };

        RenderedReport report = await _renderer.RenderAsync(
            template,
            new[] { binding },
            new Dictionary<string, object?> { [binding.DataPath] = rows });
        byte[] output = report.GetBytesCopy();

        using MemoryStream documentStream = new(output, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(documentStream, false);
        ChartPart chartPart = Assert.Single(document.MainDocumentPart!.ChartParts);
        C.RadarChartSeries firstSeries = chartPart.ChartSpace
            .Descendants<C.RadarChartSeries>()
            .First();
        C.CategoryAxisData categoryData = firstSeries.GetFirstChild<C.CategoryAxisData>()!;
        Assert.Equal(3U, categoryData.Descendants<C.PointCount>().Single().Val!.Value);
        Assert.Equal(3, categoryData.Descendants<C.StringPoint>().Count());
        Assert.Equal("RadarData!$B$1:$D$1", categoryData.Descendants<C.Formula>().Single().Text);
        Assert.Equal(3, firstSeries.GetFirstChild<C.Values>()!.Descendants<C.NumericPoint>().Count());

        EmbeddedPackagePart packagePart = Assert.IsType<EmbeddedPackagePart>(
            chartPart.Parts.Single(pair => pair.OpenXmlPart is EmbeddedPackagePart).OpenXmlPart);
        using Stream workbookStream = packagePart.GetStream(FileMode.Open, FileAccess.Read);
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(workbookStream, false);
        WorkbookPart workbookPart = workbook.WorkbookPart!;
        S.Sheet sheet = Assert.Single(workbookPart.Workbook.Sheets!.Elements<S.Sheet>());
        WorksheetPart worksheetPart = Assert.IsType<WorksheetPart>(
            workbookPart.GetPartById(sheet.Id!.Value!));
        S.SheetData data = worksheetPart.Worksheet.GetFirstChild<S.SheetData>()!;
        Assert.Equal(string.Empty, ReadCell(data, "E1"));
        Assert.Equal(string.Empty, ReadCell(data, "F1"));
        Assert.Equal(string.Empty, ReadCell(data, "E2"));
        Assert.Equal(string.Empty, ReadCell(data, "F2"));
    }

    [Fact]
    public async Task Render_RadarBinding_RejectsFewerThanThreeIndicators()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateRadarChartDocument();
        TemplateScanResult scan = await _scanner.ScanAsync(bytes);
        ChartTemplateItem chart = Assert.Single(scan.Charts);
        TemplateDocument template = TestServiceFactory.CreateTemplate(bytes, scan);
        TemplateBinding binding = CreateMappedBinding(template, chart);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            new IReadOnlyDictionary<string, object?>[]
            {
                Row("指标A", 1m, 2m),
                Row("指标B", 3m, 4m),
            };

        DataValueConversionException exception =
            await Assert.ThrowsAsync<DataValueConversionException>(() => _renderer.RenderAsync(
                template,
                new[] { binding },
                new Dictionary<string, object?> { [binding.DataPath] = rows }));
        Assert.NotNull(exception.InnerException);
        Assert.Contains("雷达图至少需要 3 个指标", exception.InnerException.Message);
    }

    private static IReadOnlyDictionary<string, object?> Row(
        string category,
        decimal? school,
        decimal? province) =>
        new Dictionary<string, object?>
        {
            ["Category"] = category,
            ["School"] = school,
            ["Province"] = province,
            ["SchoolName"] = "新学校值",
            ["ProvinceName"] = "新全省值",
        };

    private static TemplateBinding CreateMappedBinding(
        TemplateDocument template,
        ChartTemplateItem chart) => new()
    {
        TemplateId = template.Id,
        TargetKind = BindingTargetKind.Chart,
        LocatorId = chart.LocatorId,
        DataPath = "Radar.Scores",
        DataType = DataValueType.Array,
        ChartMapping = new ChartBindingMapping
        {
            Mode = "category-series",
            CategoryField = "Category",
            SeriesMappings = new[]
            {
                new ChartSeriesFieldMapping
                {
                    SeriesIndex = 0,
                    SeriesKey = "series-0",
                    ValueField = "School",
                    SeriesNameField = "SchoolName",
                },
                new ChartSeriesFieldMapping
                {
                    SeriesIndex = 1,
                    SeriesKey = "series-1",
                    ValueField = "Province",
                    SeriesNameField = "ProvinceName",
                },
            },
        },
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static string ReadCell(S.SheetData data, string reference) =>
        data.Descendants<S.Cell>()
            .Single(cell => cell.CellReference?.Value == reference)
            .CellValue?.Text ?? string.Empty;

    private static string ReadFirstSeriesShapeXml(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return Assert.Single(document.MainDocumentPart!.ChartParts)
            .ChartSpace
            .Descendants<C.RadarChartSeries>()
            .First()
            .GetFirstChild<C.ChartShapeProperties>()!
            .OuterXml;
    }
}
#pragma warning restore CS1591
