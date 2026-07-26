#pragma warning disable CS1591
using MySqlConnector;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Database;

public sealed class MySqlTemplateSegmentRepository : ITemplateSegmentRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlTemplateSegmentRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<TemplateSegmentRecord>> ReplaceForVersionAsync(
        ulong templateVersionId,
        IReadOnlyList<TemplateSegmentDefinition> segments,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand versionLock = new(
                         """
                         SELECT id
                         FROM rp_template_version
                         WHERE id = @versionId
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            versionLock.AddParameter("@versionId", templateVersionId);
            if (await versionLock.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new InvalidOperationException(
                    $"找不到模板版本：{templateVersionId}。");
            }
        }

        Dictionary<string, (ulong Id, ulong? ParentId, string? Fingerprint)> existing =
            new(StringComparer.Ordinal);
        await using (MySqlCommand select = new(
                         """
                         SELECT id, parent_segment_id, segment_key, segment_fingerprint
                         FROM rp_template_segment
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
                existing[reader.GetString("segment_key")] = (
                    reader.GetUInt64("id"),
                    reader.IsDBNull(reader.GetOrdinal("parent_segment_id"))
                        ? null
                        : reader.GetUInt64("parent_segment_id"),
                    reader.GetNullableString("segment_fingerprint"));
            }
        }

        Dictionary<string, ulong> ids = existing.ToDictionary(
            item => item.Key,
            item => item.Value.Id,
            StringComparer.Ordinal);
        foreach (TemplateSegmentDefinition segment in segments.OrderBy(item => item.Depth))
        {
            ulong? parentId = segment.ParentSegmentKey is null
                ? null
                : ids.GetValueOrDefault(segment.ParentSegmentKey);
            if (existing.TryGetValue(segment.SegmentKey, out var current))
            {
                bool fingerprintChanged = !string.Equals(
                    current.Fingerprint,
                    segment.SegmentFingerprint,
                    StringComparison.Ordinal);
                await using MySqlCommand update = new(
                    """
                    UPDATE rp_template_segment
                    SET parent_segment_id = @parentId,
                        segment_name = @name,
                        segment_type = @type,
                        anchor_type = @anchorType,
                        start_anchor_json = CAST(@startAnchor AS JSON),
                        end_anchor_json =
                            IF(@endAnchor IS NULL, NULL, CAST(@endAnchor AS JSON)),
                        document_order_start = @orderStart,
                        document_order_end = @orderEnd,
                        segment_status = @status,
                        segment_fingerprint = @fingerprint,
                        sort_no = @sortNo,
                        preview_file_object_id =
                            IF(@fingerprintChanged, NULL, preview_file_object_id),
                        preview_status =
                            IF(@fingerprintChanged, 'STALE', preview_status),
                        preview_error_message =
                            IF(@fingerprintChanged, NULL, preview_error_message),
                        updated_by = @actor,
                        row_version = row_version + 1
                    WHERE id = @id;
                    """,
                    connection,
                    transaction);
                AddDefinitionParameters(
                    update, segment, parentId, actorUserId, templateVersionId);
                update.AddParameter("@id", current.Id);
                update.AddParameter("@fingerprintChanged", fingerprintChanged);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using MySqlCommand insert = new(
                    """
                    INSERT INTO rp_template_segment
                        (template_version_id, parent_segment_id, segment_key, segment_name,
                         segment_type, anchor_type, start_anchor_json, end_anchor_json,
                         document_order_start, document_order_end, segment_status,
                         segment_fingerprint, preview_status, sort_no, created_by, updated_by)
                    VALUES
                        (@versionId, @parentId, @key, @name, @type, @anchorType,
                         CAST(@startAnchor AS JSON),
                         IF(@endAnchor IS NULL, NULL, CAST(@endAnchor AS JSON)),
                         @orderStart, @orderEnd, @status, @fingerprint,
                         'NOT_CREATED', @sortNo, @actor, @actor);
                    SELECT LAST_INSERT_ID();
                    """,
                    connection,
                    transaction);
                AddDefinitionParameters(
                    insert, segment, parentId, actorUserId, templateVersionId);
                ulong id = Convert.ToUInt64(
                    await insert.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                ids[segment.SegmentKey] = id;
            }
        }

        HashSet<ulong> retained = segments.Select(item => ids[item.SegmentKey]).ToHashSet();
        List<ulong> stale = existing.Values.Select(item => item.Id)
            .Where(id => !retained.Contains(id))
            .ToList();
        foreach (ulong id in OrderChildrenFirst(stale, existing.Values))
        {
            await using MySqlCommand clear = new(
                "UPDATE rp_template_element SET segment_id = NULL WHERE segment_id = @id;",
                connection,
                transaction);
            clear.AddParameter("@id", id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
            await using MySqlCommand delete = new(
                "DELETE FROM rp_template_segment WHERE id = @id;",
                connection,
                transaction);
            delete.AddParameter("@id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await ListAsync(templateVersionId, cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateSegmentRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(
            $"{SelectColumns} WHERE template_version_id = @versionId ORDER BY document_order_start, sort_no, id;",
            connection);
        command.AddParameter("@versionId", templateVersionId);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<TemplateSegmentRecord?> GetAsync(
        ulong segmentId,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(
            $"{SelectColumns} WHERE id = @id;",
            connection);
        command.AddParameter("@id", segmentId);
        IReadOnlyList<TemplateSegmentRecord> records =
            await ReadAsync(command, cancellationToken);
        return records.SingleOrDefault();
    }

    public async Task SetPreviewAsync(
        ulong segmentId,
        ulong? previewFileObjectId,
        string previewStatus,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(
            """
            UPDATE rp_template_segment
            SET preview_file_object_id = @fileId,
                preview_status = @status,
                preview_error_message = @error,
                row_version = row_version + 1
            WHERE id = @id;
            """,
            connection);
        command.AddParameter("@id", segmentId);
        command.AddParameter("@fileId", previewFileObjectId);
        command.AddParameter("@status", previewStatus);
        command.AddParameter("@error", errorMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectColumns = """
        SELECT id, template_version_id, parent_segment_id, segment_key, segment_name,
               segment_type, anchor_type, start_anchor_json, end_anchor_json,
               document_order_start, document_order_end, segment_status,
               segment_fingerprint, preview_file_object_id, preview_status,
               preview_error_message, sort_no, row_version, created_by, created_at,
               updated_by, updated_at
        FROM rp_template_segment
        """;

    private static void AddDefinitionParameters(
        MySqlCommand command,
        TemplateSegmentDefinition segment,
        ulong? parentId,
        ulong? actorUserId,
        ulong templateVersionId)
    {
        command.AddParameter("@versionId", templateVersionId);
        command.AddParameter("@parentId", parentId);
        command.AddParameter("@key", segment.SegmentKey);
        command.AddParameter("@name", segment.SegmentName);
        command.AddParameter("@type", segment.SegmentType);
        command.AddParameter("@anchorType", segment.AnchorType);
        command.AddParameter("@startAnchor", segment.StartAnchorJson);
        command.AddParameter("@endAnchor", segment.EndAnchorJson);
        command.AddParameter("@orderStart", segment.DocumentOrderStart);
        command.AddParameter("@orderEnd", segment.DocumentOrderEnd);
        command.AddParameter("@status", segment.SegmentStatus);
        command.AddParameter("@fingerprint", segment.SegmentFingerprint);
        command.AddParameter("@sortNo", segment.SortNo);
        command.AddParameter("@actor", actorUserId);
    }

    private static IEnumerable<ulong> OrderChildrenFirst(
        IReadOnlyCollection<ulong> stale,
        IEnumerable<(ulong Id, ulong? ParentId, string? Fingerprint)> records)
    {
        Dictionary<ulong, ulong?> parents = records.ToDictionary(item => item.Id, item => item.ParentId);
        return stale.OrderByDescending(id =>
        {
            int depth = 0;
            ulong? current = id;
            while (current is not null && parents.TryGetValue(current.Value, out current))
            {
                depth++;
            }

            return depth;
        });
    }

    private static async Task<IReadOnlyList<TemplateSegmentRecord>> ReadAsync(
        MySqlCommand command,
        CancellationToken cancellationToken)
    {
        List<TemplateSegmentRecord> result = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TemplateSegmentRecord
            {
                Id = reader.GetUInt64("id"),
                TemplateVersionId = reader.GetUInt64("template_version_id"),
                ParentSegmentId = reader.IsDBNull(reader.GetOrdinal("parent_segment_id"))
                    ? null
                    : reader.GetUInt64("parent_segment_id"),
                SegmentKey = reader.GetString("segment_key"),
                SegmentName = reader.GetString("segment_name"),
                SegmentType = reader.GetString("segment_type"),
                AnchorType = reader.GetString("anchor_type"),
                StartAnchorJson = reader.GetString("start_anchor_json"),
                EndAnchorJson = reader.GetNullableString("end_anchor_json"),
                DocumentOrderStart = reader.GetUInt32("document_order_start"),
                DocumentOrderEnd = reader.GetUInt32("document_order_end"),
                SegmentStatus = reader.GetString("segment_status"),
                SegmentFingerprint = reader.GetNullableString("segment_fingerprint"),
                PreviewFileObjectId = reader.IsDBNull(reader.GetOrdinal("preview_file_object_id"))
                    ? null
                    : reader.GetUInt64("preview_file_object_id"),
                PreviewStatus = reader.GetString("preview_status"),
                PreviewErrorMessage = reader.GetNullableString("preview_error_message"),
                SortNo = reader.GetInt32("sort_no"),
                RowVersion = reader.GetUInt32("row_version"),
                CreatedBy = reader.IsDBNull(reader.GetOrdinal("created_by"))
                    ? null
                    : reader.GetUInt64("created_by"),
                CreatedAt = reader.GetDateTimeOffset("created_at"),
                UpdatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by"))
                    ? null
                    : reader.GetUInt64("updated_by"),
                UpdatedAt = reader.GetDateTimeOffset("updated_at"),
            });
        }

        return result.AsReadOnly();
    }
}

#pragma warning restore CS1591
