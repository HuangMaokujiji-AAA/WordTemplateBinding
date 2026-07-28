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
    private readonly IDataSourceRepository _sources;
    private readonly IDataConnectionRepository _connections;
    private readonly IProjectRepository _projects;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly IDatabaseSchemaIntrospector _introspector;
    private readonly DataSourceOptions _options;
    private readonly ApplicationIdentityOptions _identity;

    public DataSourceWorkspaceService(
        IDataSourceRepository sources,
        IDataConnectionRepository connections,
        IProjectRepository projects,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        IDatabaseSchemaIntrospector introspector,
        DataSourceOptions options,
        ApplicationIdentityOptions identity)
    {
        _sources = sources;
        _connections = connections;
        _projects = projects;
        _snapshots = snapshots;
        _fields = fields;
        _introspector = introspector;
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

