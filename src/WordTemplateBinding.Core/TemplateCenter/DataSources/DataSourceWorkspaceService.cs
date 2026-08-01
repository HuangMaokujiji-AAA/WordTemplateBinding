#pragma warning disable CS1591
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;
public sealed class DataSourceWorkspaceService
{
    private const int HigherEducationModelVersion = 2;

    private readonly IDataSourceRepository _sources;
    private readonly IDataConnectionRepository _connections;
    private readonly IProjectRepository _projects;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly IDatabaseSchemaIntrospector _introspector;
    private readonly IHigherEducationReportDataProvider _higherEducation;
    private readonly DataSourceOptions _options;
    private readonly ApplicationIdentityOptions _identity;

    public DataSourceWorkspaceService(
        IDataSourceRepository sources,
        IDataConnectionRepository connections,
        IProjectRepository projects,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        IDatabaseSchemaIntrospector introspector,
        IHigherEducationReportDataProvider higherEducation,
        DataSourceOptions options,
        ApplicationIdentityOptions identity)
    {
        _sources = sources;
        _connections = connections;
        _projects = projects;
        _snapshots = snapshots;
        _fields = fields;
        _introspector = introspector;
        _higherEducation = higherEducation;
        _options = options;
        _identity = identity;
    }

    public async Task<DataSourceRecord> GetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _sources.GetAsync(id, cancellationToken)
        ?? throw new WorkspaceException("data_source_not_found", $"找不到数据源：{id}。");

    public async Task<IReadOnlyList<DataSourceRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        return await _sources.ListAsync(projectId, cancellationToken);
    }

    public async Task<DataSourceRecord> CreateAsync(
        ulong projectId,
        ulong connectionId,
        string sourceCode,
        string sourceName,
        string sourceType,
        string schema,
        string objectType,
        string objectName,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        bool isDatabase = string.Equals(sourceType, "DATABASE", StringComparison.OrdinalIgnoreCase);
        bool isJson = string.Equals(sourceType, "JSON", StringComparison.OrdinalIgnoreCase);

        if (!isDatabase && !isJson)
        {
            throw new WorkspaceException(
                "unsupported_data_source_type",
                "本阶段只支持 DATABASE 和 JSON 数据源。");
        }

        if (isDatabase && connectionId == 0)
        {
            throw new WorkspaceException(
                "invalid_data_source",
                "DATABASE 数据源必须指定连接 ID。");
        }

        // Validate connection for DATABASE sources (skip for JSON)
        string? validatedObjectType = objectType;
        string? validatedObjectName = objectName;
        if (isDatabase)
        {
            DataConnectionRecord connection = await _connections.GetAsync(
                connectionId, cancellationToken)
                ?? throw new WorkspaceException(
                    "data_connection_not_found",
                    $"找不到数据连接：{connectionId}。");
            if (connection.ProjectId.HasValue && connection.ProjectId != projectId)
            {
                throw new WorkspaceException(
                    "cross_project_binding_forbidden",
                    "数据连接不属于当前项目。");
            }

            IReadOnlyList<DatabaseObjectInfo> objects =
                await _introspector.ListObjectsAsync(connection, schema, cancellationToken);
            DatabaseObjectInfo? selected = objects.FirstOrDefault(item =>
                string.Equals(item.ObjectName, objectName, StringComparison.Ordinal) &&
                string.Equals(item.ObjectType, objectType, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new WorkspaceException(
                    "data_source_not_found",
                    $"数据库对象 {schema}.{objectName} 不存在或类型不匹配。");
            }

            validatedObjectType = selected.ObjectType;
            validatedObjectName = objectName;
        }

        if (string.IsNullOrWhiteSpace(sourceCode) ||
            sourceCode.Length > 64 ||
            !sourceCode.All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-') ||
            string.IsNullOrWhiteSpace(sourceName) ||
            sourceName.Length > 255)
        {
            throw new WorkspaceException(
                "invalid_data_source",
                "数据源编码只能包含字母、数字、下划线和连字符，名称不能为空。");
        }

        return await _sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = projectId,
                ConnectionId = connectionId,
                SourceCode = sourceCode.Trim(),
                SourceName = sourceName.Trim(),
                SourceType = isDatabase ? "DATABASE" : "JSON",
                SourceStatus = "ACTIVE",
                SchemaName = schema,
                ObjectType = validatedObjectType ?? string.Empty,
                ObjectName = validatedObjectName ?? string.Empty,
                SchemaJson = null,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ParseActor(),
            cancellationToken);
    }

    public async Task<DataSnapshotRecord> RefreshAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        DataSourceRecord source = await GetAsync(dataSourceId, cancellationToken);

        // JSON sources are refreshed through the development data source initializer.
        // Return the latest ready snapshot if one exists.
        if (string.Equals(source.SourceType, "JSON", StringComparison.OrdinalIgnoreCase))
        {
            return await _snapshots.GetLatestReadyAsync(source.Id, cancellationToken)
                ?? throw new WorkspaceException(
                    "data_snapshot_not_ready",
                    $"JSON 数据源 {dataSourceId} 尚无 READY 快照。请先初始化测试数据。");
        }

        if (string.Equals(
                source.SourceType,
                "HIGHER_EDUCATION",
                StringComparison.OrdinalIgnoreCase))
        {
            return await RefreshHigherEducationAsync(source, cancellationToken);
        }

        DataConnectionRecord connection = await _connections.GetAsync(
            source.ConnectionId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_connection_not_found",
                $"找不到数据连接：{source.ConnectionId}。");
        DataSnapshotRecord snapshot = await _snapshots.StartAsync(
            source.Id,
            ParseActor(),
            cancellationToken);
        try
        {
            IReadOnlyList<DatabaseColumnInfo> columns =
                await _introspector.ListColumnsAsync(
                    connection,
                    source.SchemaName,
                    source.ObjectName,
                    cancellationToken);
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
                await _introspector.ReadSampleAsync(
                    connection,
                    source.SchemaName,
                    source.ObjectName,
                    columns,
                    _options.SampleRowLimit,
                    cancellationToken);
            List<IReadOnlyDictionary<string, object?>> boundedRows = rows.ToList();
            string contentJson;
            do
            {
                contentJson = JsonSerializer.Serialize(
                    new { captureMode = "SCHEMA_AND_SAMPLE", sampleRows = boundedRows },
                    JsonOptions);
                if (Encoding.UTF8.GetByteCount(contentJson) <= _options.SampleMaxBytes ||
                    boundedRows.Count == 0)
                {
                    break;
                }

                boundedRows.RemoveAt(boundedRows.Count - 1);
            }
            while (true);

            string schemaJson = JsonSerializer.Serialize(
                new
                {
                    source.SchemaName,
                    source.ObjectType,
                    source.ObjectName,
                    columns,
                },
                JsonOptions);
            List<DataFieldRecord> fields = BuildFields(
                snapshot.Id,
                columns,
                boundedRows);
            await _fields.ReplaceAsync(snapshot.Id, fields, cancellationToken);
            string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(contentJson)))
                .ToLowerInvariant();
            await _snapshots.CompleteAsync(
                snapshot.Id,
                contentJson,
                schemaJson,
                hash,
                checked((ulong)boundedRows.Count),
                cancellationToken);
            await _sources.UpdateSchemaAsync(source.Id, schemaJson, cancellationToken);
            return await _snapshots.GetAsync(snapshot.Id, cancellationToken)
                ?? throw new InvalidOperationException("完成快照后无法读取记录。");
        }
        catch (OperationCanceledException)
        {
            await _snapshots.FailAsync(
                snapshot.Id,
                "刷新已取消。",
                CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await _snapshots.FailAsync(
                snapshot.Id,
                SafeRefreshMessage(exception),
                CancellationToken.None);
            throw;
        }
    }

    public async Task<DataSnapshotRecord> GetLatestReadySnapshotAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken) =>
        await _snapshots.GetLatestReadyAsync(dataSourceId, cancellationToken)
        ?? throw new WorkspaceException(
            "data_snapshot_not_ready",
            $"数据源 {dataSourceId} 尚无 READY 快照。");

    public async Task<IReadOnlyList<DataFieldRecord>> ListFieldsAsync(
        ulong dataSourceId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        DataSnapshotRecord snapshot = await GetLatestReadySnapshotAsync(
            dataSourceId,
            cancellationToken);
        return await _fields.ListAsync(
            snapshot.Id,
            query,
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListHigherEducationYearsAsync(
        CancellationToken cancellationToken) =>
        _higherEducation.ListYearsAsync(cancellationToken);

    public Task<IReadOnlyList<HigherEducationSchool>> ListHigherEducationSchoolsAsync(
        string collectionYear,
        CancellationToken cancellationToken) =>
        _higherEducation.ListSchoolsAsync(collectionYear, cancellationToken);

    public Task<HigherEducationReportData> GetHigherEducationReportAsync(
        string collectionYear,
        string schoolCode,
        CancellationToken cancellationToken) =>
        _higherEducation.BuildReportAsync(collectionYear, schoolCode, cancellationToken);

    public async Task<HigherEducationDataSourceResult> CreateHigherEducationAsync(
        ulong projectId,
        string collectionYear,
        string schoolCode,
        string? sourceCode,
        string? sourceName,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        string normalizedCollectionYear = collectionYear?.Trim() ?? string.Empty;
        string normalizedSchoolCode = schoolCode?.Trim() ?? string.Empty;
        if (normalizedCollectionYear.Length == 0 || normalizedSchoolCode.Length == 0)
        {
            throw new WorkspaceException(
                "invalid_higher_education_query",
                "监测年度和学校代码不能为空。");
        }

        string resolvedCode = string.IsNullOrWhiteSpace(sourceCode)
            ? $"he_{normalizedCollectionYear}_{normalizedSchoolCode}"
            : sourceCode.Trim();

        DataSourceRecord? existing = await FindHigherEducationSourceAsync(
            projectId,
            normalizedCollectionYear,
            normalizedSchoolCode,
            resolvedCode,
            cancellationToken);
        if (existing is not null)
        {
            DataSnapshotRecord? readySnapshot = await _snapshots.GetLatestReadyAsync(
                existing.Id,
                cancellationToken);
            if (readySnapshot is not null && IsCurrentHigherEducationModel(existing.SchemaJson))
            {
                return new HigherEducationDataSourceResult(existing, readySnapshot);
            }

            HigherEducationReportData missingSnapshotReport =
                await _higherEducation.BuildReportAsync(
                    normalizedCollectionYear,
                    normalizedSchoolCode,
                    cancellationToken);
            DataSnapshotRecord recoveredSnapshot =
                await PersistHigherEducationSnapshotAsync(
                    existing,
                    missingSnapshotReport,
                    cancellationToken);
            return new HigherEducationDataSourceResult(existing, recoveredSnapshot);
        }

        HigherEducationReportData report = await _higherEducation.BuildReportAsync(
            normalizedCollectionYear,
            normalizedSchoolCode,
            cancellationToken);
        string resolvedName = string.IsNullOrWhiteSpace(sourceName)
            ? $"{report.SchoolName}{normalizedCollectionYear}年度监测数据"
            : sourceName.Trim();
        ValidateSourceIdentity(resolvedCode, resolvedName);
        DataSourceRecord source;
        try
        {
            source = await _sources.CreateAsync(
                new DataSourceRecord
                {
                    Id = 0,
                    ProjectId = projectId,
                    ConnectionId = 0,
                    SourceCode = resolvedCode,
                    SourceName = resolvedName,
                    SourceType = "HIGHER_EDUCATION",
                    SourceStatus = "ACTIVE",
                    SchemaName = report.CollectionYear,
                    ObjectType = "REPORT_MODEL",
                    ObjectName = report.SchoolCode,
                    SchemaJson = null,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                ParseActor(),
                cancellationToken);
        }
        catch (WorkspaceException exception)
            when (string.Equals(
                exception.ErrorCode,
                "data_source_code_conflict",
                StringComparison.Ordinal))
        {
            // Another request may have created the same logical source after our
            // initial lookup. Recover the winner so this operation stays idempotent.
            DataSourceRecord? concurrentSource = await FindHigherEducationSourceAsync(
                    projectId,
                    normalizedCollectionYear,
                    normalizedSchoolCode,
                    resolvedCode,
                    cancellationToken);
            if (concurrentSource is null)
            {
                throw;
            }

            source = concurrentSource;
            DataSnapshotRecord? concurrentSnapshot =
                await _snapshots.GetLatestReadyAsync(source.Id, cancellationToken);
            if (concurrentSnapshot is not null)
            {
                return new HigherEducationDataSourceResult(source, concurrentSnapshot);
            }
        }

        DataSnapshotRecord snapshot = await PersistHigherEducationSnapshotAsync(
            source,
            report,
            cancellationToken);
        return new HigherEducationDataSourceResult(source, snapshot);
    }

    private async Task<DataSourceRecord?> FindHigherEducationSourceAsync(
        ulong projectId,
        string collectionYear,
        string schoolCode,
        string sourceCode,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DataSourceRecord> sources = await _sources.ListAsync(
            projectId,
            cancellationToken);
        DataSourceRecord? reportSource = sources.FirstOrDefault(source =>
            IsHigherEducationSource(source, collectionYear, schoolCode));
        DataSourceRecord? codeOwner = sources.FirstOrDefault(source =>
            string.Equals(
                source.SourceCode,
                sourceCode,
                StringComparison.OrdinalIgnoreCase));

        if (codeOwner is not null &&
            (!IsHigherEducationSource(codeOwner, collectionYear, schoolCode) ||
             reportSource is not null && reportSource.Id != codeOwner.Id))
        {
            throw new WorkspaceException(
                "data_source_code_conflict",
                $"数据源编码 {sourceCode} 在当前项目中已被其他数据源使用。");
        }

        return reportSource ?? codeOwner;
    }

    private static bool IsHigherEducationSource(
        DataSourceRecord source,
        string collectionYear,
        string schoolCode) =>
        string.Equals(
            source.SourceType,
            "HIGHER_EDUCATION",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(source.SchemaName, collectionYear, StringComparison.Ordinal) &&
        string.Equals(source.ObjectName, schoolCode, StringComparison.Ordinal);

    private async Task<DataSnapshotRecord> RefreshHigherEducationAsync(
        DataSourceRecord source,
        CancellationToken cancellationToken)
    {
        HigherEducationReportData report = await _higherEducation.BuildReportAsync(
            source.SchemaName,
            source.ObjectName,
            cancellationToken);
        return await PersistHigherEducationSnapshotAsync(source, report, cancellationToken);
    }

    private async Task<DataSnapshotRecord> PersistHigherEducationSnapshotAsync(
        DataSourceRecord source,
        HigherEducationReportData report,
        CancellationToken cancellationToken)
    {
        DataSnapshotRecord snapshot = await _snapshots.StartAsync(
            source.Id,
            ParseActor(),
            cancellationToken);
        try
        {
            string contentJson = JsonSerializer.Serialize(report.Content, JsonOptions);
            using JsonDocument content = JsonDocument.Parse(contentJson);
            List<DataFieldRecord> fields = BuildStructuredFields(
                snapshot.Id,
                content.RootElement);
            string schemaJson = JsonSerializer.Serialize(
                new
                {
                    model = "higherEducationMonitoring",
                    modelVersion = HigherEducationModelVersion,
                    report.CollectionYear,
                    report.SchoolCode,
                    report.SchoolName,
                    fieldPaths = fields.Select(field => field.FieldPath),
                },
                JsonOptions);
            await _fields.ReplaceAsync(snapshot.Id, fields, cancellationToken);
            string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(contentJson)))
                .ToLowerInvariant();
            await _snapshots.CompleteAsync(
                snapshot.Id,
                contentJson,
                schemaJson,
                hash,
                report.RowCount,
                cancellationToken);
            await _sources.UpdateSchemaAsync(source.Id, schemaJson, cancellationToken);
            return await _snapshots.GetAsync(snapshot.Id, cancellationToken)
                ?? throw new InvalidOperationException("完成高校监测快照后无法读取记录。");
        }
        catch (OperationCanceledException)
        {
            await _snapshots.FailAsync(snapshot.Id, "刷新已取消。", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await _snapshots.FailAsync(
                snapshot.Id,
                SafeRefreshMessage(exception),
                CancellationToken.None);
            throw;
        }
    }

    private static bool IsCurrentHigherEducationModel(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(schemaJson);
            return document.RootElement.TryGetProperty("modelVersion", out JsonElement version) &&
                   version.TryGetInt32(out int value) &&
                   value >= HigherEducationModelVersion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<DataFieldRecord> BuildFields(
        ulong snapshotId,
        IReadOnlyList<DatabaseColumnInfo> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        List<DataFieldRecord> fields = new(columns.Count + 1)
        {
            new DataFieldRecord
            {
                Id = 0,
                SnapshotId = snapshotId,
                FieldPath = "rows",
                FieldName = "样例行集合",
                Comment = "当前数据源的行集合",
                DataType = DataValueType.Array,
                IsArray = true,
                IsNullable = false,
                IsBindable = true,
                SampleValueJson = JsonSerializer.Serialize(rows, JsonOptions),
                DisplayOrder = 0,
            },
        };
        foreach (DatabaseColumnInfo column in columns)
        {
            object? sample = rows
                .Select(row => row.TryGetValue(column.ColumnName, out object? value) ? value : null)
                .FirstOrDefault(value => value is not null);
            fields.Add(new DataFieldRecord
            {
                Id = 0,
                SnapshotId = snapshotId,
                FieldPath = $"row.{column.ColumnName}",
                FieldName = string.IsNullOrWhiteSpace(column.Comment)
                    ? column.ColumnName
                    : column.Comment,
                Comment = column.Comment,
                DataType = column.DataType,
                IsArray = false,
                IsNullable = column.IsNullable,
                IsBindable = column.IsBindable,
                SampleValueJson = JsonSerializer.Serialize(sample, JsonOptions),
                DisplayOrder = column.Ordinal,
            });
        }

        return fields;
    }

    private static List<DataFieldRecord> BuildStructuredFields(
        ulong snapshotId,
        JsonElement root)
    {
        List<DataFieldRecord> fields = new();
        int order = 0;
        WalkStructuredFields(snapshotId, root, string.Empty, fields, ref order);
        return fields;
    }

    private static void WalkStructuredFields(
        ulong snapshotId,
        JsonElement element,
        string path,
        ICollection<DataFieldRecord> fields,
        ref int order)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string childPath = string.IsNullOrEmpty(path)
                    ? property.Name
                    : $"{path}.{property.Name}";
                WalkStructuredFields(
                    snapshotId,
                    property.Value,
                    childPath,
                    fields,
                    ref order);
            }

            return;
        }

        DataValueType type = element.ValueKind switch
        {
            JsonValueKind.Array => DataValueType.Array,
            JsonValueKind.String => DataValueType.String,
            JsonValueKind.Number when element.TryGetInt64(out _) => DataValueType.Integer,
            JsonValueKind.Number => DataValueType.Decimal,
            JsonValueKind.True or JsonValueKind.False => DataValueType.Boolean,
            _ => DataValueType.String,
        };
        fields.Add(new DataFieldRecord
        {
            Id = 0,
            SnapshotId = snapshotId,
            FieldPath = path,
            FieldName = FriendlyFieldName(path),
            Comment = FriendlyFieldName(path),
            DataType = type,
            IsArray = type == DataValueType.Array,
            IsNullable = element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined,
            IsBindable = element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined),
            SampleValueJson = element.GetRawText(),
            DisplayOrder = order++,
        });
    }

    private static string FriendlyFieldName(string path)
    {
        string leaf = path.Split('.').LastOrDefault() ?? path;
        return FriendlyNames.GetValueOrDefault(leaf, leaf);
    }

    private static void ValidateSourceIdentity(string sourceCode, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceCode) ||
            sourceCode.Length > 64 ||
            !sourceCode.All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-') ||
            string.IsNullOrWhiteSpace(sourceName) ||
            sourceName.Length > 255)
        {
            throw new WorkspaceException(
                "invalid_data_source",
                "数据源编码只能包含字母、数字、下划线和连字符，名称不能为空。");
        }
    }

    private ulong? ParseActor() =>
        ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) && actor > 0
            ? actor
            : null;

    private static string SafeRefreshMessage(Exception exception) => exception switch
    {
        WordTemplateBindingException business => business.Message,
        _ => "数据源刷新失败，请检查连接和数据库对象。",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly IReadOnlyDictionary<string, string> FriendlyNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["collectionYear"] = "采集年度",
            ["schoolCode"] = "学校代码",
            ["schoolName"] = "学校名称",
            ["undergraduateMajors"] = "本科专业门类统计",
            ["teachingMetrics"] = "教学重点数据",
            ["featuredMajors"] = "优势特色专业",
            ["warningMajors"] = "黄牌预警专业",
            ["majorColleges"] = "学院专业明细",
            ["collegeGroups"] = "按学院分组的专业明细",
            ["majorMetrics"] = "专业指标雷达图数据（按专业）",
            ["level1RadarData"] = "一级指标雷达图",
            ["level2RadarData"] = "二级指标雷达图",
            ["level3RadarData"] = "三级指标雷达图",
            ["institutionType"] = "院校类型",
            ["institutionCategory"] = "院校类别",
            ["sponsorType"] = "举办者类型",
            ["supervisingAuthority"] = "主管部门",
            ["undergraduateEducationStartYear"] = "本科教育起始年份",
            ["totalMajorCount"] = "本科专业总数",
            ["newMajorCount"] = "新专业数",
            ["undergraduateStudentCount"] = "本科生人数",
            ["fullTimeStudentCount"] = "全日制学生人数",
            ["equivalentStudentCount"] = "折合在校生人数",
            ["staffCount"] = "教职工人数",
            ["fullTimeTeacherCount"] = "专任教师人数",
        };
}

public sealed class PersistentDataSchemaProvider : IContextualDataSchemaProvider
{
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;

    public PersistentDataSchemaProvider(
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields)
    {
        _snapshots = snapshots;
        _fields = fields;
    }

    public async Task<IReadOnlyList<DataFieldNode>> GetSchemaAsync(
        DataSchemaContext context,
        CancellationToken cancellationToken)
    {
        ulong snapshotId = await ResolveSnapshotIdAsync(context, cancellationToken);
        IReadOnlyList<DataFieldRecord> fields = await _fields.ListAsync(
            snapshotId,
            null,
            5000,
            cancellationToken);
        return BuildTree(fields);
    }

    public async Task<DataSchemaSearchResult> SearchAsync(
        DataSchemaContext context,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ulong snapshotId = await ResolveSnapshotIdAsync(context, cancellationToken);
        IReadOnlyList<DataFieldRecord> fields = await _fields.ListAsync(
            snapshotId,
            query,
            limit + 1,
            cancellationToken);
        bool truncated = fields.Count > limit;
        IReadOnlyList<DataFieldNode> nodes = fields
            .Take(limit)
            .Select(ToNode)
            .ToList()
            .AsReadOnly();
        return new DataSchemaSearchResult
        {
            Nodes = nodes,
            MatchCount = fields.Count,
            IsTruncated = truncated,
        };
    }

    public async Task<DataFieldDefinition?> FindByPathAsync(
        DataSchemaContext context,
        string path,
        CancellationToken cancellationToken)
    {
        ulong snapshotId = await ResolveSnapshotIdAsync(context, cancellationToken);
        DataFieldRecord? field = await _fields.FindAsync(
            snapshotId,
            path,
            cancellationToken);
        return field is null
            ? null
            : new DataFieldDefinition
            {
                Name = field.FieldName,
                Path = field.FieldPath,
                Type = field.DataType,
                IsBindable = field.IsBindable,
            };
    }

    private async Task<ulong> ResolveSnapshotIdAsync(
        DataSchemaContext context,
        CancellationToken cancellationToken)
    {
        if (context.SnapshotId.HasValue)
        {
            DataSnapshotRecord snapshot = await _snapshots.GetAsync(
                context.SnapshotId.Value,
                cancellationToken)
                ?? throw new WorkspaceException(
                    "data_snapshot_not_found",
                    $"找不到数据快照：{context.SnapshotId.Value}。");
            if (snapshot.DataSourceId != context.DataSourceId ||
                !string.Equals(snapshot.SnapshotStatus, "READY", StringComparison.Ordinal))
            {
                throw new WorkspaceException(
                    "data_snapshot_not_ready",
                    "指定数据快照不属于当前数据源或尚未就绪。");
            }

            return snapshot.Id;
        }

        return (await _snapshots.GetLatestReadyAsync(
                context.DataSourceId,
                cancellationToken))?.Id
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据源 {context.DataSourceId} 尚无 READY 快照。");
    }

    private static IReadOnlyList<DataFieldNode> BuildTree(
        IReadOnlyList<DataFieldRecord> fields)
    {
        if (!fields.Any(field =>
                field.FieldPath.StartsWith("row.", StringComparison.Ordinal)))
        {
            return BuildStructuredTree(fields);
        }

        DataFieldNode? rows = fields
            .Where(field => field.IsArray)
            .Select(ToNode)
            .FirstOrDefault();
        IReadOnlyList<DataFieldNode> columns = fields
            .Where(field => field.FieldPath.StartsWith("row.", StringComparison.Ordinal))
            .Select(ToNode)
            .ToList()
            .AsReadOnly();
        List<DataFieldNode> roots = new()
        {
            new DataFieldNode
            {
                Name = "当前行",
                Path = "row",
                Type = DataValueType.Object,
                IsCollection = false,
                IsLeaf = false,
                IsBindable = false,
                Children = columns,
            },
        };
        if (rows is not null)
        {
            roots.Add(rows);
        }

        return roots.AsReadOnly();
    }

    private static IReadOnlyList<DataFieldNode> BuildStructuredTree(
        IReadOnlyList<DataFieldRecord> fields)
    {
        List<MutableSchemaNode> roots = new();
        Dictionary<string, MutableSchemaNode> groups = new(StringComparer.Ordinal);
        foreach (DataFieldRecord field in fields)
        {
            string[] segments = field.FieldPath.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length <= 1)
            {
                roots.Add(MutableSchemaNode.Leaf(ToNode(field)));
                continue;
            }

            List<MutableSchemaNode> siblings = roots;
            string prefix = string.Empty;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                prefix = string.IsNullOrEmpty(prefix)
                    ? segments[index]
                    : $"{prefix}.{segments[index]}";
                if (!groups.TryGetValue(prefix, out MutableSchemaNode? group))
                {
                    group = MutableSchemaNode.Group(
                        GroupDisplayName(prefix, segments[index], fields),
                        prefix);
                    groups[prefix] = group;
                    siblings.Add(group);
                }

                siblings = group.Children;
            }

            siblings.Add(MutableSchemaNode.Leaf(ToNode(field)));
        }

        return roots.Select(node => node.ToImmutable()).ToList().AsReadOnly();
    }

    private static string GroupDisplayName(
        string path,
        string segment,
        IReadOnlyList<DataFieldRecord> fields)
    {
        if (string.Equals(path, "school", StringComparison.Ordinal))
        {
            return "学校概况";
        }

        if (string.Equals(path, "majorMetrics", StringComparison.Ordinal))
        {
            return "专业指标雷达图数据（按专业）";
        }

        if (path.StartsWith("majorMetrics.", StringComparison.Ordinal) &&
            path.Count(character => character == '.') == 1)
        {
            DataFieldRecord? majorName = fields.FirstOrDefault(field =>
                string.Equals(
                    field.FieldPath,
                    $"{path}.majorName",
                    StringComparison.Ordinal));
            if (majorName is not null &&
                TryReadJsonString(majorName.SampleValueJson, out string? name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return $"{name}（{segment}）";
            }
        }

        return segment;
    }

    private static bool TryReadJsonString(string? json, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = document.RootElement.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class MutableSchemaNode
    {
        private readonly DataFieldNode? _leaf;

        private MutableSchemaNode(string name, string path, DataFieldNode? leaf)
        {
            Name = name;
            Path = path;
            _leaf = leaf;
        }

        internal string Name { get; }

        internal string Path { get; }

        internal List<MutableSchemaNode> Children { get; } = new();

        internal static MutableSchemaNode Group(string name, string path) =>
            new(name, path, null);

        internal static MutableSchemaNode Leaf(DataFieldNode leaf) =>
            new(leaf.Name, leaf.Path, leaf);

        internal DataFieldNode ToImmutable()
        {
            if (_leaf is not null)
            {
                return _leaf;
            }

            return new DataFieldNode
            {
                Name = Name,
                Path = Path,
                Type = DataValueType.Object,
                IsCollection = false,
                IsLeaf = false,
                IsBindable = false,
                Children = Children.Select(child => child.ToImmutable()).ToList().AsReadOnly(),
            };
        }
    }

    private static DataFieldNode ToNode(DataFieldRecord field) => new()
    {
        Name = field.FieldName,
        Path = field.FieldPath,
        Type = field.DataType,
        IsCollection = field.IsArray,
        IsLeaf = true,
        IsBindable = field.IsBindable,
        Children = Array.Empty<DataFieldNode>(),
    };
}

#pragma warning restore CS1591
