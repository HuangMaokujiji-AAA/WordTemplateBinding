using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 提供主文档段落枚举、上下文哈希和文本空格处理等共享能力。
/// </summary>
internal static class OpenXmlDocumentHelpers
{
    /// <summary>
    /// 主文档部件的稳定键。
    /// </summary>
    internal const string MainDocumentPartKey = "/word/document.xml";

    /// <summary>
    /// 枚举正文和表格单元格中的普通段落，同时排除文本框段落。
    /// </summary>
    /// <param name="body">主文档正文。</param>
    /// <returns>返回按文档顺序排列的段落列表。</returns>
    internal static IReadOnlyList<Paragraph> GetMainDocumentParagraphs(Body body)
    {
        return body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<TextBoxContent>().Any())
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 计算匹配值附近原始上下文的 SHA-256 哈希。
    /// </summary>
    /// <param name="fullText">段落拼接文本。</param>
    /// <param name="startOffset">匹配起始偏移。</param>
    /// <param name="length">匹配长度。</param>
    /// <param name="contextLength">匹配两侧保留的上下文长度。</param>
    /// <returns>返回小写十六进制哈希。</returns>
    internal static string ComputeContextHash(
        string fullText,
        int startOffset,
        int length,
        int contextLength)
    {
        int contextStart = Math.Max(0, startOffset - contextLength);
        int contextEnd = Math.Min(fullText.Length, startOffset + length + contextLength);
        string context = fullText[contextStart..contextEnd];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 校验定位信息是否属于第一阶段支持的主文档。
    /// </summary>
    /// <param name="locator">需要校验的定位信息。</param>
    /// <returns>返回定位是否属于主文档正文。</returns>
    internal static bool IsMainDocumentLocator(TextLocator locator) =>
        locator.PartKind == DocumentPartKind.MainDocument &&
        string.Equals(locator.PartKey, MainDocumentPartKey, StringComparison.Ordinal);

    /// <summary>
    /// 根据文本首尾空格决定是否设置 xml:space="preserve"。
    /// </summary>
    /// <param name="textNode">需要更新空格策略的 Text 节点。</param>
    internal static void PreserveBoundaryWhitespace(Text textNode)
    {
        string value = textNode.Text ?? string.Empty;
        if (value.Length > 0 &&
            (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            textNode.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
        }
    }
}
