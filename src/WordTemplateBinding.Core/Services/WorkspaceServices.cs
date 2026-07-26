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

public sealed class ProjectChapterService
{
    private readonly IProjectRepository _projects;
    private readonly IChapterRepository _chapters;
    private readonly ApplicationIdentityOptions _identity;

    public ProjectChapterService(
        IProjectRepository projects,
        IChapterRepository chapters,
        ApplicationIdentityOptions identity)
    {
        _projects = projects;
        _chapters = chapters;
        _identity = identity;
    }

    public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(
        CancellationToken cancellationToken) =>
        _projects.ListAsync(cancellationToken);

    public async Task<ProjectRecord> CreateProjectAsync(
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        ValidateCode(code, "项目编码");
        ValidateName(name, "项目名称");
        return await _projects.CreateAsync(
            code.Trim(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            RequireActor(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterRecord>> ListChaptersAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        return await _chapters.ListAsync(projectId, cancellationToken);
    }

    public async Task<ChapterRecord> CreateChapterAsync(
        ulong projectId,
        string code,
        string title,
        ulong? parentId,
        decimal sortKey,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        ValidateCode(code, "章节编码");
        ValidateName(title, "章节标题");
        return await _chapters.CreateAsync(
            projectId,
            code.Trim(),
            title.Trim(),
            parentId,
            sortKey,
            RequireActor(),
            cancellationToken);
    }

    private ulong RequireActor()
    {
        if (!ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) || actor == 0)
        {
            throw new WorkspaceException(
                "application_identity_not_configured",
                "服务端尚未配置 ApplicationIdentity:DefaultActorUserId。");
        }

        return actor;
    }

    private static void ValidateCode(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            !value.All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new WorkspaceException(
                "invalid_workspace_request",
                $"{label}只能包含字母、数字、下划线和连字符，且长度不能超过 64。");
        }
    }

    private static void ValidateName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
        {
            throw new WorkspaceException(
                "invalid_workspace_request",
                $"{label}不能为空且长度不能超过 255。");
        }
    }
}

public sealed record DataConnectionTestResult(
    string Status,
    string Provider,
    string Database,
    string? ServerVersion,
    string Message);

public sealed class DataConnectionService
{
    private readonly IDataConnectionRepository _connections;
    private readonly IProjectRepository _projects;
    private readonly IDatabaseSchemaIntrospector _introspector;
    private readonly IDataSourceConnectionFactory _connectionFactory;
    private readonly ApplicationIdentityOptions _identity;

    public DataConnectionService(
        IDataConnectionRepository connections,
        IProjectRepository projects,
        IDatabaseSchemaIntrospector introspector,
        IDataSourceConnectionFactory connectionFactory,
        ApplicationIdentityOptions identity)
    {
        _connections = connections;
        _projects = projects;
        _introspector = introspector;
        _connectionFactory = connectionFactory;
        _identity = identity;
    }

    public Task<IReadOnlyList<DataConnectionRecord>> ListAsync(
        ulong? projectId,
        CancellationToken cancellationToken) =>
        _connections.ListAsync(projectId, cancellationToken);

    public async Task<DataConnectionRecord> GetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _connections.GetAsync(id, cancellationToken)
        ?? throw new WorkspaceException(
            "data_connection_not_found",
            $"找不到数据连接：{id}。");

    public async Task<DataConnectionRecord> CreateAsync(
        ulong? projectId,
        string name,
        string type,
        DataConnectionConfig config,
        string credentialRef,
        CancellationToken cancellationToken)
    {
        if (projectId.HasValue &&
            await _projects.GetAsync(projectId.Value, cancellationToken) is null)
        {
            throw new WorkspaceException(
                "project_not_found",
                $"找不到项目：{projectId.Value}。");
        }

        if (!string.Equals(type, "MYSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "unsupported_data_connection_type",
                "本阶段只支持 MYSQL 数据连接。");
        }

        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 255 ||
            string.IsNullOrWhiteSpace(config.Host) ||
            string.IsNullOrWhiteSpace(config.Database) ||
            string.IsNullOrWhiteSpace(credentialRef) ||
            credentialRef.Length > 255 ||
            !credentialRef.StartsWith(
                "config:DataSourceCredentials:",
                StringComparison.OrdinalIgnoreCase) ||
            config.Port == 0)
        {
            throw new WorkspaceException(
                "invalid_data_connection",
                "数据连接名称、主机、端口、数据库和凭据引用均不能为空。");
        }

        return await _connections.CreateAsync(
            new DataConnectionRecord
            {
                Id = 0,
                ProjectId = projectId,
                ConnectionName = name.Trim(),
                ConnectionType = "MYSQL",
                Config = config,
                CredentialRef = credentialRef.Trim(),
                ConnectionStatus = "ACTIVE",
                LastTestedAt = null,
                LastTestResultJson = null,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ParseActor(),
            cancellationToken);
    }

    public async Task<DataConnectionTestResult> TestAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        DataConnectionRecord connection = await GetAsync(id, cancellationToken);
        try
        {
            await using System.Data.Common.DbConnection opened =
                await _connectionFactory.OpenAsync(connection, cancellationToken);
            string version = opened.ServerVersion;
            DataConnectionTestResult result = new(
                "healthy",
                "MySQL",
                connection.Config.Database,
                version,
                "数据连接成功。");
            await _connections.UpdateTestResultAsync(
                id,
                "ACTIVE",
                JsonSerializer.Serialize(result, JsonOptions),
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            DataConnectionTestResult result = new(
                "unavailable",
                "MySQL",
                connection.Config.Database,
                null,
                "无法连接业务数据库，请检查服务端凭据、网络和 TLS 设置。");
            await _connections.UpdateTestResultAsync(
                id,
                "ERROR",
                JsonSerializer.Serialize(result, JsonOptions),
                CancellationToken.None);
            throw new WorkspaceException(
                "data_connection_unavailable",
                result.Message,
                exception);
        }
    }

    public async Task<IReadOnlyList<string>> ListSchemasAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _introspector.ListSchemasAsync(
            await GetAsync(id, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(
        ulong id,
        string? schema,
        CancellationToken cancellationToken) =>
        await _introspector.ListObjectsAsync(
            await GetAsync(id, cancellationToken),
            schema,
            cancellationToken);

    public async Task<IReadOnlyList<DatabaseColumnInfo>> ListColumnsAsync(
        ulong id,
        string schema,
        string objectName,
        CancellationToken cancellationToken) =>
        await _introspector.ListColumnsAsync(
            await GetAsync(id, cancellationToken),
            schema,
            objectName,
            cancellationToken);

    private ulong? ParseActor() =>
        ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) && actor > 0
            ? actor
            : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

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

        DataConnectionRecord connection = await _connections.GetAsync(
            connectionId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_connection_not_found",
                $"找不到数据连接：{connectionId}。");
        if (connection.ProjectId.HasValue && connection.ProjectId != projectId)
        {
            throw new WorkspaceException(
                "cross_project_binding_forbidden",
                "数据连接不属于当前项目。");
        }

        if (!string.Equals(sourceType, "DATABASE", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "unsupported_data_source_type",
                "本阶段只支持 DATABASE 数据源。");
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

        return await _sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = projectId,
                ConnectionId = connectionId,
                SourceCode = sourceCode.Trim(),
                SourceName = sourceName.Trim(),
                SourceType = "DATABASE",
                SourceStatus = "ACTIVE",
                SchemaName = schema,
                ObjectType = selected.ObjectType,
                ObjectName = objectName,
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

public sealed class BindingWorkspaceService
{
    private static readonly Regex TargetPropertyPattern = new(
        @"^(?:\$|categories|series\[\d+\]\.values)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private readonly IBindingSetRepository _sets;
    private readonly IBindingItemRepository _items;
    private readonly ITemplateVersionRepository _versions;
    private readonly ITemplateElementRepository _elements;
    private readonly IChapterRepository _chapters;
    private readonly IDataSourceRepository _sources;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly BindingSuggestionOptions _suggestions;
    private readonly ApplicationIdentityOptions _identity;

    public BindingWorkspaceService(
        IBindingSetRepository sets,
        IBindingItemRepository items,
        ITemplateVersionRepository versions,
        ITemplateElementRepository elements,
        IChapterRepository chapters,
        IDataSourceRepository sources,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        BindingSuggestionOptions suggestions,
        ApplicationIdentityOptions identity)
    {
        _sets = sets;
        _items = items;
        _versions = versions;
        _elements = elements;
        _chapters = chapters;
        _sources = sources;
        _snapshots = snapshots;
        _fields = fields;
        _suggestions = suggestions;
        _identity = identity;
    }

    public async Task<BindingSetRecord> GetOrCreateDraftAsync(
        ulong chapterId,
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        TemplateVersionRecord version = await _versions.GetAsync(
            templateVersionId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{templateVersionId}。");
        if (!version.VersionStatus.StartsWith("READY", StringComparison.Ordinal))
        {
            throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {templateVersionId} 尚未就绪。");
        }

        if (await _chapters.GetAsync(chapterId, cancellationToken) is null)
        {
            throw new WorkspaceException("chapter_not_found", $"找不到章节：{chapterId}。");
        }

        return await _sets.GetOrCreateDraftAsync(
            chapterId,
            templateVersionId,
            ParseActor(),
            cancellationToken);
    }

    public async Task<BindingItemRecord> UpsertAsync(
        ulong bindingSetId,
        ulong templateElementId,
        BindingItemUpsert request,
        CancellationToken cancellationToken)
    {
        BindingContext context = await ValidateContextAsync(
            bindingSetId,
            templateElementId,
            request.DataSourceId,
            request.SourcePath,
            cancellationToken);
        if (!TargetPropertyPattern.IsMatch(request.TargetProperty))
        {
                throw new BindingValidationException("TargetProperty 不在允许的属性白名单中。");
        }

        if (!string.Equals(
                request.SourceKind,
                "DATA_SOURCE",
                StringComparison.Ordinal) ||
            request.SourcePath.Length > 1024)
        {
            throw new BindingValidationException(
                "本阶段只允许 DATA_SOURCE 来源，且字段路径不能超过 1024 字符。");
        }

        ValidateOptionalJson(request.TransformConfigJson, "TransformConfigJson");
        ValidateOptionalJson(request.FormatConfigJson, "FormatConfigJson");
        ValidateOptionalJson(request.FallbackValueJson, "FallbackValueJson");
        ValidateCompatibility(context.Element, context.Field);
        BindingItemRecord saved = await _items.UpsertAsync(
            bindingSetId,
            templateElementId,
            request,
            cancellationToken);
        await _sets.ResetValidationAsync(bindingSetId, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<BindingItemRecord>> ListAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        await RequireSetAsync(bindingSetId, cancellationToken);
        return await _items.ListAsync(bindingSetId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        EnsureDraft(set);
        bool deleted = await _items.DeleteAsync(
            bindingSetId,
            templateElementId,
            cancellationToken);
        await _sets.ResetValidationAsync(bindingSetId, cancellationToken);
        return deleted;
    }

    public async Task<BindingValidationResult> ValidateAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        IReadOnlyList<TemplateElementRecord> elements = await _elements.ListAsync(
            set.TemplateVersionId,
            cancellationToken);
        IReadOnlyList<BindingItemRecord> items = await _items.ListAsync(
            bindingSetId,
            cancellationToken);
        List<BindingValidationItem> issues = new();
        Dictionary<ulong, TemplateElementRecord> elementIndex =
            elements.ToDictionary(item => item.Id);
        ChapterRecord? chapter = await _chapters.GetAsync(
            set.ChapterId,
            cancellationToken);
        foreach (TemplateElementRecord required in elements.Where(item => item.IsRequired))
        {
            if (!items.Any(item => item.TemplateElementId == required.Id))
            {
                issues.Add(new BindingValidationItem(
                    "REQUIRED_ELEMENT_UNBOUND",
                    "ERROR",
                    required.Id,
                    $"必填标记“{required.DisplayName ?? required.ElementKey}”尚未绑定。"));
            }
        }

        int invalidCount = 0;
        foreach (BindingItemRecord item in items)
        {
            if (!elementIndex.TryGetValue(
                    item.TemplateElementId,
                    out TemplateElementRecord? element))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "TEMPLATE_ELEMENT_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定引用的模板元素已不存在。"));
                continue;
            }

            if (!string.Equals(element.ParseStatus, "VALID", StringComparison.OrdinalIgnoreCase))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "TEMPLATE_ELEMENT_UNAVAILABLE",
                    "ERROR",
                    item.TemplateElementId,
                    $"模板元素当前不可绑定：{element.ParseMessage ?? element.ParseStatus}"));
                continue;
            }

            if (!item.DataSourceId.HasValue || string.IsNullOrWhiteSpace(item.SourcePath))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "BINDING_SOURCE_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定缺少数据源或字段路径。"));
                continue;
            }

            DataSourceRecord? source = await _sources.GetAsync(
                item.DataSourceId.Value,
                cancellationToken);
            if (chapter is null ||
                source is null ||
                source.ProjectId != chapter.ProjectId)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "BINDING_SOURCE_PROJECT_MISMATCH",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定数据源不存在或不属于当前章节项目。"));
                continue;
            }

            DataSnapshotRecord? snapshot = await _snapshots.GetLatestReadyAsync(
                item.DataSourceId.Value,
                cancellationToken);
            DataFieldRecord? field = snapshot is null
                ? null
                : await _fields.FindAsync(
                    snapshot.Id,
                    item.SourcePath,
                    cancellationToken);
            if (field is null)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    $"字段 {item.SourcePath} 已失效。"));
                continue;
            }

            if (!field.IsBindable)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_NOT_BINDABLE",
                    "ERROR",
                    item.TemplateElementId,
                    $"字段 {item.SourcePath} 当前不可绑定。"));
                continue;
            }

            try
            {
                ValidateCompatibility(element, field);
            }
            catch (BindingValidationException exception)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_TYPE_CHANGED",
                    "ERROR",
                    item.TemplateElementId,
                    exception.Message));
            }
        }

        int requiredUnbound = issues.Count(item =>
            item.Code == "REQUIRED_ELEMENT_UNBOUND");
        string status = invalidCount > 0 || requiredUnbound > 0
            ? "ERROR"
            : issues.Count > 0
                ? "WARNING"
                : "VALID";
        BindingValidationResult result = new()
        {
            Status = status,
            Summary = new BindingValidationSummary(
                elements.Count,
                items.Count,
                requiredUnbound,
                invalidCount,
                issues.Count(item => item.Level == "WARNING")),
            Items = issues.AsReadOnly(),
        };
        await _sets.UpdateValidationAsync(
            bindingSetId,
            status,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<BindingSuggestion>> SuggestAsync(
        ulong elementId,
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        TemplateElementRecord element = await _elements.GetAsync(elementId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{elementId}。");
        EnsureElementBindable(element);
        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            dataSourceId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据源 {dataSourceId} 尚无 READY 快照。");
        IReadOnlyList<DataFieldRecord> fields = await _fields.ListAsync(
            snapshot.Id,
            null,
            5000,
            cancellationToken);
        string displayName = element.DisplayName ?? element.ElementKey;
        string normalizedDisplay = NormalizeName(displayName);
        HashSet<string> aliases = _suggestions.Aliases.TryGetValue(
                displayName,
                out string[]? configured)
            ? configured.Select(NormalizeName).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return fields
            .Where(field => field.IsBindable)
            .Select(field => ScoreSuggestion(
                element,
                field,
                displayName,
                normalizedDisplay,
                aliases))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.FieldPath, StringComparer.Ordinal)
            .Take(20)
            .ToList()
            .AsReadOnly();
    }

    public async Task<BindingPreview> PreviewAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        BindingItemRecord item = await _items.GetAsync(
            bindingSetId,
            templateElementId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "binding_item_not_found",
                "找不到指定模板元素的绑定。");
        if (!item.DataSourceId.HasValue || string.IsNullOrWhiteSpace(item.SourcePath))
        {
            throw new WorkspaceException(
                "binding_item_not_found",
                "绑定缺少数据源字段。");
        }

        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            item.DataSourceId.Value,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                "绑定数据源尚无 READY 快照。");
        DataFieldRecord field = await _fields.FindAsync(
            snapshot.Id,
            item.SourcePath,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_field_not_found",
                $"字段 {item.SourcePath} 已失效。");
        TemplateElementRecord element = await _elements.GetAsync(
            templateElementId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{templateElementId}。");
        EnsureElementBindable(element);
        return new BindingPreview
        {
            TemplateElementId = element.Id,
            DisplayName = element.DisplayName ?? element.ElementKey,
            SourcePath = field.FieldPath,
            RawValueJson = field.SampleValueJson,
            FormattedValue = FormatSample(field.SampleValueJson),
            DataType = field.DataType,
            SnapshotId = snapshot.Id,
        };
    }

    private async Task<BindingContext> ValidateContextAsync(
        ulong bindingSetId,
        ulong templateElementId,
        ulong dataSourceId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        EnsureDraft(set);
        TemplateElementRecord element = await _elements.GetAsync(
            templateElementId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{templateElementId}。");
        if (element.TemplateVersionId != set.TemplateVersionId)
        {
            throw new BindingValidationException("模板元素不属于绑定配置固定的模板版本。");
        }

        EnsureElementBindable(element);
        ChapterRecord chapter = await _chapters.GetAsync(set.ChapterId, cancellationToken)
            ?? throw new WorkspaceException(
                "chapter_not_found",
                $"找不到章节：{set.ChapterId}。");
        DataSourceRecord source = await _sources.GetAsync(dataSourceId, cancellationToken)
            ?? throw new WorkspaceException(
                "data_source_not_found",
                $"找不到数据源：{dataSourceId}。");
        if (chapter.ProjectId != source.ProjectId)
        {
            throw new WorkspaceException(
                "cross_project_binding_forbidden",
                "不允许绑定其他项目的数据源。");
        }

        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            dataSourceId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据源 {dataSourceId} 尚无 READY 快照。");
        DataFieldRecord field = await _fields.FindAsync(
            snapshot.Id,
            sourcePath,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_field_not_found",
                $"找不到字段：{sourcePath}。");
        if (!field.IsBindable)
        {
            throw new BindingValidationException($"字段 {sourcePath} 当前不可绑定。");
        }

        return new BindingContext(set, element, source, snapshot, field);
    }

    private async Task<BindingSetRecord> RequireSetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _sets.GetAsync(id, cancellationToken)
        ?? throw new WorkspaceException(
            "binding_set_not_found",
            $"找不到绑定配置：{id}。");

    private static void EnsureDraft(BindingSetRecord set)
    {
        if (!string.Equals(set.BindingStatus, "DRAFT", StringComparison.Ordinal))
        {
            throw new WorkspaceException(
                "binding_set_read_only",
                $"绑定配置 {set.Id} 已发布，不能修改。");
        }
    }

    private static void EnsureElementBindable(TemplateElementRecord element)
    {
        if (!string.Equals(element.ParseStatus, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            throw new BindingValidationException(
                $"模板元素当前不可绑定：{element.ParseMessage ?? element.ParseStatus}");
        }
    }

    private static void ValidateCompatibility(
        TemplateElementRecord element,
        DataFieldRecord field)
    {
        if (string.Equals(element.ElementType, "TEXT", StringComparison.Ordinal))
        {
            if (field.DataType is DataValueType.Array or DataValueType.Object or
                DataValueType.Binary)
            {
                throw new BindingValidationException(
                    $"文字元素不能绑定 {field.DataType} 字段 {field.FieldPath}。");
            }

            using JsonDocument locator = JsonDocument.Parse(element.LocatorJson);
            if (locator.RootElement.TryGetProperty("dataType", out JsonElement typeElement) &&
                TryReadMockDataType(typeElement, out MockDataType mockType) &&
                mockType is MockDataType.Decimal or MockDataType.Integer &&
                field.DataType is not (DataValueType.Integer or DataValueType.Decimal))
            {
                throw new BindingValidationException(
                    $"数字模板元素不能绑定 {field.DataType} 字段 {field.FieldPath}。");
            }

            return;
        }

        if (string.Equals(element.ElementType, "CHART", StringComparison.Ordinal) &&
            field.DataType != DataValueType.Array)
        {
            throw new BindingValidationException("图表元素必须绑定 Array 字段。");
        }
    }

    private static bool TryReadMockDataType(
        JsonElement element,
        out MockDataType value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Enum.TryParse(element.GetString(), true, out value);
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int numeric) &&
            Enum.IsDefined(typeof(MockDataType), numeric))
        {
            value = (MockDataType)numeric;
            return true;
        }

        value = default;
        return false;
    }

    private static void ValidateOptionalJson(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
            if (document.RootElement.ValueKind == JsonValueKind.Undefined)
            {
                throw new JsonException();
            }
        }
        catch (JsonException exception)
        {
            throw new BindingValidationException($"{name} 必须是有效 JSON：{exception.Message}");
        }
    }

    private static BindingSuggestion ScoreSuggestion(
        TemplateElementRecord element,
        DataFieldRecord field,
        string displayName,
        string normalizedDisplay,
        IReadOnlySet<string> aliases)
    {
        List<string> reasons = new();
        int score = 0;
        string leaf = field.FieldPath.Split('.').Last();
        string normalizedFieldName = NormalizeName(field.FieldName);
        string normalizedLeaf = NormalizeName(leaf);
        if (string.Equals(displayName, field.FieldName, StringComparison.OrdinalIgnoreCase))
        {
            score += 75;
            reasons.Add("字段注释完全匹配");
        }
        else if (normalizedDisplay == normalizedFieldName)
        {
            score += 60;
            reasons.Add("名称归一化后匹配");
        }

        if (normalizedDisplay == normalizedLeaf)
        {
            score += 55;
            reasons.Add("字段末级名称匹配");
        }

        if (aliases.Contains(normalizedLeaf) || aliases.Contains(normalizedFieldName))
        {
            score += 65;
            reasons.Add("命中配置同义词");
        }

        bool typeCompatible = string.Equals(element.ElementType, "CHART", StringComparison.Ordinal)
            ? field.DataType == DataValueType.Array
            : field.DataType is not (
                DataValueType.Array or DataValueType.Object or DataValueType.Binary);
        if (typeCompatible)
        {
            score += 20;
            reasons.Add("数据类型兼容");
        }
        else
        {
            score = 0;
            reasons.Clear();
        }

        return new BindingSuggestion(
            field.FieldPath,
            Math.Min(score, 100),
            reasons.AsReadOnly());
    }

    private static string NormalizeName(string value) =>
        new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string? FormatSample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.ToString();
    }

    private ulong? ParseActor() =>
        ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) && actor > 0
            ? actor
            : null;

    private sealed record BindingContext(
        BindingSetRecord Set,
        TemplateElementRecord Element,
        DataSourceRecord Source,
        DataSnapshotRecord Snapshot,
        DataFieldRecord Field);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

#pragma warning restore CS1591
