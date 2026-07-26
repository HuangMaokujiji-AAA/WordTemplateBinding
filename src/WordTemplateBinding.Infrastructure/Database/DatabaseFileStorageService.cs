#pragma warning disable CS1591
using System.Buffers;
using System.Data;
using System.Security.Cryptography;
using MySqlConnector;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.Database;

public sealed class MySqlFileObjectRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlFileObjectRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<ulong> CreateAsync(
        FileStoreRequest request,
        string objectKey,
        int chunkSize,
        int totalChunks,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_file_object
                (storage_provider, bucket_name, object_key, original_name, file_ext,
                 mime_type, file_size, sha256, chunk_size, total_chunks, object_status,
                 metadata_json, created_by)
            VALUES
                ('DATABASE', @bucket, @key, @name, @extension, @mime, @size, @sha,
                 @chunkSize, @totalChunks, 'UPLOADING', CAST(@metadata AS JSON), @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@bucket", request.BucketName);
        command.AddParameter("@key", objectKey);
        command.AddParameter("@name", request.OriginalName);
        command.AddParameter("@extension", request.FileExtension);
        command.AddParameter("@mime", request.MimeType);
        command.AddParameter("@size", checked((ulong)request.ExpectedLength));
        command.AddParameter("@sha", request.ExpectedSha256);
        command.AddParameter("@chunkSize", chunkSize);
        command.AddParameter("@totalChunks", totalChunks);
        command.AddParameter("@metadata", request.MetadataJson ?? "null");
        command.AddParameter("@createdBy", request.CreatedBy);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToUInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<FileObjectMetadata?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, object_key, original_name, file_ext, mime_type, file_size, sha256,
                   chunk_size, total_chunks, object_status, upload_completed_at, deleted_at
            FROM rp_file_object
            WHERE id = @id;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task SetVerifyingAsync(ulong id, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_file_object
            SET object_status = 'VERIFYING', row_version = row_version + 1
            WHERE id = @id AND object_status = 'UPLOADING' AND deleted_at IS NULL;
            """;
        await ExecuteStateAsync(id, sql, cancellationToken);
    }

    public async Task SetReadyAsync(
        ulong id,
        string sha256,
        int totalChunks,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_file_object
            SET object_status = 'READY', sha256 = @sha, total_chunks = @chunks,
                upload_completed_at = UTC_TIMESTAMP(3), row_version = row_version + 1
            WHERE id = @id AND object_status = 'VERIFYING' AND deleted_at IS NULL;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        command.AddParameter("@sha", sha256);
        command.AddParameter("@chunks", totalChunks);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new DatabaseFileException(
                "database_file_not_ready",
                "文件状态已变化，无法完成上传。");
        }
    }

    public async Task SetFailedAsync(ulong id, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rp_file_object
            SET object_status = 'FAILED', row_version = row_version + 1
            WHERE id = @id AND object_status IN ('UPLOADING', 'VERIFYING');
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteStateAsync(
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
            throw new DatabaseFileException(
                "database_file_not_ready",
                "文件状态不允许执行当前操作。");
        }
    }

    private static FileObjectMetadata Map(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt64("id"),
        ObjectKey = reader.GetString("object_key"),
        OriginalName = reader.GetString("original_name"),
        FileExtension = reader.GetNullableString("file_ext"),
        MimeType = reader.GetNullableString("mime_type"),
        FileSize = checked((long)reader.GetUInt64("file_size")),
        Sha256 = reader.GetNullableString("sha256"),
        ChunkSize = checked((int)reader.GetUInt32("chunk_size")),
        TotalChunks = checked((int)reader.GetUInt32("total_chunks")),
        ObjectStatus = reader.GetString("object_status"),
        UploadCompletedAt = reader.GetNullableDateTimeOffset("upload_completed_at"),
        DeletedAt = reader.GetNullableDateTimeOffset("deleted_at"),
    };
}

public sealed class MySqlFileChunkRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlFileChunkRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<bool> PutAsync(
        ulong fileObjectId,
        int chunkNo,
        ReadOnlyMemory<byte> data,
        string sha256,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        const string existingSql = """
            SELECT chunk_length, chunk_sha256
            FROM rp_file_chunk
            WHERE file_object_id = @fileId AND chunk_no = @chunkNo
            FOR UPDATE;
            """;
        await using (MySqlCommand existing = new(existingSql, connection, transaction))
        {
            existing.AddParameter("@fileId", fileObjectId);
            existing.AddParameter("@chunkNo", chunkNo);
            await using MySqlDataReader reader =
                await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                int existingLength = checked((int)reader.GetUInt32(0));
                string existingHash = reader.GetString(1);
                if (existingLength != data.Length ||
                    !string.Equals(existingHash, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DatabaseFileException(
                        "file_chunk_conflict",
                        $"文件分片 {chunkNo} 与已上传内容冲突。");
                }

                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        const string insertSql = """
            INSERT INTO rp_file_chunk
                (file_object_id, chunk_no, chunk_length, chunk_sha256, chunk_data)
            VALUES (@fileId, @chunkNo, @length, @sha, @data);
            """;
        await using (MySqlCommand insert = new(insertSql, connection, transaction))
        {
            insert.AddParameter("@fileId", fileObjectId);
            insert.AddParameter("@chunkNo", chunkNo);
            insert.AddParameter("@length", data.Length);
            insert.AddParameter("@sha", sha256);
            insert.AddParameter("@data", data.ToArray(), MySqlDbType.MediumBlob);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(int Count, long Bytes, int MinNo, int MaxNo)> GetStatsAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*) AS chunk_count, COALESCE(SUM(chunk_length), 0) AS byte_count,
                   COALESCE(MIN(chunk_no), 0) AS min_no, COALESCE(MAX(chunk_no), 0) AS max_no
            FROM rp_file_chunk
            WHERE file_object_id = @fileId;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@fileId", fileObjectId);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (
            checked((int)reader.GetInt64(0)),
            reader.GetInt64(1),
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)));
    }

    public async Task CopyToAsync(
        ulong fileObjectId,
        Stream destination,
        IncrementalHash? fullHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT chunk_no, chunk_data
            FROM rp_file_chunk
            WHERE file_object_id = @fileId
            ORDER BY chunk_no ASC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@fileId", fileObjectId);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess,
            cancellationToken);
        int expectedChunkNo = 0;
        byte[] hashBuffer = new byte[64 * 1024];
        while (await reader.ReadAsync(cancellationToken))
        {
            int chunkNo = checked((int)reader.GetUInt32(0));
            if (chunkNo != expectedChunkNo++)
            {
                throw new DatabaseFileException(
                    "file_integrity_failed",
                    "文件分片不连续，无法读取。");
            }

            await using Stream chunk = reader.GetStream(1);
            int read;
            while ((read = await chunk.ReadAsync(hashBuffer, cancellationToken)) > 0)
            {
                fullHash?.AppendData(hashBuffer, 0, read);
                await destination.WriteAsync(
                    hashBuffer.AsMemory(0, read),
                    cancellationToken);
            }
        }
    }
}

public sealed class MySqlUploadSessionRepository
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    public MySqlUploadSessionRepository(IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<ulong> CreateAsync(
        ulong fileObjectId,
        FileStoreRequest request,
        int chunkSize,
        int expectedChunks,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_file_upload_session
                (upload_token, file_object_id, expected_file_size, expected_sha256,
                 chunk_size, expected_chunks, upload_status, expires_at, created_by)
            VALUES
                (@token, @fileId, @size, @sha, @chunkSize, @chunks, 'UPLOADING',
                 @expires, @createdBy);
            SELECT LAST_INSERT_ID();
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@token", Guid.NewGuid().ToString());
        command.AddParameter("@fileId", fileObjectId);
        command.AddParameter("@size", checked((ulong)request.ExpectedLength));
        command.AddParameter("@sha", request.ExpectedSha256);
        command.AddParameter("@chunkSize", chunkSize);
        command.AddParameter("@chunks", expectedChunks);
        command.AddParameter("@expires", expiresAt.UtcDateTime);
        command.AddParameter("@createdBy", request.CreatedBy);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToUInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task AddProgressAsync(
        ulong sessionId,
        int bytes,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            sessionId,
            """
            UPDATE rp_file_upload_session
            SET uploaded_chunks = uploaded_chunks + 1,
                uploaded_bytes = uploaded_bytes + @bytes,
                last_activity_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND upload_status = 'UPLOADING' AND expires_at > UTC_TIMESTAMP(3);
            """,
            bytes,
            null,
            cancellationToken);

    public Task CompleteAsync(ulong sessionId, CancellationToken cancellationToken) =>
        UpdateAsync(
            sessionId,
            """
            UPDATE rp_file_upload_session
            SET upload_status = 'COMPLETED', last_activity_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND upload_status IN ('UPLOADING', 'VERIFYING');
            """,
            null,
            null,
            cancellationToken);

    public Task SetVerifyingAsync(ulong sessionId, CancellationToken cancellationToken) =>
        UpdateAsync(
            sessionId,
            """
            UPDATE rp_file_upload_session
            SET upload_status = 'VERIFYING', last_activity_at = UTC_TIMESTAMP(3),
                row_version = row_version + 1
            WHERE id = @id AND upload_status = 'UPLOADING' AND expires_at > UTC_TIMESTAMP(3);
            """,
            null,
            null,
            cancellationToken);

    public Task FailAsync(
        ulong sessionId,
        string status,
        string safeMessage,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            sessionId,
            """
            UPDATE rp_file_upload_session
            SET upload_status = @status, error_message = @message,
                last_activity_at = UTC_TIMESTAMP(3), row_version = row_version + 1
            WHERE id = @id AND upload_status NOT IN ('COMPLETED', 'CANCELLED');
            """,
            null,
            (status, safeMessage),
            cancellationToken);

    private async Task UpdateAsync(
        ulong id,
        string sql,
        int? bytes,
        (string Status, string Message)? failure,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.AddParameter("@id", id);
        if (bytes.HasValue)
        {
            command.AddParameter("@bytes", bytes.Value);
        }

        if (failure.HasValue)
        {
            command.AddParameter("@status", failure.Value.Status);
            command.AddParameter("@message", failure.Value.Message);
        }

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new DatabaseFileException(
                "upload_session_expired",
                "文件上传会话已过期或状态不允许继续上传。");
        }
    }
}

public sealed class DatabaseFileStorageService : IFileStorageService
{
    private readonly MySqlFileObjectRepository _objects;
    private readonly MySqlFileChunkRepository _chunks;
    private readonly MySqlUploadSessionRepository _sessions;
    private readonly DatabaseFileStorageOptions _options;
    private readonly IClock _clock;

    public DatabaseFileStorageService(
        MySqlFileObjectRepository objects,
        MySqlFileChunkRepository chunks,
        MySqlUploadSessionRepository sessions,
        DatabaseFileStorageOptions options,
        IClock clock)
    {
        _objects = objects;
        _chunks = chunks;
        _sessions = sessions;
        _options = options;
        _clock = clock;
    }

    public async Task<StoredFile> StoreAsync(
        Stream source,
        FileStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(source, request);
        int totalChunks = checked((int)(
            (request.ExpectedLength + _options.ChunkSizeBytes - 1) /
            _options.ChunkSizeBytes));
        string objectKey = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
        ulong fileObjectId = await _objects.CreateAsync(
            request,
            objectKey,
            _options.ChunkSizeBytes,
            totalChunks,
            cancellationToken);
        ulong sessionId = await _sessions.CreateAsync(
            fileObjectId,
            request,
            _options.ChunkSizeBytes,
            totalChunks,
            _clock.UtcNow.AddMinutes(_options.UploadSessionMinutes),
            cancellationToken);

        try
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(_options.ChunkSizeBytes);
            try
            {
                long received = 0;
                for (int chunkNo = 0; chunkNo < totalChunks; chunkNo++)
                {
                    int required = checked((int)Math.Min(
                        _options.ChunkSizeBytes,
                        request.ExpectedLength - received));
                    int length = await ReadExactlyAsync(
                        source,
                        rented,
                        required,
                        cancellationToken);
                    if (length != required)
                    {
                        throw new DatabaseFileException(
                            "file_integrity_failed",
                            "上传流提前结束，文件字节总数不匹配。");
                    }

                    string chunkHash = Convert.ToHexString(
                            SHA256.HashData(rented.AsSpan(0, length)))
                        .ToLowerInvariant();
                    bool inserted = await _chunks.PutAsync(
                        fileObjectId,
                        chunkNo,
                        rented.AsMemory(0, length),
                        chunkHash,
                        cancellationToken);
                    if (inserted)
                    {
                        await _sessions.AddProgressAsync(
                            sessionId,
                            length,
                            cancellationToken);
                    }

                    received += length;
                }

                byte[] extra = new byte[1];
                if (await source.ReadAsync(extra, cancellationToken) != 0)
                {
                    throw new DatabaseFileException(
                        "file_integrity_failed",
                        "上传流包含超出声明大小的额外字节。");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }

            await _sessions.SetVerifyingAsync(sessionId, cancellationToken);
            await _objects.SetVerifyingAsync(fileObjectId, cancellationToken);
            string fullHash = await VerifyCoreAsync(
                fileObjectId,
                request.ExpectedLength,
                totalChunks,
                cancellationToken);
            if (request.ExpectedSha256 is not null &&
                !string.Equals(
                    request.ExpectedSha256,
                    fullHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new DatabaseFileException(
                    "file_integrity_failed",
                    "文件完整 SHA-256 与声明值不一致。");
            }

            await _objects.SetReadyAsync(
                fileObjectId,
                fullHash,
                totalChunks,
                cancellationToken);
            await _sessions.CompleteAsync(sessionId, cancellationToken);
            return new StoredFile
            {
                FileObjectId = fileObjectId,
                ObjectKey = objectKey,
                FileSize = request.ExpectedLength,
                Sha256 = fullHash,
                ChunkSize = _options.ChunkSizeBytes,
                TotalChunks = totalChunks,
            };
        }
        catch (OperationCanceledException)
        {
            await MarkFailedBestEffortAsync(fileObjectId, sessionId, "CANCELLED", "上传已取消。");
            throw;
        }
        catch
        {
            await MarkFailedBestEffortAsync(fileObjectId, sessionId, "FAILED", "文件上传或校验失败。");
            throw;
        }
    }

    public Task<FileObjectMetadata?> GetMetadataAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default) =>
        _objects.GetAsync(fileObjectId, cancellationToken);

    public async Task CopyToAsync(
        ulong fileObjectId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        FileObjectMetadata metadata = await RequireReadyAsync(
            fileObjectId,
            cancellationToken);
        CountingWriteStream counting = new(destination);
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await _chunks.CopyToAsync(
            fileObjectId,
            counting,
            hash,
            cancellationToken);
        if (counting.BytesWritten != metadata.FileSize)
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "文件分片字节总数与元数据不一致。");
        }

        string actualHash = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(metadata.Sha256) ||
            !string.Equals(
                actualHash,
                metadata.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "文件完整 SHA-256 校验失败。");
        }
    }

    public async Task<TemporaryFileLease> MaterializeTemporaryFileAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"wtb-{Guid.NewGuid():N}.docx");
        try
        {
            await using FileStream output = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyToAsync(fileObjectId, output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return new TemporaryFileLease(path);
        }
        catch
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    public async Task VerifyAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default)
    {
        FileObjectMetadata metadata = await RequireReadyAsync(
            fileObjectId,
            cancellationToken);
        string hash = await VerifyCoreAsync(
            fileObjectId,
            metadata.FileSize,
            metadata.TotalChunks,
            cancellationToken);
        if (!string.Equals(hash, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "文件完整 SHA-256 校验失败。");
        }
    }

    private async Task<string> VerifyCoreAsync(
        ulong fileObjectId,
        long expectedLength,
        int expectedChunks,
        CancellationToken cancellationToken)
    {
        (int count, long bytes, int minNo, int maxNo) =
            await _chunks.GetStatsAsync(fileObjectId, cancellationToken);
        if (count != expectedChunks ||
            bytes != expectedLength ||
            minNo != 0 ||
            maxNo != expectedChunks - 1)
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "文件分片缺失、序号不连续或字节总数不匹配。");
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await _chunks.CopyToAsync(
            fileObjectId,
            Stream.Null,
            hash,
            cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<FileObjectMetadata> RequireReadyAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        FileObjectMetadata metadata = await _objects.GetAsync(id, cancellationToken)
            ?? throw new DatabaseFileException(
                "database_file_not_found",
                $"找不到数据库文件：{id}。");
        if (metadata.DeletedAt is not null ||
            !string.Equals(metadata.ObjectStatus, "READY", StringComparison.Ordinal))
        {
            throw new DatabaseFileException(
                "database_file_not_ready",
                $"数据库文件 {id} 尚未就绪。");
        }

        return metadata;
    }

    private async Task MarkFailedBestEffortAsync(
        ulong fileId,
        ulong sessionId,
        string sessionStatus,
        string message)
    {
        try
        {
            await _sessions.FailAsync(sessionId, sessionStatus, message, CancellationToken.None);
        }
        catch
        {
        }

        try
        {
            await _objects.SetFailedAsync(fileId, CancellationToken.None);
        }
        catch
        {
        }
    }

    private static void ValidateRequest(Stream source, FileStoreRequest request)
    {
        if (!source.CanRead)
        {
            throw new DatabaseFileException("file_integrity_failed", "上传流不可读。");
        }

        if (request.ExpectedLength <= 0)
        {
            throw new InvalidTemplateFileException("上传文件不能为空。");
        }
    }

    private static async Task<int> ReadExactlyAsync(
        Stream source,
        byte[] buffer,
        int required,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < required)
        {
            int read = await source.ReadAsync(
                buffer.AsMemory(total, required - total),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private sealed class CountingWriteStream : Stream
    {
        private readonly Stream _inner;

        internal CountingWriteStream(Stream inner)
        {
            _inner = inner;
        }

        internal long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            BytesWritten += buffer.Length;
        }
    }
}

#pragma warning restore CS1591
