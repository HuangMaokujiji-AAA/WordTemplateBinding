using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Core.Services;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.UnitTests;

/// <summary>验证高校监测数据源的创建和重复创建行为。</summary>
public sealed class HigherEducationDataSourceTests
{
    /// <summary>重复构造同一学校年度时复用数据源和已就绪快照。</summary>
    [Fact]
    public async Task CreateHigherEducationAsync_ReusesExistingReadySource()
    {
        TestContext context = await TestContext.CreateAsync();

        HigherEducationDataSourceResult first = await context.Service
            .CreateHigherEducationAsync(
                context.Project.Id,
                "2024",
                "10621",
                null,
                null,
                CancellationToken.None);
        HigherEducationDataSourceResult second = await context.Service
            .CreateHigherEducationAsync(
                context.Project.Id,
                "2024",
                "10621",
                null,
                null,
                CancellationToken.None);

        Assert.Equal(first.Source.Id, second.Source.Id);
        Assert.Equal(first.Snapshot.Id, second.Snapshot.Id);
        Assert.Equal(1, context.Provider.BuildReportCallCount);
        Assert.Single(await context.Sources.ListAsync(
            context.Project.Id,
            CancellationToken.None));
    }

    /// <summary>已有数据源但没有成功快照时，重复构造会补齐快照。</summary>
    [Fact]
    public async Task CreateHigherEducationAsync_RecoversMissingSnapshot()
    {
        TestContext context = await TestContext.CreateAsync();
        DataSourceRecord existing = await context.Sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = context.Project.Id,
                ConnectionId = 0,
                SourceCode = "he_2024_10621",
                SourceName = "成都信息工程大学2024年度监测数据",
                SourceType = "HIGHER_EDUCATION",
                SourceStatus = "ACTIVE",
                SchemaName = "2024",
                ObjectType = "REPORT_MODEL",
                ObjectName = "10621",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            actorUserId: 1,
            CancellationToken.None);

        HigherEducationDataSourceResult result = await context.Service
            .CreateHigherEducationAsync(
                context.Project.Id,
                "2024",
                "10621",
                null,
                null,
                CancellationToken.None);

        Assert.Equal(existing.Id, result.Source.Id);
        Assert.Equal("READY", result.Snapshot.SnapshotStatus);
        Assert.Equal(1, context.Provider.BuildReportCallCount);
        Assert.Single(await context.Sources.ListAsync(
            context.Project.Id,
            CancellationToken.None));
    }

    /// <summary>高校结构字段按专业折叠，专业节点显示代码和名称。</summary>
    [Fact]
    public async Task CreateHigherEducationAsync_ExposesMajorMetricHierarchy()
    {
        TestContext context = await TestContext.CreateAsync();
        HigherEducationDataSourceResult result = await context.Service
            .CreateHigherEducationAsync(
                context.Project.Id,
                "2024",
                "10621",
                null,
                null,
                CancellationToken.None);
        PersistentDataSchemaProvider schemaProvider = new(
            context.Snapshots,
            context.Fields);

        IReadOnlyList<DataFieldNode> roots = await schemaProvider.GetSchemaAsync(
            new DataSchemaContext(result.Source.Id),
            CancellationToken.None);

        DataFieldNode metrics = Assert.Single(roots.Where(node =>
            node.Path == "majorMetrics"));
        Assert.Equal("专业指标雷达图数据（按专业）", metrics.Name);
        DataFieldNode major = Assert.Single(metrics.Children);
        Assert.Equal("大气科学（070601）", major.Name);
        DataFieldNode radarData = Assert.Single(major.Children.Where(node =>
            node.Path == "majorMetrics.070601.level1RadarData"));
        Assert.Equal(Core.Enums.DataValueType.Array, radarData.Type);
        Assert.True(radarData.IsBindable);
        Assert.Equal("一级指标雷达图", radarData.Comment);
        Assert.False(radarData.IsNullable);
        Assert.Contains("category", radarData.SampleValueJson);
        Assert.Equal(
            new[] { "category", "平均值", "最大值", "最小值", "大气科学", "supplement" },
            radarData.Children.Select(node => node.Path.Split('.').Last()));
        Assert.All(radarData.Children, node =>
        {
            Assert.True(node.IsLeaf);
            Assert.True(node.IsBindable);
            Assert.False(string.IsNullOrWhiteSpace(node.SampleValueJson));
        });
    }

    /// <summary>关系数据库的 rows 集合会直接携带单行数据库列，搜索时也不丢失。</summary>
    [Fact]
    public async Task PersistentSchema_RowsArrayContainsDatabaseColumns()
    {
        TestContext context = await TestContext.CreateAsync();
        DataSourceRecord source = await context.Sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = context.Project.Id,
                ConnectionId = 1,
                SourceCode = "MAJOR_ROWS",
                SourceName = "专业明细",
                SourceType = "DATABASE",
                SourceStatus = "ACTIVE",
                SchemaName = "reporting",
                ObjectType = "TABLE",
                ObjectName = "major_rows",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            actorUserId: 1,
            CancellationToken.None);
        DataSnapshotRecord snapshot = await context.Snapshots.StartAsync(
            source.Id,
            actorUserId: 1,
            CancellationToken.None);
        await context.Fields.ReplaceAsync(
            snapshot.Id,
            new[]
            {
                new DataFieldRecord
                {
                    Id = 0,
                    SnapshotId = snapshot.Id,
                    FieldPath = "rows",
                    FieldName = "样例行集合",
                    Comment = "当前数据源的行集合",
                    DataType = Core.Enums.DataValueType.Array,
                    IsArray = true,
                    IsNullable = false,
                    IsBindable = true,
                    SampleValueJson = "[]",
                    DisplayOrder = 0,
                },
                new DataFieldRecord
                {
                    Id = 0,
                    SnapshotId = snapshot.Id,
                    FieldPath = "row.major_name",
                    FieldName = "专业名称",
                    Comment = "数据库专业名称列",
                    DataType = Core.Enums.DataValueType.String,
                    IsArray = false,
                    IsNullable = false,
                    IsBindable = true,
                    SampleValueJson = "\"人工智能\"",
                    DisplayOrder = 1,
                },
                new DataFieldRecord
                {
                    Id = 0,
                    SnapshotId = snapshot.Id,
                    FieldPath = "row.student_count",
                    FieldName = "学生人数",
                    Comment = "数据库学生人数列",
                    DataType = Core.Enums.DataValueType.Integer,
                    IsArray = false,
                    IsNullable = true,
                    IsBindable = true,
                    SampleValueJson = "120",
                    DisplayOrder = 2,
                },
            },
            CancellationToken.None);
        await context.Snapshots.CompleteAsync(
            snapshot.Id,
            "{}",
            "{}",
            new string('c', 64),
            1,
            CancellationToken.None);
        PersistentDataSchemaProvider provider = new(
            context.Snapshots,
            context.Fields);

        IReadOnlyList<DataFieldNode> roots = await provider.GetSchemaAsync(
            new DataSchemaContext(source.Id),
            CancellationToken.None);
        DataFieldNode rows = Assert.Single(roots.Where(node => node.Path == "rows"));
        Assert.Equal(
            new[] { "row.major_name", "row.student_count" },
            rows.Children.Select(node => node.Path));

        DataSchemaSearchResult search = await provider.SearchAsync(
            new DataSchemaContext(source.Id),
            "rows",
            20,
            CancellationToken.None);
        Assert.Equal(2, Assert.Single(search.Nodes).Children.Count);
    }

    private sealed class TestContext
    {
        private TestContext(
            ProjectRecord project,
            InMemoryDataSourceRepository sources,
            InMemoryDataSnapshotRepository snapshots,
            InMemoryDataFieldRepository fields,
            StubHigherEducationProvider provider,
            DataSourceWorkspaceService service)
        {
            Project = project;
            Sources = sources;
            Snapshots = snapshots;
            Fields = fields;
            Provider = provider;
            Service = service;
        }

        internal ProjectRecord Project { get; }
        internal InMemoryDataSourceRepository Sources { get; }
        internal InMemoryDataSnapshotRepository Snapshots { get; }
        internal InMemoryDataFieldRepository Fields { get; }
        internal StubHigherEducationProvider Provider { get; }
        internal DataSourceWorkspaceService Service { get; }

        internal static async Task<TestContext> CreateAsync()
        {
            InMemoryPersistenceState state = new();
            InMemoryProjectRepository projects = new(state);
            ProjectRecord project = await projects.CreateAsync(
                "HE_PROJECT",
                "高校监测项目",
                null,
                actorUserId: 1,
                CancellationToken.None);
            InMemoryDataSourceRepository sources = new(state);
            InMemoryDataSnapshotRepository snapshots = new(state);
            InMemoryDataFieldRepository fields = new(state);
            StubHigherEducationProvider provider = new();
            DataSourceWorkspaceService service = new(
                sources,
                new InMemoryDataConnectionRepository(state),
                projects,
                snapshots,
                fields,
                new StubDatabaseSchemaIntrospector(),
                provider,
                new DataSourceOptions(),
                new ApplicationIdentityOptions { DefaultActorUserId = "1" });
            return new TestContext(project, sources, snapshots, fields, provider, service);
        }
    }

    private sealed class StubHigherEducationProvider
        : IHigherEducationReportDataProvider
    {
        internal int BuildReportCallCount { get; private set; }

        public Task<IReadOnlyList<string>> ListYearsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(new[] { "2024" });

        public Task<IReadOnlyList<HigherEducationSchool>> ListSchoolsAsync(
            string collectionYear,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HigherEducationSchool>>(
                new[]
                {
                    new HigherEducationSchool(
                        collectionYear,
                        "10621",
                        "成都信息工程大学"),
                });

        public Task<HigherEducationReportData> BuildReportAsync(
            string collectionYear,
            string schoolCode,
            CancellationToken cancellationToken = default)
        {
            BuildReportCallCount++;
            return Task.FromResult(new HigherEducationReportData
            {
                CollectionYear = collectionYear,
                SchoolCode = schoolCode,
                SchoolName = "成都信息工程大学",
                Content = new Dictionary<string, object?>
                {
                    ["collectionYear"] = collectionYear,
                    ["schoolCode"] = schoolCode,
                    ["schoolName"] = "成都信息工程大学",
                    ["school"] = new Dictionary<string, object?>
                    {
                        ["fullTimeStudentCount"] = 24599,
                    },
                    ["majorMetrics"] = new Dictionary<string, object?>
                    {
                        ["070601"] = new Dictionary<string, object?>
                        {
                            ["majorName"] = "大气科学",
                            ["level1RadarData"] = new[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["category"] = "就业",
                                    ["平均值"] = 80m,
                                    ["最大值"] = 95m,
                                    ["最小值"] = 60m,
                                    ["大气科学"] = 88m,
                                },
                                new Dictionary<string, object?>
                                {
                                    ["category"] = "招生",
                                    ["平均值"] = 75m,
                                    ["supplement"] = "仅第二条记录包含的字段",
                                },
                            },
                        },
                    },
                },
                RowCount = 1,
            });
        }
    }

    private sealed class StubDatabaseSchemaIntrospector
        : IDatabaseSchemaIntrospector
    {
        public Task<IReadOnlyList<string>> ListSchemasAsync(
            DataConnectionRecord connection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(
            DataConnectionRecord connection,
            string? schema,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>(
                Array.Empty<DatabaseObjectInfo>());

        public Task<IReadOnlyList<DatabaseColumnInfo>> ListColumnsAsync(
            DataConnectionRecord connection,
            string schema,
            string objectName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DatabaseColumnInfo>>(
                Array.Empty<DatabaseColumnInfo>());

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadSampleAsync(
            DataConnectionRecord connection,
            string schema,
            string objectName,
            IReadOnlyList<DatabaseColumnInfo> columns,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                Array.Empty<IReadOnlyDictionary<string, object?>>());
    }
}
