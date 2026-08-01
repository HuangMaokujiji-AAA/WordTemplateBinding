using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Infrastructure.Database;
using WordTemplateBinding.Infrastructure.OpenXml;

namespace WordTemplateBinding.UnitTests;

/// <summary>验证高校专业指标会按专业转置成雷达图长表数据。</summary>
public sealed class HigherEducationMetricChartDataTests
{
    /// <summary>一个专业只生成自己的三级雷达图数组和四个正确系列。</summary>
    [Fact]
    public void BuildMajorMetricChartData_GroupsAndTransposesEachMajor()
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> majors =
            new[]
            {
                Row(
                    ("majorCode", "070601"),
                    ("majorName", "大气科学"),
                    ("collegeName", "大气科学学院")),
            };
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level1 = MetricRows(
            (71m, 72m, 73m),
            (81m, 82m, 83m),
            (91m, 92m, 93m),
            (61m, 62m, 63m));
        IReadOnlyList<IReadOnlyDictionary<string, object?>> emptyMetricRows = MetricRows(
            (1m, 1m, 1m),
            (2m, 2m, 2m),
            (3m, 3m, 3m),
            (4m, 4m, 4m));

        IReadOnlyDictionary<string, object?> grouped =
            MySqlHigherEducationReportDataProvider.BuildMajorMetricChartData(
                "10621",
                majors,
                level1,
                emptyMetricRows,
                emptyMetricRows);

        IReadOnlyDictionary<string, object?> major = Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, object?>>(grouped["070601"]);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> radarRows =
            Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                major["level1RadarData"]);
        Assert.Equal(new[] { "就业", "招生", "培养" },
            radarRows.Select(row => row["category"]));

        ChartDataSet dataSet = ChartDataSetParser.Parse(radarRows);
        Assert.Equal(new[] { "就业", "招生", "培养" }, dataSet.Categories);
        Assert.Equal(
            new[] { "平均值", "最大值", "最小值", "大气科学" },
            dataSet.Series.Select(series => series.Name));
        Assert.Equal(new decimal?[] { 81m, 82m, 83m }, dataSet.Series[0].Values);
        Assert.Equal(new decimal?[] { 71m, 72m, 73m }, dataSet.Series[3].Values);
    }

    /// <summary>省级对比行缺失时明确拒绝生成，避免再次输出畸形图表。</summary>
    [Fact]
    public void BuildMajorMetricChartData_MissingComparisonRowFailsClearly()
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> majors =
            new[]
            {
                Row(("majorCode", "070601"), ("majorName", "大气科学")),
            };
        IReadOnlyList<IReadOnlyDictionary<string, object?>> incomplete = MetricRows(
            (71m, 72m, 73m),
            (81m, 82m, 83m),
            (91m, 92m, 93m),
            (61m, 62m, 63m))
            .Where(row => !string.Equals(row["displayOrder"] as string, "03", StringComparison.Ordinal))
            .ToList();

        WorkspaceException exception = Assert.Throws<WorkspaceException>(() =>
            MySqlHigherEducationReportDataProvider.BuildMajorMetricChartData(
                "10621",
                majors,
                incomplete,
                incomplete,
                incomplete));

        Assert.Equal("higher_education_metric_incomplete", exception.ErrorCode);
        Assert.Contains("全省最低", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MetricRows(
        (decimal Employment, decimal Admission, decimal Cultivation) school,
        (decimal Employment, decimal Admission, decimal Cultivation) average,
        (decimal Employment, decimal Admission, decimal Cultivation) maximum,
        (decimal Employment, decimal Admission, decimal Cultivation) minimum) =>
        new[]
        {
            MetricRow("10621", "04", school),
            MetricRow("全省同专业", "01", average),
            MetricRow("全省同专业", "02", maximum),
            MetricRow("全省同专业", "03", minimum),
        };

    private static IReadOnlyDictionary<string, object?> MetricRow(
        string schoolCode,
        string displayOrder,
        (decimal Employment, decimal Admission, decimal Cultivation) values) =>
        Row(
            ("schoolCode", schoolCode),
            ("displayOrder", displayOrder),
            ("majorCode", "070601"),
            ("majorName", "大气科学"),
            ("s1EmploymentScore", values.Employment),
            ("s2AdmissionScore", values.Admission),
            ("s3CultivationScore", values.Cultivation));

    private static IReadOnlyDictionary<string, object?> Row(
        params (string Key, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
