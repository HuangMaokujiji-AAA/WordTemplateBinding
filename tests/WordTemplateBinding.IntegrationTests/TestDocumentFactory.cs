using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 为 API 集成测试创建最小 DOCX。
/// </summary>
internal static class TestDocumentFactory
{
    /// <summary>
    /// 创建包含指定段落文本的普通 DOCX。
    /// </summary>
    /// <param name="text">段落文本。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] Create(string text)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(
                        new Run(new Text(text)))));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 读取 DOCX 主文档正文文本。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回正文 InnerText。</returns>
    internal static string ReadBodyText(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }
}
