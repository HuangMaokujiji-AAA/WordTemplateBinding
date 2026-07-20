using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 使用长度前缀编码和 SHA-256 生成紧凑、稳定的定位标识。
/// </summary>
public sealed class LocatorIdGenerator : ILocatorIdGenerator
{
    /// <inheritdoc />
    public string Generate(string templateHash, TextLocator locator)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, templateHash);
        AppendInt32(hash, (int)locator.PartKind);
        AppendString(hash, locator.PartKey);
        AppendInt32(hash, locator.ParagraphIndex);
        AppendInt32(hash, locator.StartOffset);
        AppendInt32(hash, locator.Length);
        AppendInt32(hash, locator.OccurrenceIndex);
        AppendString(hash, locator.OriginalValue);
        AppendString(hash, locator.ContextHash);

        string base64 = Convert.ToBase64String(hash.GetHashAndReset());
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// 将字符串以 UTF-8 长度前缀形式追加到哈希输入，避免字段拼接歧义。
    /// </summary>
    /// <param name="hash">增量哈希实例。</param>
    /// <param name="value">需要追加的字符串。</param>
    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    /// <summary>
    /// 将 32 位整数以大端字节序追加到哈希输入。
    /// </summary>
    /// <param name="hash">增量哈希实例。</param>
    /// <param name="value">需要追加的整数。</param>
    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
