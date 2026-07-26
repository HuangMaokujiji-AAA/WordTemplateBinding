using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

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

    /// <summary>创建包含多个独立段落的普通 DOCX。</summary>
    internal static byte[] CreateParagraphs(params string[] texts)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                texts.Select(text =>
                    new Paragraph(new Run(new Text(text))))));
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

    /// <summary>
    /// 创建包含一个双系列柱状图缓存的 DOCX。
    /// </summary>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateChartDocument()
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            ChartPart chartPart = mainPart.AddNewPart<ChartPart>();
            string relationshipId = mainPart.GetIdOfPart(chartPart);
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Drawing(
                    new DW.Inline(
                        new DW.Extent { Cx = 5715000L, Cy = 3429000L },
                        new DW.EffectExtent
                        {
                            LeftEdge = 0L,
                            TopEdge = 0L,
                            RightEdge = 0L,
                            BottomEdge = 0L,
                        },
                        new DW.DocProperties { Id = 1U, Name = "Chart 1" },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(new C.ChartReference { Id = relationshipId })
                            {
                                Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                            })))))));
            C.BarChart chart = new(
                new C.BarDirection { Val = C.BarDirectionValues.Column },
                new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                CreateSeries(0U, "你县", new[] { 543m, 505m }),
                CreateSeries(1U, "全省", new[] { 506m, 493m }));
            chartPart.ChartSpace = new C.ChartSpace(
                new C.Chart(new C.AutoTitleDeleted { Val = true }, new C.PlotArea(new C.Layout(), chart)));
            chartPart.ChartSpace.Save();
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 读取第一个图表部件的 XML。
    /// </summary>
    internal static string ReadFirstChartXml(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        ChartPart part = document.MainDocumentPart?.ChartParts.First()
            ?? throw new InvalidOperationException("文档不包含图表。");
        return ReadPartText(part);
    }

    /// <summary>
    /// 读取绑定 Manifest XML。
    /// </summary>
    internal static string ReadBindingManifest(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return document.MainDocumentPart?.CustomXmlParts
            .Select(ReadPartText)
            .First(text => text.Contains(
                "urn:word-template-binding:bindings:v1",
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("文档不包含绑定 Manifest。");
    }

    /// <summary>
    /// 读取第一个图表各系列的数值缓存。
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<decimal>> ReadChartValues(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        ChartPart part = document.MainDocumentPart?.ChartParts.First()
            ?? throw new InvalidOperationException("文档不包含图表。");
        return part.ChartSpace.Descendants<C.BarChartSeries>()
            .Select(series => (IReadOnlyList<decimal>)series.Descendants<C.NumericPoint>()
                .OrderBy(point => point.Index?.Value)
                .Select(point => decimal.Parse(
                    point.NumericValue?.Text ?? "0",
                    CultureInfo.InvariantCulture))
                .ToList()
                .AsReadOnly())
            .ToList()
            .AsReadOnly();
    }

    private static C.BarChartSeries CreateSeries(
        uint index,
        string name,
        IReadOnlyList<decimal> values)
    {
        C.StringCache nameCache = new(new C.PointCount { Val = 1U });
        nameCache.Append(new C.StringPoint(new C.NumericValue(name)) { Index = 0U });
        C.StringCache categories = new(new C.PointCount { Val = 2U });
        categories.Append(new C.StringPoint(new C.NumericValue("四年级")) { Index = 0U });
        categories.Append(new C.StringPoint(new C.NumericValue("八年级")) { Index = 1U });
        C.NumberingCache numbers = new(
            new C.FormatCode("General"),
            new C.PointCount { Val = (uint)values.Count });
        for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
        {
            numbers.Append(new C.NumericPoint(
                new C.NumericValue(values[pointIndex].ToString(CultureInfo.InvariantCulture)))
            {
                Index = (uint)pointIndex,
            });
        }

        return new C.BarChartSeries(
            new C.Index { Val = index },
            new C.Order { Val = index },
            new C.SeriesText(new C.StringReference(new C.Formula("Sheet1!$B$1"), nameCache)),
            new C.CategoryAxisData(new C.StringReference(
                new C.Formula("Sheet1!$A$2:$A$3"),
                categories)),
            new C.Values(new C.NumberReference(new C.Formula("Sheet1!$B$2:$B$3"), numbers)));
    }

    private static string ReadPartText(OpenXmlPart part)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using StreamReader reader = new(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
}
