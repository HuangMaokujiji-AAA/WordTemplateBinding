#pragma warning disable CS1591
using System.Text.Json;
using MySqlConnector;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Database;

public sealed class MySqlProjectRepository : IProjectRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlProjectRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<ProjectRecord> CreateAsync(
        string code,
        string name,
        string? description,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_project
                (project_code, project_name, description, project_status, created_by)
            VALUES (@code, @name, @description, 'DRAFT', @actor);
            SELECT LAST_INSERT_ID();
            """;
        try
        {
            await using MySqlConnection connection =
                await _connections.OpenConnectionAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.AddParameter("@code", code);
            command.AddParameter("@name", name);
            command.AddParameter("@description", description);
            command.AddParameter("@actor", actorUserId);
            ulong id = Convert.ToUInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            return await GetAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("新建项目后无法读取记录。");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new WorkspaceException(
                "project_code_conflict",
                $"项目编码 {code} 已存在。",
                exception);
        }
    }

    public async Task<ProjectRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_code, project_name, description, project_status,
                   created_at, updated_at, row_version
            FROM rp_project
            WHERE id = @id AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<ProjectRecord?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_code, project_name, description, project_status,
                   created_at, updated_at, row_version
            FROM rp_project
            WHERE project_code = @code AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@code", code);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ProjectRecord>> ListAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_code, project_name, description, project_status,
                   created_at, updated_at, row_version
            FROM rp_project
            WHERE deleted_at IS NULL
            ORDER BY updated_at DESC, id DESC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        List<ProjectRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task<PagedResult<ProjectRecord>> ListAsync(
        string? query,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string where = """
            deleted_at IS NULL
              AND (@query IS NULL OR project_code LIKE CONCAT('%', @query, '%')
                   OR project_name LIKE CONCAT('%', @query, '%'))
              AND (@status IS NULL OR project_status = @status)
            """;
        string countSql = $"SELECT COUNT(*) FROM rp_project WHERE {where};";
        string listSql = $"""
            SELECT id, project_code, project_name, description, project_status,
                   created_at, updated_at, row_version
            FROM rp_project
            WHERE {where}
            ORDER BY updated_at DESC, id DESC
            LIMIT @limit OFFSET @offset;
            """;

        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        await using MySqlCommand count = new(countSql, connection);
        count.AddParameter("@query", NullIfBlank(query));
        count.AddParameter("@status", NullIfBlank(status));
        long total = Convert.ToInt64(
            await count.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        await using MySqlCommand list = new(listSql, connection);
        list.AddParameter("@query", NullIfBlank(query));
        list.AddParameter("@status", NullIfBlank(status));
        list.AddParameter("@limit", pageSize);
        list.AddParameter("@offset", checked((page - 1) * pageSize));
        List<ProjectRecord> records = new();
        await using MySqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return new PagedResult<ProjectRecord>(
            records.AsReadOnly(),
            total,
            page,
            pageSize);
    }

    public async Task<bool> UpdateAsync(
        ulong projectId,
        string name,
        string? description,
        string? status,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_project
            SET project_name = @name,
                description = @description,
                project_status = @status,
                updated_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", projectId);
        command.AddParameter("@name", name);
        command.AddParameter("@description", description);
        command.AddParameter("@status", status);
        command.AddParameter("@expectedRowVersion", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> ArchiveAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_project
            SET deleted_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", projectId);
        command.AddParameter("@expectedRowVersion", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> RestoreAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_project
            SET deleted_at = NULL,
                row_version = row_version + 1
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NOT NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", projectId);
        command.AddParameter("@expectedRowVersion", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProjectRecord Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        ProjectCode = reader.GetString("project_code"),
        ProjectName = reader.GetString("project_name"),
        Description = reader.GetNullableString("description"),
        ProjectStatus = reader.GetString("project_status"),
        CreatedAt = reader.GetDateTimeOffset("created_at"),
        UpdatedAt = reader.GetDateTimeOffset("updated_at"),
        RowVersion = reader.GetUInt32("row_version"),
    };
}

public sealed class MySqlChapterRepository : IChapterRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlChapterRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<ChapterRecord> CreateAsync(
        ulong projectId,
        string code,
        string title,
        ulong? parentId,
        decimal sortKey,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        ushort level = 1;
        if (parentId.HasValue)
        {
            ChapterRecord parent = await GetAsync(parentId.Value, cancellationToken)
                ?? throw new WorkspaceException(
                    "chapter_not_found",
                    $"找不到父章节：{parentId.Value}。");
            if (parent.ProjectId != projectId)
            {
                throw new WorkspaceException(
                    "chapter_not_found",
                    "父章节不属于当前项目。");
            }

            level = checked((ushort)(parent.LevelNo + 1));
        }

        const string sql = """
            INSERT INTO rp_chapter
                (project_id, parent_id, chapter_code, current_title, level_no,
                 sort_key, workflow_status, created_by)
            VALUES
                (@projectId, @parentId, @code, @title, @level, @sort,
                 'PENDING', @actor);
            SELECT LAST_INSERT_ID();
            """;
        try
        {
            await using MySqlConnection connection =
                await _connections.OpenConnectionAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.AddParameter("@projectId", projectId);
            command.AddParameter("@parentId", parentId);
            command.AddParameter("@code", code);
            command.AddParameter("@title", title);
            command.AddParameter("@level", level);
            command.AddParameter("@sort", sortKey);
            command.AddParameter("@actor", actorUserId);
            ulong id = Convert.ToUInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            return await GetAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("新建章节后无法读取记录。");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new WorkspaceException(
                "chapter_code_conflict",
                $"章节编码 {code} 在当前项目中已存在。",
                exception);
        }
    }

    public async Task<ChapterRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, parent_id, chapter_code, current_title, level_no,
                   sort_key, workflow_status, is_enabled, created_at,
                   updated_at, row_version
            FROM rp_chapter
            WHERE id = @id AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ChapterRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, parent_id, chapter_code, current_title, level_no,
                   sort_key, workflow_status, is_enabled, created_at,
                   updated_at, row_version
            FROM rp_chapter
            WHERE project_id = @projectId AND deleted_at IS NULL
            ORDER BY parent_id, sort_key, id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@projectId", projectId);
        List<ChapterRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task<bool> UpdateAsync(
        ulong chapterId,
        string code,
        string title,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_chapter
            SET chapter_code = @code, current_title = @title,
                updated_at = UTC_TIMESTAMP(3), row_version = row_version + 1
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", chapterId);
        command.AddParameter("@code", code);
        command.AddParameter("@title", title);
        command.AddParameter("@expectedRowVersion", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteAsync(
        ulong chapterId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_chapter
            SET deleted_at = UTC_TIMESTAMP(3), row_version = row_version + 1
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", chapterId);
        command.AddParameter("@expectedRowVersion", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ReorderAsync(
        ulong projectId,
        IReadOnlyList<(ulong ChapterId, ulong? ParentId, decimal SortKey)> items,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE rp_chapter
            SET parent_id = @parentId, sort_key = @sortKey,
                updated_at = UTC_TIMESTAMP(3), row_version = row_version + 1
            WHERE id = @id AND project_id = @projectId AND deleted_at IS NULL;
            """;
        foreach (var item in items)
        {
            await using MySqlCommand command = new(sql, connection, transaction);
            command.AddParameter("@id", item.ChapterId);
            command.AddParameter("@parentId", item.ParentId);
            command.AddParameter("@sortKey", item.SortKey);
            command.AddParameter("@projectId", projectId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM rp_chapter
            WHERE project_id = @projectId AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@projectId", projectId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> HasChildrenAsync(
        ulong chapterId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM rp_chapter
            WHERE parent_id = @chapterId AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@chapterId", chapterId);
        long count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static ChapterRecord Map(MySqlDataReader reader)
    {
        int parentOrdinal = reader.GetOrdinal("parent_id");
        return new ChapterRecord
        {
            Id = reader.GetUInt64("id"),
            ProjectId = reader.GetUInt64("project_id"),
            ParentId = reader.IsDBNull(parentOrdinal) ? null : reader.GetUInt64(parentOrdinal),
            ChapterCode = reader.GetString("chapter_code"),
            Title = reader.GetString("current_title"),
            LevelNo = reader.GetUInt16(reader.GetOrdinal("level_no")),
            SortKey = reader.GetDecimal(reader.GetOrdinal("sort_key")),
            WorkflowStatus = reader.GetString("workflow_status"),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            CreatedAt = reader.GetDateTimeOffset("created_at"),
            UpdatedAt = reader.GetDateTimeOffset("updated_at"),
            RowVersion = reader.GetUInt32("row_version"),
        };
    }
}

public sealed class MySqlDataConnectionRepository : IDataConnectionRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlDataConnectionRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<DataConnectionRecord> CreateAsync(
        DataConnectionRecord connectionRecord,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_data_connection
                (project_id, connection_name, connection_type, config_json, credential_ref,
                 connection_status, created_by)
            VALUES
                (@projectId, @name, @type, CAST(@config AS JSON), @credentialRef,
                 'ACTIVE', @actor);
            SELECT LAST_INSERT_ID();
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@projectId", connectionRecord.ProjectId);
        command.AddParameter("@name", connectionRecord.ConnectionName);
        command.AddParameter("@type", connectionRecord.ConnectionType);
        command.AddParameter("@config", MySqlDataAccess.SerializeJson(connectionRecord.Config));
        command.AddParameter("@credentialRef", connectionRecord.CredentialRef);
        command.AddParameter("@actor", actorUserId);
        ulong id = Convert.ToUInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("新建数据连接后无法读取记录。");
    }

    public async Task<DataConnectionRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, connection_name, connection_type, config_json,
                   credential_ref, connection_status, last_tested_at, last_test_result,
                   created_at
            FROM rp_data_connection
            WHERE id = @id AND deleted_at IS NULL
              AND UPPER(connection_type) = 'MYSQL';
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<DataConnectionRecord>> ListAsync(
        ulong? projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, connection_name, connection_type, config_json,
                   credential_ref, connection_status, last_tested_at, last_test_result,
                   created_at
            FROM rp_data_connection
            WHERE deleted_at IS NULL
              AND UPPER(connection_type) = 'MYSQL'
              AND (@projectId IS NULL OR project_id = @projectId)
            ORDER BY created_at DESC, id DESC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@projectId", projectId);
        List<DataConnectionRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task UpdateTestResultAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_data_connection
            SET connection_status = @status, last_tested_at = UTC_TIMESTAMP(3),
                last_test_result = CAST(@result AS JSON), row_version = row_version + 1
            WHERE id = @id AND deleted_at IS NULL
              AND UPPER(connection_type) = 'MYSQL';
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@status", status);
        command.AddParameter("@result", resultJson);
        command.AddParameter("@id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new WorkspaceException(
                "data_connection_not_found",
                $"找不到数据连接：{id}。");
        }
    }

    private static DataConnectionRecord Map(MySqlDataReader reader)
    {
        int projectOrdinal = reader.GetOrdinal("project_id");
        DataConnectionConfig config =
            JsonSerializer.Deserialize<DataConnectionConfig>(
                reader.GetString("config_json"),
                MySqlDataAccess.JsonOptions)
            ?? throw new InvalidOperationException("数据连接配置 JSON 无效。");
        return new DataConnectionRecord
        {
            Id = reader.GetUInt64("id"),
            ProjectId = reader.IsDBNull(projectOrdinal)
                ? null
                : reader.GetUInt64(projectOrdinal),
            ConnectionName = reader.GetString("connection_name"),
            ConnectionType = reader.GetString("connection_type"),
            Config = config,
            CredentialRef = reader.GetNullableString("credential_ref"),
            ConnectionStatus = reader.GetString("connection_status"),
            LastTestedAt = reader.GetNullableDateTimeOffset("last_tested_at"),
            LastTestResultJson = reader.GetNullableString("last_test_result"),
            CreatedAt = reader.GetDateTimeOffset("created_at"),
        };
    }
}

public sealed class MySqlDataSourceRepository : IDataSourceRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlDataSourceRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<DataSourceRecord> CreateAsync(
        DataSourceRecord source,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_data_source
                (project_id, connection_id, source_code, source_name, source_type,
                 source_status, config_json, refresh_mode, created_by)
            VALUES
                (@projectId, @connectionId, @code, @name, @type, 'ACTIVE',
                 CAST(@config AS JSON), 'MANUAL', @actor);
            SELECT LAST_INSERT_ID();
            """;
        string config = MySqlDataAccess.SerializeJson(source.SchemaJson is not null
            ? JsonSerializer.Deserialize<object>(source.SchemaJson)
            : new
            {
                schema = source.SchemaName,
                objectType = source.ObjectType,
                objectName = source.ObjectName,
            });
        try
        {
            await using MySqlConnection connection =
                await _connections.OpenConnectionAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.AddParameter("@projectId", source.ProjectId);
            command.AddParameter("@connectionId", source.ConnectionId != 0 ? source.ConnectionId : (object)DBNull.Value);
            command.AddParameter("@code", source.SourceCode);
            command.AddParameter("@name", source.SourceName);
            command.AddParameter("@type", source.SourceType);
            command.AddParameter("@config", config);
            command.AddParameter("@actor", actorUserId);
            ulong id = Convert.ToUInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            return await GetAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("新建数据源后无法读取记录。");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new WorkspaceException(
                "data_source_code_conflict",
                $"数据源编码 {source.SourceCode} 在当前项目中已存在。",
                exception);
        }
    }

    public async Task<DataSourceRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, connection_id, source_code, source_name, source_type,
                   source_status, config_json, schema_json, created_at
            FROM rp_data_source
            WHERE id = @id AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<DataSourceRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, project_id, connection_id, source_code, source_name, source_type,
                   source_status, config_json, schema_json, created_at
            FROM rp_data_source
            WHERE project_id = @projectId AND deleted_at IS NULL
            ORDER BY created_at DESC, id DESC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@projectId", projectId);
        List<DataSourceRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task UpdateSchemaAsync(
        ulong id,
        string schemaJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_data_source
            SET schema_json = CAST(@schema AS JSON), updated_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@schema", schemaJson);
        command.AddParameter("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DataSourceRecord Map(MySqlDataReader reader)
    {
        int connIdOrdinal = reader.GetOrdinal("connection_id");
        ulong connectionId = reader.IsDBNull(connIdOrdinal) ? 0 : reader.GetUInt64(connIdOrdinal);

        string configJson = reader.GetString("config_json");
        string schemaName = string.Empty;
        string objectType = string.Empty;
        string objectName = string.Empty;
        try
        {
            using JsonDocument config = JsonDocument.Parse(configJson);
            JsonElement root = config.RootElement;
            schemaName = root.TryGetProperty("schema", out JsonElement schemaProp)
                ? schemaProp.GetString() ?? string.Empty : string.Empty;
            objectType = root.TryGetProperty("objectType", out JsonElement objectTypeProp)
                ? objectTypeProp.GetString() ?? string.Empty : string.Empty;
            objectName = root.TryGetProperty("objectName", out JsonElement objectNameProp)
                ? objectNameProp.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            // Non-standard config_json for JSON/API sources; leave fields empty.
        }

        return new DataSourceRecord
        {
            Id = reader.GetUInt64("id"),
            ProjectId = reader.GetUInt64("project_id"),
            ConnectionId = connectionId,
            SourceCode = reader.GetString("source_code"),
            SourceName = reader.GetString("source_name"),
            SourceType = reader.GetString("source_type"),
            SourceStatus = reader.GetString("source_status"),
            SchemaName = schemaName,
            ObjectType = objectType,
            ObjectName = objectName,
            SchemaJson = reader.GetNullableString("schema_json"),
            CreatedAt = reader.GetDateTimeOffset("created_at"),
        };
    }
}

public sealed class MySqlDataSnapshotRepository : IDataSnapshotRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlDataSnapshotRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<DataSnapshotRecord> StartAsync(
        ulong dataSourceId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand sourceLock = new(
                         """
                         SELECT id FROM rp_data_source
                         WHERE id = @id AND deleted_at IS NULL
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            sourceLock.AddParameter("@id", dataSourceId);
            if (await sourceLock.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new WorkspaceException(
                    "data_source_not_found",
                    $"找不到数据源：{dataSourceId}。");
            }
        }

        ulong snapshotNo;
        await using (MySqlCommand next = new(
                         """
                         SELECT COALESCE(MAX(snapshot_no), 0) + 1
                         FROM rp_data_snapshot
                         WHERE data_source_id = @sourceId;
                         """,
                         connection,
                         transaction))
        {
            next.AddParameter("@sourceId", dataSourceId);
            snapshotNo = Convert.ToUInt64(
                await next.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        ulong id;
        await using (MySqlCommand insert = new(
                         """
                         INSERT INTO rp_data_snapshot
                             (data_source_id, snapshot_no, snapshot_status, created_by)
                         VALUES (@sourceId, @snapshotNo, 'CAPTURING', @actor);
                         SELECT LAST_INSERT_ID();
                         """,
                         connection,
                         transaction))
        {
            insert.AddParameter("@sourceId", dataSourceId);
            insert.AddParameter("@snapshotNo", snapshotNo);
            insert.AddParameter("@actor", actorUserId);
            id = Convert.ToUInt64(
                await insert.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("新建快照后无法读取记录。");
    }

    public async Task CompleteAsync(
        ulong snapshotId,
        string contentJson,
        string schemaJson,
        string contentHash,
        ulong rowCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_data_snapshot
            SET snapshot_status = 'READY', content_json = CAST(@content AS JSON),
                schema_json = CAST(@schema AS JSON), content_hash = @hash,
                row_count = @rowCount, error_message = NULL
            WHERE id = @id AND snapshot_status = 'CAPTURING';
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@content", contentJson);
        command.AddParameter("@schema", schemaJson);
        command.AddParameter("@hash", contentHash);
        command.AddParameter("@rowCount", rowCount);
        command.AddParameter("@id", snapshotId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据快照 {snapshotId} 状态不允许完成。");
        }
    }

    public async Task FailAsync(
        ulong snapshotId,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_data_snapshot
            SET snapshot_status = 'FAILED', error_message = @message
            WHERE id = @id AND snapshot_status = 'CAPTURING';
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@message", safeMessage);
        command.AddParameter("@id", snapshotId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<DataSnapshotRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        GetOneAsync("id = @value", id, cancellationToken);

    public Task<DataSnapshotRecord?> GetLatestReadyAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken) =>
        GetOneAsync(
            "data_source_id = @value AND snapshot_status = 'READY' ORDER BY snapshot_no DESC LIMIT 1",
            dataSourceId,
            cancellationToken);

    private async Task<DataSnapshotRecord?> GetOneAsync(
        string predicate,
        ulong value,
        CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT id, data_source_id, snapshot_no, snapshot_status, content_json,
                   schema_json, content_hash, row_count, captured_at, error_message
            FROM rp_data_snapshot
            WHERE {predicate};
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@value", value);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static DataSnapshotRecord Map(MySqlDataReader reader)
    {
        int rowCountOrdinal = reader.GetOrdinal("row_count");
        return new DataSnapshotRecord
        {
            Id = reader.GetUInt64("id"),
            DataSourceId = reader.GetUInt64("data_source_id"),
            SnapshotNo = reader.GetUInt64("snapshot_no"),
            SnapshotStatus = reader.GetString("snapshot_status"),
            ContentJson = reader.GetNullableString("content_json"),
            SchemaJson = reader.GetNullableString("schema_json"),
            ContentHash = reader.GetNullableString("content_hash"),
            RowCount = reader.IsDBNull(rowCountOrdinal)
                ? null
                : reader.GetUInt64(rowCountOrdinal),
            CapturedAt = reader.GetDateTimeOffset("captured_at"),
            ErrorMessage = reader.GetNullableString("error_message"),
        };
    }
}

public sealed class MySqlDataFieldRepository : IDataFieldRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlDataFieldRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task ReplaceAsync(
        ulong snapshotId,
        IReadOnlyList<DataFieldRecord> fields,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand delete = new(
                         "DELETE FROM rp_data_field WHERE snapshot_id = @snapshotId;",
                         connection,
                         transaction))
        {
            delete.AddParameter("@snapshotId", snapshotId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = """
            INSERT INTO rp_data_field
                (snapshot_id, field_path, field_name, data_type, is_array,
                 is_nullable, sample_value_json, display_order)
            VALUES
                (@snapshotId, @path, @name, @type, @array, @nullable,
                 CAST(@sample AS JSON), @sort);
            """;
        foreach (DataFieldRecord field in fields)
        {
            await using MySqlCommand command = new(sql, connection, transaction);
            command.AddParameter("@snapshotId", snapshotId);
            command.AddParameter("@path", field.FieldPath);
            command.AddParameter("@name", field.FieldName);
            command.AddParameter("@type", field.DataType.ToString().ToUpperInvariant());
            command.AddParameter("@array", field.IsArray);
            command.AddParameter("@nullable", field.IsNullable);
            command.AddParameter("@sample", field.SampleValueJson ?? "null");
            command.AddParameter("@sort", field.DisplayOrder);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DataFieldRecord>> ListAsync(
        ulong snapshotId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT field.id, field.snapshot_id, field.field_path, field.field_name,
                   field.data_type, field.is_array, field.is_nullable,
                   field.sample_value_json, field.display_order
            FROM
            (
                SELECT id
                FROM rp_data_field
                WHERE snapshot_id = @snapshotId
                  AND (@query IS NULL OR field_name LIKE CONCAT('%', @query, '%')
                       OR field_path LIKE CONCAT('%', @query, '%'))
                ORDER BY display_order, id
                LIMIT @limit
            ) AS selected
            INNER JOIN rp_data_field AS field ON field.id = selected.id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@snapshotId", snapshotId);
        command.AddParameter("@query", string.IsNullOrWhiteSpace(query) ? null : query.Trim());
        command.AddParameter("@limit", limit);
        List<DataFieldRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records
            .OrderBy(record => record.DisplayOrder)
            .ThenBy(record => record.Id)
            .ToList()
            .AsReadOnly();
    }

    public async Task<DataFieldRecord?> FindAsync(
        ulong snapshotId,
        string path,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, snapshot_id, field_path, field_name, data_type, is_array,
                   is_nullable, sample_value_json, display_order
            FROM rp_data_field
            WHERE snapshot_id = @snapshotId AND field_path = @path;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@snapshotId", snapshotId);
        command.AddParameter("@path", path);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static DataFieldRecord Map(MySqlDataReader reader)
    {
        string typeName = reader.GetString("data_type");
        DataValueType type = Enum.TryParse(typeName, true, out DataValueType parsed)
            ? parsed
            : DataValueType.Object;
        return new DataFieldRecord
        {
            Id = reader.GetUInt64("id"),
            SnapshotId = reader.GetUInt64("snapshot_id"),
            FieldPath = reader.GetString("field_path"),
            FieldName = reader.GetString("field_name"),
            Comment = null,
            DataType = type,
            IsArray = reader.GetBoolean(reader.GetOrdinal("is_array")),
            IsNullable = reader.GetBoolean(reader.GetOrdinal("is_nullable")),
            IsBindable = type is not (DataValueType.Binary or DataValueType.Object),
            SampleValueJson = reader.GetNullableString("sample_value_json"),
            DisplayOrder = reader.GetInt32("display_order"),
        };
    }
}

public sealed class MySqlBindingSetRepository : IBindingSetRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlBindingSetRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<BindingSetRecord> GetOrCreateDraftAsync(
        ulong chapterId,
        ulong templateVersionId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand chapterLock = new(
                         """
                         SELECT id FROM rp_chapter
                         WHERE id = @chapterId AND deleted_at IS NULL
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            chapterLock.AddParameter("@chapterId", chapterId);
            if (await chapterLock.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new WorkspaceException(
                    "chapter_not_found",
                    $"找不到章节：{chapterId}。");
            }
        }

        ulong? existingId;
        await using (MySqlCommand existing = new(
                         """
                         SELECT id FROM rp_binding_set
                         WHERE chapter_id = @chapterId
                           AND template_version_id = @versionId
                           AND binding_status = 'DRAFT'
                         ORDER BY version_no DESC
                         LIMIT 1;
                         """,
                         connection,
                         transaction))
        {
            existing.AddParameter("@chapterId", chapterId);
            existing.AddParameter("@versionId", templateVersionId);
            object? result = await existing.ExecuteScalarAsync(cancellationToken);
            existingId = result is null or DBNull
                ? null
                : Convert.ToUInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (existingId.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(existingId.Value, cancellationToken)
                ?? throw new InvalidOperationException("无法读取已有草稿绑定配置。");
        }

        uint versionNo;
        await using (MySqlCommand next = new(
                         """
                         SELECT COALESCE(MAX(version_no), 0) + 1
                         FROM rp_binding_set
                         WHERE chapter_id = @chapterId;
                         """,
                         connection,
                         transaction))
        {
            next.AddParameter("@chapterId", chapterId);
            versionNo = Convert.ToUInt32(
                await next.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        ulong id;
        await using (MySqlCommand insert = new(
                         """
                         INSERT INTO rp_binding_set
                             (chapter_id, version_no, template_version_id, binding_status,
                              validation_status, created_by)
                         VALUES
                             (@chapterId, @versionNo, @templateVersionId, 'DRAFT',
                              'NOT_VALIDATED', @actor);
                         SELECT LAST_INSERT_ID();
                         """,
                         connection,
                         transaction))
        {
            insert.AddParameter("@chapterId", chapterId);
            insert.AddParameter("@versionNo", versionNo);
            insert.AddParameter("@templateVersionId", templateVersionId);
            insert.AddParameter("@actor", actorUserId);
            id = Convert.ToUInt64(
                await insert.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("新建绑定配置后无法读取记录。");
    }

    public async Task<BindingSetRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, chapter_id, version_no, template_version_id, binding_status,
                   validation_status, validation_result_json, created_at
            FROM rp_binding_set
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public Task UpdateValidationAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            id,
            """
            UPDATE rp_binding_set
            SET validation_status = @status,
                validation_result_json = CAST(@result AS JSON)
            WHERE id = @id;
            """,
            status,
            resultJson,
            cancellationToken);

    public Task ResetValidationAsync(ulong id, CancellationToken cancellationToken) =>
        UpdateAsync(
            id,
            """
            UPDATE rp_binding_set
            SET validation_status = 'NOT_VALIDATED', validation_result_json = NULL
            WHERE id = @id;
            """,
            null,
            null,
            cancellationToken);

    private async Task UpdateAsync(
        ulong id,
        string sql,
        string? status,
        string? result,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        if (status is not null)
        {
            command.AddParameter("@status", status);
            command.AddParameter("@result", result);
        }

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new WorkspaceException(
                "binding_set_not_found",
                $"找不到绑定配置：{id}。");
        }
    }

    private static BindingSetRecord Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        ChapterId = reader.GetUInt64("chapter_id"),
        VersionNo = reader.GetUInt32("version_no"),
        TemplateVersionId = reader.GetUInt64("template_version_id"),
        BindingStatus = reader.GetString("binding_status"),
        ValidationStatus = reader.GetString("validation_status"),
        ValidationResultJson = reader.GetNullableString("validation_result_json"),
        CreatedAt = reader.GetDateTimeOffset("created_at"),
    };
}

public sealed class MySqlBindingItemRepository : IBindingItemRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlBindingItemRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<BindingItemRecord> UpsertAsync(
        ulong bindingSetId,
        ulong templateElementId,
        BindingItemUpsert request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_binding_item
                (binding_set_id, template_element_id, target_property, source_kind,
                 data_source_id, source_path, transform_config_json, format_config_json,
                 fallback_value_json, is_required, sort_no)
            VALUES
                (@setId, @elementId, @property, @sourceKind, @sourceId, @path,
                 CAST(@transform AS JSON), CAST(@format AS JSON), CAST(@fallback AS JSON),
                 @required, 0)
            ON DUPLICATE KEY UPDATE
                id = LAST_INSERT_ID(id),
                source_kind = VALUES(source_kind),
                data_source_id = VALUES(data_source_id),
                source_path = VALUES(source_path),
                transform_config_json = VALUES(transform_config_json),
                format_config_json = VALUES(format_config_json),
                fallback_value_json = VALUES(fallback_value_json),
                is_required = VALUES(is_required),
                updated_at = UTC_TIMESTAMP(3);
            SELECT LAST_INSERT_ID();
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@setId", bindingSetId);
        command.AddParameter("@elementId", templateElementId);
        command.AddParameter("@property", request.TargetProperty);
        command.AddParameter("@sourceKind", request.SourceKind);
        command.AddParameter("@sourceId", request.DataSourceId);
        command.AddParameter("@path", request.SourcePath);
        command.AddParameter("@transform", request.TransformConfigJson);
        command.AddParameter("@format", request.FormatConfigJson);
        command.AddParameter("@fallback", request.FallbackValueJson);
        command.AddParameter("@required", request.IsRequired);
        ulong id = Convert.ToUInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        return await GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("保存绑定项后无法读取记录。");
    }

    public async Task<IReadOnlyList<BindingItemRecord>> ListAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, binding_set_id, template_element_id, target_property, source_kind,
                   data_source_id, source_path, transform_config_json, format_config_json,
                   fallback_value_json, is_required, sort_no, created_at, updated_at
            FROM rp_binding_item
            WHERE binding_set_id = @setId
            ORDER BY sort_no, id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@setId", bindingSetId);
        List<BindingItemRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task<BindingItemRecord?> GetAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, binding_set_id, template_element_id, target_property, source_kind,
                   data_source_id, source_path, transform_config_json, format_config_json,
                   fallback_value_json, is_required, sort_no, created_at, updated_at
            FROM rp_binding_item
            WHERE binding_set_id = @setId AND template_element_id = @elementId
            ORDER BY id
            LIMIT 1;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@setId", bindingSetId);
        command.AddParameter("@elementId", templateElementId);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<bool> DeleteAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM rp_binding_item
            WHERE binding_set_id = @setId AND template_element_id = @elementId;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@setId", bindingSetId);
        command.AddParameter("@elementId", templateElementId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<BindingItemRecord?> GetByIdAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, binding_set_id, template_element_id, target_property, source_kind,
                   data_source_id, source_path, transform_config_json, format_config_json,
                   fallback_value_json, is_required, sort_no, created_at, updated_at
            FROM rp_binding_item
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static BindingItemRecord Map(MySqlDataReader reader)
    {
        int sourceIdOrdinal = reader.GetOrdinal("data_source_id");
        return new BindingItemRecord
        {
            Id = reader.GetUInt64("id"),
            BindingSetId = reader.GetUInt64("binding_set_id"),
            TemplateElementId = reader.GetUInt64("template_element_id"),
            TargetProperty = reader.GetString("target_property"),
            SourceKind = reader.GetString("source_kind"),
            DataSourceId = reader.IsDBNull(sourceIdOrdinal)
                ? null
                : reader.GetUInt64(sourceIdOrdinal),
            SourcePath = reader.GetNullableString("source_path"),
            TransformConfigJson = NormalizeOptionalJson(
                reader.GetNullableString("transform_config_json")),
            FormatConfigJson = NormalizeOptionalJson(
                reader.GetNullableString("format_config_json")),
            FallbackValueJson = NormalizeOptionalJson(
                reader.GetNullableString("fallback_value_json")),
            IsRequired = reader.GetBoolean(reader.GetOrdinal("is_required")),
            SortNo = reader.GetInt32("sort_no"),
            CreatedAt = reader.GetDateTimeOffset("created_at"),
            UpdatedAt = reader.GetDateTimeOffset("updated_at"),
        };
    }

    private static string? NormalizeOptionalJson(string? json) =>
        string.Equals(json?.Trim(), "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : json;
}

#pragma warning restore CS1591
