using System.Security.Cryptography;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Infrastructure.Stores;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证文件存储抽象的分片、哈希和临时物化契约。
/// </summary>
public sealed class FileStorageTests
{
    /// <summary>
    /// 验证跨多个分片的内容可以按原顺序复制并通过完整哈希校验。
    /// </summary>
    [Fact]
    public async Task StoreAndCopy_MultipleChunks_PreservesBytesAndHash()
    {
        byte[] bytes = Enumerable.Range(0, 150_000)
            .Select(index => (byte)(index % 251))
            .ToArray();
        InMemoryFileStorageService storage = new(
            new DatabaseFileStorageOptions
            {
                ChunkSizeBytes = 64 * 1024,
            });
        string expectedHash = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        await using MemoryStream input = new(bytes, writable: false);

        StoredFile stored = await storage.StoreAsync(
            input,
            new FileStoreRequest
            {
                OriginalName = "sample.docx",
                MimeType =
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileExtension = "docx",
                ExpectedLength = bytes.Length,
                ExpectedSha256 = expectedHash,
            });

        Assert.Equal(3, stored.TotalChunks);
        Assert.Equal(expectedHash, stored.Sha256);
        await using MemoryStream output = new();
        await storage.CopyToAsync(stored.FileObjectId, output);
        Assert.Equal(bytes, output.ToArray());
        await storage.VerifyAsync(stored.FileObjectId);
        await using TemporaryFileLease lease =
            await storage.MaterializeTemporaryFileAsync(stored.FileObjectId);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(lease.Path));
    }
}
