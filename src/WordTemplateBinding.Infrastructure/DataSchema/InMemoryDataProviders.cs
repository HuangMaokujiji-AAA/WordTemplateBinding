using System.Collections.ObjectModel;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.DataSchema;

/// <summary>
/// 程序化生成约三千个叶子字段，并提供内存查找和搜索。
/// </summary>
public sealed class InMemoryDataSchemaProvider : IDataSchemaProvider
{
    private readonly IReadOnlyList<DataFieldNode> _roots;
    private readonly IReadOnlyDictionary<string, DataFieldDefinition> _definitions;
    private readonly IReadOnlyList<DataFieldNode> _searchableNodes;
    private readonly int _leafCount;

    /// <summary>
    /// 初始化演示字段树和扁平字段索引。
    /// </summary>
    public InMemoryDataSchemaProvider()
    {
        _roots = BuildSchema();
        List<DataFieldNode> allNodes = new();
        Flatten(_roots, allNodes);
        _searchableNodes = new ReadOnlyCollection<DataFieldNode>(allNodes);
        _definitions = new ReadOnlyDictionary<string, DataFieldDefinition>(
            allNodes
                .ToDictionary(
                    node => node.Path,
                    node => new DataFieldDefinition
                    {
                        Name = node.Name,
                        Path = node.Path,
                        Type = node.Type,
                        IsBindable = node.IsBindable,
                    },
                    StringComparer.Ordinal));
        _leafCount = allNodes.Count(node => node.IsLeaf);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DataFieldNode>> GetSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CloneNodes(_roots));
    }

    /// <inheritdoc />
    public Task<DataFieldDefinition?> FindByPathAsync(
        string dataPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _definitions.TryGetValue(dataPath, out DataFieldDefinition? definition);
        return Task.FromResult(definition is null ? null : definition with { });
    }

    /// <inheritdoc />
    public Task<DataSchemaSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = query.Trim();
        List<DataFieldNode> matches = _searchableNodes
            .Where(node =>
                node.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                node.Path.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        IReadOnlyList<DataFieldNode> returned = matches
            .Take(maxResults)
            .Select(CloneWithoutChildren)
            .ToList()
            .AsReadOnly();

        return Task.FromResult(new DataSchemaSearchResult
        {
            Nodes = returned,
            MatchCount = matches.Count,
            IsTruncated = matches.Count > maxResults,
        });
    }

    /// <inheritdoc />
    public Task<int> GetLeafCountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_leafCount);
    }

    /// <summary>
    /// 构建基础字段、数组示例和 100×30 个部门指标字段。
    /// </summary>
    /// <returns>返回只读根节点列表。</returns>
    private static IReadOnlyList<DataFieldNode> BuildSchema()
    {
        DataFieldNode studentStatistics = Branch(
            "学生统计",
            "StudentStatistics",
            DataValueType.String,
            new[]
            {
                Leaf("平均成绩", "StudentStatistics.AverageScore", DataValueType.Decimal),
                Leaf("学生人数", "StudentStatistics.StudentCount", DataValueType.Integer),
                Leaf("及格率", "StudentStatistics.PassRate", DataValueType.Decimal),
            });
        DataFieldNode report = Branch(
            "报告信息",
            "Report",
            DataValueType.String,
            new[]
            {
                Leaf("报告标题", "Report.Title", DataValueType.String),
                Leaf("报告字符1", "Report.Str1", DataValueType.String),
                Leaf("报告字符2", "Report.Str2", DataValueType.String),
                Leaf("报告字符3", "Report.Str3", DataValueType.String),           
                Leaf("报告日期", "Report.GeneratedAt", DataValueType.Date),
                Leaf("是否定稿", "Report.IsFinal", DataValueType.Boolean),
            });
        DataFieldNode students = new()
        {
            Name = "学生列表（后续阶段）",
            Path = "Students",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = false,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("姓名", "Students[].Name", DataValueType.String, false),
                Leaf("成绩", "Students[].Score", DataValueType.Decimal, false),
            }),
        };
        // ── 图表数据：科学成绩（双系列柱形图） ──
        DataFieldNode chartScienceScores = new()
        {
            Name = "图表数据 · 科学成绩",
            Path = "ChartData.ScienceScores",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = true,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("分类", "ChartData.ScienceScores[].Category", DataValueType.String, false),
                Leaf("你县", "ChartData.ScienceScores[].你县", DataValueType.Decimal, false),
                Leaf("全省", "ChartData.ScienceScores[].全省", DataValueType.Decimal, false),
            }),
        };

        // ── 图表数据：季度销售（三系列柱形/折线图） ──
        DataFieldNode chartQuarterlySales = new()
        {
            Name = "图表数据 · 季度销售",
            Path = "ChartData.QuarterlySales",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = true,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("季度", "ChartData.QuarterlySales[].quarter", DataValueType.String, false),
                Leaf("线上销售额", "ChartData.QuarterlySales[].onlineSales", DataValueType.Decimal, false),
                Leaf("线下销售额", "ChartData.QuarterlySales[].offlineSales", DataValueType.Decimal, false),
                Leaf("总销售额", "ChartData.QuarterlySales[].totalSales", DataValueType.Decimal, false),
            }),
        };

        // ── 图表数据：区域 GDP（单系列条形图） ──
        DataFieldNode chartRegionGdp = new()
        {
            Name = "图表数据 · 区域 GDP",
            Path = "ChartData.RegionGDP",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = true,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("区域", "ChartData.RegionGDP[].region", DataValueType.String, false),
                Leaf("GDP（亿元）", "ChartData.RegionGDP[].gdp", DataValueType.Decimal, false),
                Leaf("增速（%）", "ChartData.RegionGDP[].growthRate", DataValueType.Decimal, false),
            }),
        };

        // ── 图表数据：产品满意度评分（饼图/环形图） ──
        DataFieldNode chartProductRatings = new()
        {
            Name = "图表数据 · 产品评分",
            Path = "ChartData.ProductRatings",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = true,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("评分维度", "ChartData.ProductRatings[].dimension", DataValueType.String, false),
                Leaf("满意度（%）", "ChartData.ProductRatings[].satisfaction", DataValueType.Decimal, false),
                Leaf("推荐率（%）", "ChartData.ProductRatings[].recommendation", DataValueType.Decimal, false),
            }),
        };

        // ── 图表数据：气温趋势（折线图：最高/最低/平均） ──
        DataFieldNode chartTemperature = new()
        {
            Name = "图表数据 · 月度气温",
            Path = "ChartData.MonthlyTemperature",
            Type = DataValueType.Array,
            IsCollection = true,
            IsLeaf = false,
            IsBindable = true,
            Children = new ReadOnlyCollection<DataFieldNode>(new[]
            {
                Leaf("月份", "ChartData.MonthlyTemperature[].month", DataValueType.String, false),
                Leaf("最高气温", "ChartData.MonthlyTemperature[].maxTemp", DataValueType.Decimal, false),
                Leaf("最低气温", "ChartData.MonthlyTemperature[].minTemp", DataValueType.Decimal, false),
                Leaf("平均气温", "ChartData.MonthlyTemperature[].avgTemp", DataValueType.Decimal, false),
            }),
        };

        List<DataFieldNode> departments = new(100);
        for (int departmentIndex = 1; departmentIndex <= 100; departmentIndex++)
        {
            string departmentPath = $"DepartmentStatistics.Department{departmentIndex:000}";
            List<DataFieldNode> metrics = new(30);
            for (int metricIndex = 1; metricIndex <= 30; metricIndex++)
            {
                metrics.Add(Leaf(
                    $"指标 {metricIndex:00}",
                    $"{departmentPath}.Metric{metricIndex:00}",
                    DataValueType.Decimal));
            }

            departments.Add(Branch(
                $"部门 {departmentIndex:000}",
                departmentPath,
                DataValueType.String,
                metrics));
        }

        DataFieldNode generatedRoot = Branch(
            "部门指标（大数据量演示）",
            "DepartmentStatistics",
            DataValueType.String,
            departments);
        return new ReadOnlyCollection<DataFieldNode>(
            new[] { studentStatistics, report, students,
                chartScienceScores, chartQuarterlySales, chartRegionGdp,
                chartProductRatings, chartTemperature, generatedRoot });
    }

    /// <summary>
    /// 创建普通分支节点。
    /// </summary>
    /// <param name="name">节点名称。</param>
    /// <param name="path">节点路径。</param>
    /// <param name="type">节点类型。</param>
    /// <param name="children">子节点。</param>
    /// <returns>返回分支节点。</returns>
    private static DataFieldNode Branch(
        string name,
        string path,
        DataValueType type,
        IEnumerable<DataFieldNode> children) =>
        new()
        {
            Name = name,
            Path = path,
            Type = type,
            IsCollection = false,
            IsLeaf = false,
            IsBindable = false,
            Children = new ReadOnlyCollection<DataFieldNode>(children.ToList()),
        };

    /// <summary>
    /// 创建叶子字段。
    /// </summary>
    /// <param name="name">字段名称。</param>
    /// <param name="path">字段路径。</param>
    /// <param name="type">字段类型。</param>
    /// <param name="isBindable">当前阶段是否允许绑定。</param>
    /// <returns>返回叶子字段节点。</returns>
    private static DataFieldNode Leaf(
        string name,
        string path,
        DataValueType type,
        bool isBindable = true) =>
        new()
        {
            Name = name,
            Path = path,
            Type = type,
            IsCollection = false,
            IsLeaf = true,
            IsBindable = isBindable,
            Children = Array.Empty<DataFieldNode>(),
        };

    /// <summary>
    /// 将字段树按深度优先顺序展开到列表。
    /// </summary>
    /// <param name="nodes">需要展开的节点。</param>
    /// <param name="target">接收节点的列表。</param>
    private static void Flatten(IEnumerable<DataFieldNode> nodes, ICollection<DataFieldNode> target)
    {
        foreach (DataFieldNode node in nodes)
        {
            target.Add(node);
            Flatten(node.Children, target);
        }
    }

    /// <summary>
    /// 深度复制字段节点，确保调用方无法修改内部集合。
    /// </summary>
    /// <param name="nodes">原始节点。</param>
    /// <returns>返回独立只读节点列表。</returns>
    private static IReadOnlyList<DataFieldNode> CloneNodes(IEnumerable<DataFieldNode> nodes) =>
        new ReadOnlyCollection<DataFieldNode>(
            nodes.Select(node => node with { Children = CloneNodes(node.Children) }).ToList());

    /// <summary>
    /// 创建用于搜索结果的无子节点副本。
    /// </summary>
    /// <param name="node">原始节点。</param>
    /// <returns>返回浅层搜索节点。</returns>
    private static DataFieldNode CloneWithoutChildren(DataFieldNode node) =>
        node with { Children = Array.Empty<DataFieldNode>() };
}

/// <summary>
/// 为内存 Schema 中的可用字段提供确定性的演示值。
/// </summary>
public sealed class InMemoryDataValueProvider : IDataValueProvider
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>
    /// 初始化基础演示值和程序化部门指标值。
    /// </summary>
    public InMemoryDataValueProvider()
    {
        Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            ["StudentStatistics.AverageScore"] = 92.3m,
            ["StudentStatistics.StudentCount"] = 1260L,
            ["StudentStatistics.PassRate"] = 96.8m,
            ["Report.Title"] = "本年度学生成绩统计报告",
            ["Report.GeneratedAt"] = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
            ["Report.IsFinal"] = true,
            // ── 科学成绩 ──
            ["ChartData.ScienceScores"] = MakeChartRows(
                new[] { "Category", "你县", "全省" },
                ("四年级", 552m, 506m),
                ("八年级", 518m, 493m)),

            // ── 季度销售 ──
            ["ChartData.QuarterlySales"] = MakeChartRows(
                new[] { "quarter", "onlineSales", "offlineSales", "totalSales" },
                ("第一季度", 1280m, 3420m, 4700m),
                ("第二季度", 1560m, 3890m, 5450m),
                ("第三季度", 1420m, 4010m, 5430m),
                ("第四季度", 1890m, 4520m, 6410m)),

            // ── 区域 GDP ──
            ["ChartData.RegionGDP"] = MakeChartRows(
                new[] { "region", "gdp", "growthRate" },
                ("华东地区", 48650m, 5.8m),
                ("华南地区", 34120m, 6.2m),
                ("华北地区", 29870m, 4.5m),
                ("西南地区", 18540m, 7.1m),
                ("西北地区", 9620m, 6.8m),
                ("东北地区", 13780m, 3.2m)),

            // ── 产品评分 ──
            ["ChartData.ProductRatings"] = MakeChartRows(
                new[] { "dimension", "satisfaction", "recommendation" },
                ("产品质量", 92.3m, 88.5m),
                ("售后服务", 85.7m, 82.1m),
                ("物流速度", 78.4m, 75.3m),
                ("价格合理", 71.2m, 68.9m),
                ("界面体验", 89.1m, 86.4m)),

            // ── 月度气温 ──
            ["ChartData.MonthlyTemperature"] = MakeChartRows(
                new[] { "month", "maxTemp", "minTemp", "avgTemp" },
                ("1月", 5.2m, -3.8m, 0.7m),
                ("2月", 8.1m, -1.2m, 3.4m),
                ("3月", 14.6m, 3.5m, 9.0m),
                ("4月", 21.3m, 9.8m, 15.5m),
                ("5月", 26.8m, 15.2m, 21.0m),
                ("6月", 30.5m, 20.1m, 25.3m),
                ("7月", 33.2m, 23.6m, 28.4m),
                ("8月", 32.1m, 22.8m, 27.5m),
                ("9月", 27.4m, 17.5m, 22.4m),
                ("10月", 21.8m, 11.2m, 16.5m),
                ("11月", 13.5m, 4.6m, 9.0m),
                ("12月", 7.8m, -1.5m, 3.1m)),
        };

        for (int departmentIndex = 1; departmentIndex <= 100; departmentIndex++)
        {
            for (int metricIndex = 1; metricIndex <= 30; metricIndex++)
            {
                string path =
                    $"DepartmentStatistics.Department{departmentIndex:000}.Metric{metricIndex:00}";
                values[path] = 70m + ((departmentIndex * 30 + metricIndex) % 300) / 10m;
            }
        }

        _values = new ReadOnlyDictionary<string, object?>(values);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MakeChartRows(
        string[] columns,
        params (string, decimal, decimal)[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, object?>>(rows.Length);
        foreach (var (c0, c1, c2) in rows)
        {
            list.Add(new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>
                {
                    [columns[0]] = c0,
                    [columns[1]] = c1,
                    [columns[2]] = c2,
                }));
        }
        return new ReadOnlyCollection<IReadOnlyDictionary<string, object?>>(list);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MakeChartRows(
        string[] columns,
        params (string, decimal, decimal, decimal)[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, object?>>(rows.Length);
        foreach (var (c0, c1, c2, c3) in rows)
        {
            list.Add(new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>
                {
                    [columns[0]] = c0,
                    [columns[1]] = c1,
                    [columns[2]] = c2,
                    [columns[3]] = c3,
                }));
        }
        return new ReadOnlyCollection<IReadOnlyDictionary<string, object?>>(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>> GetValuesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, object?> snapshot =
            new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(_values, StringComparer.Ordinal));
        return Task.FromResult(snapshot);
    }
}
