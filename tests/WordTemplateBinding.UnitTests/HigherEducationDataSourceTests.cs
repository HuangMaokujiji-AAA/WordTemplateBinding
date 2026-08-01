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
        Assert.Contains(major.Children, node =>
            node.Path == "majorMetrics.070601.level1RadarData" &&
            node.Type == Core.Enums.DataValueType.Array &&
            node.IsBindable);
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
