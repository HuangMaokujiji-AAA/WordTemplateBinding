using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

#pragma warning disable CS1591
namespace WordTemplateBinding.UnitTests;

public sealed class ChartAnalysisTests
{
    private readonly IWordTemplateScanner _scanner = TestServiceFactory.CreateScanner();

    [Fact]
    public async Task ChartAnalysis_Present()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.NotNull(chart.Analysis);
        Assert.Equal("1.0", chart.Analysis!.SchemaVersion);
    }

    [Fact]
    public async Task ChartAnalysis_Identity()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        ChartIdentitySnapshot identity = chart.Analysis!.Identity;

        Assert.Equal(chart.LocatorId, identity.LocatorId);
        Assert.Equal(chart.Locator.PartKey, identity.PartKey);
        Assert.Equal(chart.Locator.RelationshipId, identity.RelationshipId);
        Assert.Equal(chart.Locator.DocumentOrder, identity.DocumentOrder);
    }

    [Fact]
    public async Task ChartAnalysis_ChartType()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.Equal("bar", chart.Analysis!.Chart.Type);
        Assert.Equal("条形图", chart.Analysis!.Chart.TypeLabel);
        Assert.True(chart.Analysis!.Chart.SupportedForBinding);
    }

    [Fact]
    public async Task ChartAnalysis_Series()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        IReadOnlyList<ChartSeriesSnapshot> series = chart.Analysis!.Series;

        Assert.Equal(2, series.Count);
        Assert.Equal("series-0", series[0].Key);
        Assert.Equal(0, series[0].SeriesIndex);
        Assert.Equal("你县", series[0].Name);
        Assert.Equal("series-1", series[1].Key);
        Assert.Equal(1, series[1].SeriesIndex);
        Assert.Equal("全省", series[1].Name);
    }

    [Fact]
    public async Task ChartAnalysis_DataPoints()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        ChartSeriesSnapshot series0 = chart.Analysis!.Series[0];

        Assert.Equal(2, series0.Values.Count);
        Assert.Equal(0, series0.Values[0].Index);
        Assert.Equal(543m, series0.Values[0].Value);
        Assert.False(series0.Values[0].IsMissing);
        Assert.Equal(1, series0.Values[1].Index);
        Assert.Equal(505m, series0.Values[1].Value);
        Assert.False(series0.Values[1].IsMissing);
    }

    [Fact]
    public async Task ChartAnalysis_Categories()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        IReadOnlyList<ChartCategorySnapshot> categories = chart.Analysis!.Categories;

        Assert.Equal(2, categories.Count);
        Assert.Equal(0, categories[0].Index);
        Assert.Equal("四年级", categories[0].Value);
        Assert.Equal(1, categories[1].Index);
        Assert.Equal("八年级", categories[1].Value);
    }

    [Fact]
    public async Task ChartAnalysis_DataTable()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        ChartDataTableSnapshot dataTable = chart.Analysis!.DataTable;

        Assert.Equal("categories-as-rows", dataTable.Orientation);
        Assert.Equal(3, dataTable.ColumnCount); // category + 2 series
        Assert.Equal(2, dataTable.RowCount);
        Assert.Equal("Category", dataTable.Columns[0].Label);
        Assert.Equal("你县", dataTable.Columns[1].Label);
    }

    [Fact]
    public async Task ChartAnalysis_BindingContract()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        ChartBindingContract contract = chart.Analysis!.BindingContract;

        Assert.Equal("whole-dataset", contract.Mode);
        Assert.Equal("Category", contract.CategoryProperty);
        Assert.Equal(2, contract.SeriesFields.Count);
        Assert.Equal("series-0", contract.SeriesFields[0].SeriesKey);
        Assert.Equal("你县", contract.SeriesFields[0].PayloadProperty);
        Assert.Equal("series-1", contract.SeriesFields[1].SeriesKey);
        Assert.Equal("全省", contract.SeriesFields[1].PayloadProperty);

        Assert.NotNull(contract.SampleReplacementPayload);
        Assert.NotNull(contract.ReportRequestExample);
    }

    [Fact]
    public async Task ChartAnalysis_SamplePayload_MatchesWriterFormat()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        object payload = chart.Analysis!.BindingContract.SampleReplacementPayload;

        // Verify it can be serialized (JSON may escape CJK characters)
        string json = System.Text.Json.JsonSerializer.Serialize(payload);
        Assert.NotNull(json);
        Assert.Contains("Category", json);
        Assert.Contains("543", json);
        Assert.Contains("505", json);
        // CJK characters may be escaped: use relaxed escaping to verify series names
        var relaxedOptions = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string relaxedJson = System.Text.Json.JsonSerializer.Serialize(payload, relaxedOptions);
        Assert.Contains("你县", relaxedJson);
        Assert.Contains("全省", relaxedJson);
    }

    [Fact]
    public async Task ChartAnalysis_Diagnostics()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        ChartAnalysisDiagnostics diagnostics = chart.Analysis!.Diagnostics;

        Assert.False(diagnostics.HasErrors);
        Assert.InRange(diagnostics.CompletenessScore, 80, 100);
    }

    [Fact]
    public async Task ChartAnalysis_Serializable()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        string json = System.Text.Json.JsonSerializer.Serialize(chart.Analysis);

        Assert.NotNull(json);
        // Verify no OpenXml SDK types or raw XML are serialized
        Assert.DoesNotContain("\"OpenXmlElement\"", json);
        Assert.DoesNotContain("\"ChartPart\"", json);
        Assert.DoesNotContain("\"WordprocessingDocument\"", json);
        Assert.DoesNotContain("\"byte[]\"", json);
        Assert.DoesNotContain("\"rawXml\"", json);
        Assert.DoesNotContain("DocumentFormat.OpenXml", json);
    }

    [Fact]
    public async Task ChartAnalysis_FormulaPresent()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.NotEmpty(chart.Analysis!.Source.Formulas);
        Assert.Contains(chart.Analysis!.Source.Formulas, f => f.Role == "SeriesName");
        Assert.Contains(chart.Analysis!.Source.Formulas, f => f.Role == "Category");
        Assert.Contains(chart.Analysis!.Source.Formulas, f => f.Role == "Value");
    }

    [Fact]
    public async Task ChartAnalysis_PlotGroups()
    {
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.Single(chart.Analysis!.PlotGroups);
        ChartPlotGroupSnapshot group = chart.Analysis!.PlotGroups[0];
        Assert.Equal("plot-group-0", group.Id);
        Assert.Contains("series-0", group.SeriesKeys);
        Assert.Contains("series-1", group.SeriesKeys);
    }

    [Fact]
    public async Task ChartAnalysis_ExistingFunctionality_Unaffected()
    {
        // Verify that existing ChartTemplateItem fields are unchanged
        byte[] bytes = OpenXmlTestDocumentFactory.CreateChartDocument();
        TemplateScanResult result = await _scanner.ScanAsync(bytes);

        ChartTemplateItem chart = Assert.Single(result.Charts);
        Assert.Equal("bar", chart.ChartType);
        Assert.Equal("图表 1", chart.Title);
        Assert.Equal(2, chart.Categories.Count);
        Assert.Equal(2, chart.Series.Count);
        Assert.True(chart.IsBindable);
        Assert.False(chart.IsBound);
    }
}
#pragma warning restore CS1591
