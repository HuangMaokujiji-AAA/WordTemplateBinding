#pragma warning disable CS1591
using MySqlConnector;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Database;

public sealed class MySqlTemplateRepository : ITemplateRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlTemplateRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<TemplateRecord> CreateAsync(
        TemplateCreateRequest request,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_template
                (template_code, template_name, template_type, category_code,
                 template_status, description, created_by)
            VALUES
                (@code, @name, @type, @category, 'ACTIVE', @description, @actor);
            SELECT LAST_INSERT_ID();
            """;
        try
        {
            await using MySqlConnection connection =
                await _connections.OpenConnectionAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.AddParameter("@code", request.TemplateCode);
            command.AddParameter("@name", request.TemplateName);
            command.AddParameter("@type", request.TemplateType);
            command.AddParameter("@category", request.CategoryCode);
            command.AddParameter("@description", request.Description);
            command.AddParameter("@actor", actorUserId);
            ulong id = Convert.ToUInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            return await GetAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("新建模板后无法读取记录。");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new TemplatePersistenceException(
                "template_code_conflict",
                $"模板编码 {request.TemplateCode} 已存在。",
                exception);
        }
    }

    public Task<TemplateRecord?> GetAsync(ulong id, CancellationToken cancellationToken) =>
        GetOneAsync("id = @value", id, cancellationToken);

    public Task<TemplateRecord?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken) =>
        GetOneAsync("template_code = @value", code, cancellationToken);

    public async Task<PagedResult<TemplateRecord>> ListAsync(
        TemplateListQuery query,
        CancellationToken cancellationToken)
    {
        const string where = """
            deleted_at IS NULL
              AND (@name IS NULL OR template_name LIKE CONCAT('%', @name, '%'))
              AND (@code IS NULL OR template_code LIKE CONCAT('%', @code, '%'))
              AND (@type IS NULL OR template_type = @type)
              AND (@status IS NULL OR template_status = @status)
            """;
        string countSql = $"SELECT COUNT(*) FROM rp_template WHERE {where};";
        string listSql = $"""
            SELECT id, template_code, template_name, template_type, category_code,
                   template_status, description, current_version_no, created_at,
                   updated_at, row_version
            FROM rp_template
            WHERE {where}
            ORDER BY updated_at DESC, id DESC
            LIMIT @limit OFFSET @offset;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand count = new(countSql, connection);
        AddFilters(count, query);
        long total = Convert.ToInt64(
            await count.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        await using MySqlCommand list = new(listSql, connection);
        AddFilters(list, query);
        list.AddParameter("@limit", query.PageSize);
        list.AddParameter("@offset", checked((query.Page - 1) * query.PageSize));
        List<TemplateRecord> records = new();
        await using MySqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return new PagedResult<TemplateRecord>(
            records.AsReadOnly(),
            total,
            query.Page,
            query.PageSize);
    }

    public async Task<bool> UpdateAsync(
        ulong templateId,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var setClauses = new List<string>();

        if (request.TemplateName is not null)
            setClauses.Add("template_name = @name");
        if (request.CategoryCode is not null)
            setClauses.Add("category_code = @category");
        if (request.Description is not null)
            setClauses.Add("description = @description");
        if (request.TemplateStatus is not null)
            setClauses.Add("template_status = @status");

        if (setClauses.Count == 0)
            return false;

        setClauses.Add("updated_at = UTC_TIMESTAMP(3)");
        setClauses.Add("row_version = row_version + 1");

        string sql = $"""
            UPDATE rp_template
            SET {string.Join(", ", setClauses)}
            WHERE id = @id AND row_version = @expectedRowVersion AND deleted_at IS NULL;
            """;

        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", templateId);
        command.AddParameter("@expectedRowVersion", request.ExpectedRowVersion);
        if (request.TemplateName is not null)
            command.AddParameter("@name", request.TemplateName);
        if (request.CategoryCode is not null)
            command.AddParameter("@category", request.CategoryCode);
        if (request.Description is not null)
            command.AddParameter("@description", request.Description);
        if (request.TemplateStatus is not null)
            command.AddParameter("@status", request.TemplateStatus);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ArchiveAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_template
            SET template_status = 'ARCHIVED',
                deleted_at = UTC_TIMESTAMP(3),
                updated_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND deleted_at IS NULL;
            """;

        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", templateId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RestoreAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_template
            SET template_status = 'ACTIVE',
                deleted_at = NULL,
                updated_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND deleted_at IS NOT NULL;
            """;

        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", templateId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<TemplateRecord?> GetOneAsync(
        string predicate,
        object value,
        CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT id, template_code, template_name, template_type, category_code,
                   template_status, description, current_version_no, created_at,
                   updated_at, row_version
            FROM rp_template
            WHERE {predicate} AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@value", value);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static void AddFilters(MySqlCommand command, TemplateListQuery query)
    {
        command.AddParameter("@name", NullIfBlank(query.Name));
        command.AddParameter("@code", NullIfBlank(query.Code));
        command.AddParameter("@type", NullIfBlank(query.Type));
        command.AddParameter("@status", NullIfBlank(query.Status));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TemplateRecord Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        TemplateCode = reader.GetString("template_code"),
        TemplateName = reader.GetString("template_name"),
        TemplateType = reader.GetString("template_type"),
        CategoryCode = reader.GetNullableString("category_code"),
        TemplateStatus = reader.GetString("template_status"),
        Description = reader.GetNullableString("description"),
        CurrentVersionNo = reader.GetUInt32("current_version_no"),
        CreatedAt = reader.GetDateTimeOffset("created_at"),
        UpdatedAt = reader.GetDateTimeOffset("updated_at"),
        RowVersion = reader.GetUInt32("row_version"),
    };
}

public sealed class MySqlTemplateVersionRepository : ITemplateVersionRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlTemplateVersionRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<TemplateVersionRecord> CreateNextAsync(
        ulong templateId,
        ulong fileObjectId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand templateLock = new(
                         "SELECT id FROM rp_template WHERE id = @id AND deleted_at IS NULL FOR UPDATE;",
                         connection,
                         transaction))
        {
            templateLock.AddParameter("@id", templateId);
            if (await templateLock.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new TemplateNotFoundException(templateId);
            }
        }

        uint versionNo;
        await using (MySqlCommand next = new(
                         """
                         SELECT COALESCE(MAX(version_no), 0) + 1
                         FROM rp_template_version
                         WHERE template_id = @templateId;
                         """,
                         connection,
                         transaction))
        {
            next.AddParameter("@templateId", templateId);
            versionNo = Convert.ToUInt32(
                await next.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        ulong id;
        await using (MySqlCommand insert = new(
                         """
                         INSERT INTO rp_template_version
                             (template_id, version_no, file_object_id, version_status, created_by)
                         VALUES (@templateId, @versionNo, @fileId, 'UPLOADED', @actor);
                         SELECT LAST_INSERT_ID();
                         """,
                         connection,
                         transaction))
        {
            insert.AddParameter("@templateId", templateId);
            insert.AddParameter("@versionNo", versionNo);
            insert.AddParameter("@fileId", fileObjectId);
            insert.AddParameter("@actor", actorUserId);
            id = Convert.ToUInt64(
                await insert.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("新建模板版本后无法读取记录。");
    }

    public async Task<TemplateVersionRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, template_id, version_no, file_object_id, version_status,
                   parser_name, parser_version, parse_result_json, element_count,
                   style_fingerprint, created_at
            FROM rp_template_version
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<TemplateVersionRecord>> ListAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, template_id, version_no, file_object_id, version_status,
                   parser_name, parser_version, parse_result_json, element_count,
                   style_fingerprint, created_at
            FROM rp_template_version
            WHERE template_id = @templateId
            ORDER BY version_no DESC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@templateId", templateId);
        List<TemplateVersionRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public Task UpdateParsingAsync(ulong id, CancellationToken cancellationToken) =>
        UpdateStatusOnlyAsync(
            id,
            """
            UPDATE rp_template_version
            SET version_status = 'PARSING', parser_name = 'WordTemplateScanner',
                parser_version = '2.0'
            WHERE id = @id AND version_status IN ('UPLOADED', 'READY', 'READY_WITH_WARNINGS', 'FAILED');
            """,
            cancellationToken);

    public async Task CompleteAsync(
        ulong id,
        string status,
        string parseResultJson,
        uint elementCount,
        string? styleFingerprint,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        ulong templateId;
        uint versionNo;
        await using (MySqlCommand find = new(
                         """
                         SELECT template_id, version_no
                         FROM rp_template_version
                         WHERE id = @id
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            find.AddParameter("@id", id);
            await using MySqlDataReader reader =
                await find.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new TemplatePersistenceException(
                    "template_version_not_found",
                    $"找不到模板版本：{id}。");
            }

            templateId = reader.GetUInt64(0);
            versionNo = reader.GetUInt32(1);
        }

        await using (MySqlCommand update = new(
                         """
                         UPDATE rp_template_version
                         SET version_status = @status, parser_name = 'WordTemplateScanner',
                             parser_version = '2.0', parse_result_json = CAST(@parse AS JSON),
                             element_count = @count, style_fingerprint = @fingerprint
                         WHERE id = @id;
                         """,
                         connection,
                         transaction))
        {
            update.AddParameter("@status", status);
            update.AddParameter("@parse", parseResultJson);
            update.AddParameter("@count", elementCount);
            update.AddParameter("@fingerprint", styleFingerprint);
            update.AddParameter("@id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (MySqlCommand updateTemplate = new(
                         """
                         UPDATE rp_template
                         SET current_version_no = @versionNo,
                             updated_at = UTC_TIMESTAMP(3),
                             row_version = row_version + 1
                         WHERE id = @templateId;
                         """,
                         connection,
                         transaction))
        {
            updateTemplate.AddParameter("@versionNo", versionNo);
            updateTemplate.AddParameter("@templateId", templateId);
            await updateTemplate.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(
        ulong id,
        string parseResultJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_template_version
            SET version_status = 'FAILED', parser_name = 'WordTemplateScanner',
                parser_version = '2.0', parse_result_json = CAST(@parse AS JSON)
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@parse", parseResultJson);
        command.AddParameter("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateStatusOnlyAsync(
        ulong id,
        string sql,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到可重新扫描的模板版本：{id}。");
        }
    }

    private static TemplateVersionRecord Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        TemplateId = reader.GetUInt64("template_id"),
        VersionNo = reader.GetUInt32("version_no"),
        FileObjectId = reader.GetUInt64("file_object_id"),
        VersionStatus = reader.GetString("version_status"),
        ParserName = reader.GetNullableString("parser_name"),
        ParserVersion = reader.GetNullableString("parser_version"),
        ParseResultJson = reader.GetNullableString("parse_result_json"),
        ElementCount = reader.GetUInt32("element_count"),
        StyleFingerprint = reader.GetNullableString("style_fingerprint"),
        CreatedAt = reader.GetDateTimeOffset("created_at"),
    };
}

public sealed class MySqlTemplateElementRepository : ITemplateElementRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlTemplateElementRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task ReplaceAsync(
        ulong templateVersionId,
        IReadOnlyList<TemplateElementRecord> elements,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        Dictionary<string, ulong> existing = new(StringComparer.OrdinalIgnoreCase);
        await using (MySqlCommand select = new(
                         """
                         SELECT id, element_key
                         FROM rp_template_element
                         WHERE template_version_id = @versionId
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            select.AddParameter("@versionId", templateVersionId);
            await using MySqlDataReader reader =
                await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing[reader.GetString("element_key")] = reader.GetUInt64("id");
            }
        }

        const string insertSql = """
            INSERT INTO rp_template_element
                (template_version_id, element_key, element_type, locator_type, display_name,
                 locator_json, binding_schema_json, default_value_json, is_required,
                 sort_no, parse_status, parse_message)
            VALUES
                (@versionId, @key, @type, @locatorType, @name, CAST(@locator AS JSON),
                 CAST(@schema AS JSON), CAST(@defaultValue AS JSON), @required,
                 @sort, @status, @message);
            """;
        const string updateSql = """
            UPDATE rp_template_element
            SET element_key = @key,
                element_type = @type,
                locator_type = @locatorType,
                display_name = @name,
                locator_json = CAST(@locator AS JSON),
                binding_schema_json = CAST(@schema AS JSON),
                default_value_json = CAST(@defaultValue AS JSON),
                is_required = @required,
                sort_no = @sort,
                parse_status = @status,
                parse_message = @message
            WHERE id = @id AND template_version_id = @versionId;
            """;
        HashSet<ulong> retainedIds = new();
        foreach (TemplateElementRecord element in elements)
        {
            bool isUpdate = existing.TryGetValue(element.ElementKey, out ulong existingId);
            await using MySqlCommand command = new(
                isUpdate ? updateSql : insertSql,
                connection,
                transaction);
            command.AddParameter("@versionId", templateVersionId);
            command.AddParameter("@key", element.ElementKey);
            command.AddParameter("@type", element.ElementType);
            command.AddParameter("@locatorType", element.LocatorType);
            command.AddParameter("@name", element.DisplayName);
            command.AddParameter("@locator", element.LocatorJson);
            command.AddParameter("@schema", element.BindingSchemaJson ?? "null");
            command.AddParameter("@defaultValue", element.DefaultValueJson ?? "null");
            command.AddParameter("@required", element.IsRequired);
            command.AddParameter("@sort", element.SortNo);
            command.AddParameter("@status", element.ParseStatus);
            command.AddParameter("@message", element.ParseMessage);
            if (isUpdate)
            {
                command.AddParameter("@id", existingId);
                retainedIds.Add(existingId);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (ulong staleId in existing.Values.Where(id => !retainedIds.Contains(id)))
        {
            await using MySqlCommand markStale = new(
                """
                UPDATE rp_template_element
                SET parse_status = 'STALE',
                    parse_message = '重新扫描未找到此元素。'
                WHERE id = @id AND template_version_id = @versionId;
                """,
                connection,
                transaction);
            markStale.AddParameter("@id", staleId);
            markStale.AddParameter("@versionId", templateVersionId);
            await markStale.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateElementRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, template_version_id, element_key, element_type, locator_type,
                   display_name, locator_json, binding_schema_json, default_value_json,
                   is_required, sort_no, parse_status, parse_message
            FROM rp_template_element
            WHERE template_version_id = @versionId
              AND UPPER(parse_status) <> 'STALE'
            ORDER BY sort_no, id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@versionId", templateVersionId);
        List<TemplateElementRecord> records = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Map(reader));
        }

        return records.AsReadOnly();
    }

    public async Task<TemplateElementRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, template_version_id, element_key, element_type, locator_type,
                   display_name, locator_json, binding_schema_json, default_value_json,
                   is_required, sort_no, parse_status, parse_message
            FROM rp_template_element
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static TemplateElementRecord Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        TemplateVersionId = reader.GetUInt64("template_version_id"),
        ElementKey = reader.GetString("element_key"),
        ElementType = reader.GetString("element_type"),
        LocatorType = reader.GetString("locator_type"),
        DisplayName = reader.GetNullableString("display_name"),
        LocatorJson = reader.GetString("locator_json"),
        BindingSchemaJson = reader.GetNullableString("binding_schema_json"),
        DefaultValueJson = reader.GetNullableString("default_value_json"),
        IsRequired = reader.GetBoolean(reader.GetOrdinal("is_required")),
        SortNo = reader.GetInt32("sort_no"),
        ParseStatus = reader.GetString("parse_status"),
        ParseMessage = reader.GetNullableString("parse_message"),
    };
}

#pragma warning restore CS1591
