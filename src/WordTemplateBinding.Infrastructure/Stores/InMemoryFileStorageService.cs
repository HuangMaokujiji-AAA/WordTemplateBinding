#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Security.Cryptography;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.Stores;

public sealed class InMemoryFileStorageService : IFileStorageService
{
    private readonly ConcurrentDictionary<ulong, Entry> _files = new();
    private readonly DatabaseFileStorageOptions _options;
    private long _nextId;

    public InMemoryFileStorageService(DatabaseFileStorageOptions options)
    {
        _options = options;
    }

    public async Task<StoredFile> StoreAsync(
        Stream source,
        FileStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedLength <= 0)
        {
            throw new InvalidTemplateFileException("上传文件不能为空。");
        }

        ulong id = checked((ulong)Interlocked.Increment(ref _nextId));
        List<byte[]> chunks = new();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        while (received < request.ExpectedLength)
        {
            int expected = checked((int)Math.Min(
                _options.ChunkSizeBytes,
                request.ExpectedLength - received));
            byte[] chunk = new byte[expected];
            int offset = 0;
            while (offset < expected)
            {
                int read = await source.ReadAsync(
                    chunk.AsMemory(offset, expected - offset),
                    cancellationToken);
                if (read == 0)
                {
                    throw new DatabaseFileException(
                        "file_integrity_failed",
                        "上传流提前结束。");
                }

                offset += read;
            }

            hash.AppendData(chunk);
            chunks.Add(chunk);
            received += expected;
        }

        byte[] extra = new byte[1];
        if (await source.ReadAsync(extra, cancellationToken) != 0)
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "上传流包含额外字节。");
        }

        string fullHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (request.ExpectedSha256 is not null &&
            !string.Equals(request.ExpectedSha256, fullHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseFileException(
                "file_integrity_failed",
                "文件完整 SHA-256 与声明值不一致。");
        }

        string objectKey = $"memory/{Guid.NewGuid():N}";
        FileObjectMetadata metadata = new()
        {
            Id = id,
            ObjectKey = objectKey,
            OriginalName = request.OriginalName,
            FileExtension = request.FileExtension,
            MimeType = request.MimeType,
            FileSize = request.ExpectedLength,
            Sha256 = fullHash,
            ChunkSize = _options.ChunkSizeBytes,
            TotalChunks = chunks.Count,
            ObjectStatus = "READY",
            UploadCompletedAt = DateTimeOffset.UtcNow,
            DeletedAt = null,
        };
        _files[id] = new Entry(metadata, chunks.AsReadOnly());
        return new StoredFile
        {
            FileObjectId = id,
            ObjectKey = objectKey,
            FileSize = request.ExpectedLength,
            Sha256 = fullHash,
            ChunkSize = _options.ChunkSizeBytes,
            TotalChunks = chunks.Count,
        };
    }

    public Task<FileObjectMetadata?> GetMetadataAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files.TryGetValue(fileObjectId, out Entry? entry);
        FileObjectMetadata? metadata = entry is null
            ? null
            : entry.Metadata with { };
        return Task.FromResult(metadata);
    }

    public async Task CopyToAsync(
        ulong fileObjectId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        Entry entry = RequireReady(fileObjectId);
        foreach (byte[] chunk in entry.Chunks)
        {
            await destination.WriteAsync(chunk, cancellationToken);
        }
    }

    public async Task<TemporaryFileLease> MaterializeTemporaryFileAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(Path.GetTempPath(), $"wtb-{Guid.NewGuid():N}.docx");
        try
        {
            await using FileStream output = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await CopyToAsync(fileObjectId, output, cancellationToken);
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

    public Task VerifyAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry = RequireReady(fileObjectId);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] chunk in entry.Chunks)
        {
            hash.AppendData(chunk);
        }

        string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, entry.Metadata.Sha256, StringComparison.Ordinal))
        {
            throw new DatabaseFileException("file_integrity_failed", "文件完整 SHA-256 校验失败。");
        }

        return Task.CompletedTask;
    }

    private Entry RequireReady(ulong id)
    {
        if (!_files.TryGetValue(id, out Entry? entry))
        {
            throw new DatabaseFileException("database_file_not_found", $"找不到数据库文件：{id}。");
        }

        if (!string.Equals(entry.Metadata.ObjectStatus, "READY", StringComparison.Ordinal))
        {
            throw new DatabaseFileException("database_file_not_ready", $"数据库文件 {id} 尚未就绪。");
        }

        return entry;
    }

    private sealed record Entry(
        FileObjectMetadata Metadata,
        IReadOnlyList<byte[]> Chunks);
}

#pragma warning restore CS1591
