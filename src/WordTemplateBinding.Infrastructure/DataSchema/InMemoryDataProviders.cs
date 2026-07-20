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
                .Where(node => node.IsLeaf)
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
            new[] { studentStatistics, report, students, generatedRoot });
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
