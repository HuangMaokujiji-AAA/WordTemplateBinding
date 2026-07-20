using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 为单元测试程序化创建可重复的 DOCX 文档。
/// </summary>
internal static class OpenXmlTestDocumentFactory
{
    /// <summary>
    /// 创建只包含一个段落且每个输入字符串位于独立 Run 中的 DOCX。
    /// </summary>
    /// <param name="runTexts">按顺序写入的 Run 文本。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateParagraphDocument(params string[] runTexts)
    {
        Paragraph paragraph = new(runTexts.Select(text => new Run(new Text(text))));
        return CreateDocument(paragraph);
    }

    /// <summary>
    /// 创建包含多个纯文本段落的 DOCX。
    /// </summary>
    /// <param name="paragraphTexts">段落文本。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateMultipleParagraphDocument(params string[] paragraphTexts)
    {
        return CreateDocument(paragraphTexts.Select(
            text => new Paragraph(new Run(new Text(text)))).ToArray());
    }

    /// <summary>
    /// 创建一个在表格单元格中包含模拟小数的 DOCX。
    /// </summary>
    /// <param name="text">单元格文本。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateTableDocument(string text)
    {
        Table table = new(
            new TableRow(
                new TableCell(
                    new Paragraph(new Run(new Text(text))))));
        return CreateDocument(table);
    }

    /// <summary>
    /// 创建跨三个 Run 且首个 Run 包含字体、字号、粗体、斜体和颜色的 DOCX。
    /// </summary>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateFormattedSplitDocument()
    {
        RunProperties properties = new(
            new RunFonts { Ascii = "Arial", HighAnsi = "Arial" },
            new Bold(),
            new Italic(),
            new Color { Val = "C00000" },
            new FontSize { Val = "28" });
        Paragraph paragraph = new(
            new Run(properties, new Text("88")),
            new Run(new Text(".")),
            new Run(new Text("5")));
        return CreateDocument(paragraph);
    }

    /// <summary>
    /// 读取 DOCX 主文档正文的可见拼接文本。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回正文 InnerText。</returns>
    internal static string ReadBodyText(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    /// <summary>
    /// 读取 DOCX 主文档中的第一个 Run。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回第一个 Run 的克隆。</returns>
    internal static Run ReadFirstRun(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        Run run = document.MainDocumentPart?.Document?.Body?.Descendants<Run>().First()
            ?? throw new InvalidOperationException("测试文档不包含 Run。");
        return (Run)run.CloneNode(true);
    }

    /// <summary>
    /// 创建包含指定正文元素的最小普通 DOCX。
    /// </summary>
    /// <param name="elements">正文子元素。</param>
    /// <returns>返回 DOCX 字节。</returns>
    private static byte[] CreateDocument(params OpenXmlElement[] elements)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            Body body = new();
            body.Append(elements.Select(element => element.CloneNode(true)));
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
