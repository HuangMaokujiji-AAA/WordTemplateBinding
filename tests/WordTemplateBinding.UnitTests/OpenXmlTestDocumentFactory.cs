using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;

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
    /// 创建只包含一个段落、并按输入声明为部分 Run 添加黄色高亮的 DOCX。
    /// </summary>
    /// <param name="runs">Run 文本及其是否使用黄色高亮。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateHighlightedParagraphDocument(
        params (string Text, bool IsHighlighted)[] runs)
    {
        Paragraph paragraph = new(runs.Select(run =>
            run.IsHighlighted
                ? new Run(
                    new RunProperties(
                        new Highlight { Val = HighlightColorValues.Yellow }),
                    new Text(run.Text))
                : new Run(new Text(run.Text))));
        return CreateDocument(paragraph);
    }

    /// <summary>
    /// 创建一个使用 w:shd 黄色填充标记文本的 DOCX。
    /// </summary>
    internal static byte[] CreateShadedParagraphDocument(string text)
    {
        Paragraph paragraph = new(
            new Run(
                new RunProperties(
                    new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Fill = "FFFF00",
                    }),
                new Text(text)));
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

    /// <summary>创建带上下文标题、表头和样例数据行的业务表格。</summary>
    internal static byte[] CreateBusinessTableDocument(
        string context,
        IReadOnlyList<string> headers,
        params IReadOnlyList<string>[] rows)
    {
        Table table = new();
        table.Append(new TableRow(headers.Select(value =>
            new TableCell(new Paragraph(new Run(new Text(value)))))));
        foreach (IReadOnlyList<string> row in rows)
        {
            table.Append(new TableRow(row.Select(value =>
                new TableCell(new Paragraph(new Run(new Text(value)))))));
        }

        return CreateDocument(
            new Paragraph(new Run(new Text(context))),
            table);
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
    /// 创建同时包含正文和默认页脚的 DOCX。
    /// </summary>
    /// <param name="bodyText">正文段落文本。</param>
    /// <param name="footerText">页脚段落文本。</param>
    /// <returns>返回 DOCX 字节。</returns>
    internal static byte[] CreateBodyAndFooterDocument(
        string bodyText,
        string footerText)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            FooterPart footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(
                new Paragraph(new Run(new Text(footerText))));
            string footerRelationshipId = mainPart.GetIdOfPart(footerPart);

            Body body = new(
                new Paragraph(new Run(new Text(bodyText))),
                new SectionProperties(
                    new FooterReference
                    {
                        Id = footerRelationshipId,
                        Type = HeaderFooterValues.Default,
                    }));
            mainPart.Document = new Document(body);
            footerPart.Footer.Save();
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 创建包含一个双系列柱状图缓存的最小 DOCX。
    /// </summary>
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
                            new A.GraphicData(
                                new C.ChartReference { Id = relationshipId })
                            {
                                Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                            }))
                    {
                        DistanceFromTop = 0U,
                        DistanceFromBottom = 0U,
                        DistanceFromLeft = 0U,
                        DistanceFromRight = 0U,
                    })))));

            C.BarChart barChart = new(
                new C.BarDirection { Val = C.BarDirectionValues.Column },
                new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                CreateBarSeries(0U, "你县", new[] { "四年级", "八年级" }, new[] { 543m, 505m }),
                CreateBarSeries(1U, "全省", new[] { "四年级", "八年级" }, new[] { 506m, 493m }));
            chartPart.ChartSpace = new C.ChartSpace(
                new C.Chart(
                    new C.AutoTitleDeleted { Val = true },
                    new C.PlotArea(new C.Layout(), barChart)));
            chartPart.ChartSpace.Save();
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 创建包含双系列原生雷达图的最小 DOCX。
    /// </summary>
    internal static byte[] CreateRadarChartDocument(
        string radarStyle = "marker",
        bool includeCategories = true,
        bool includeValueCache = true,
        bool includeAxisRange = true,
        bool seriesLengthMismatch = false,
        bool includeEmbeddedWorkbook = false)
    {
        string[] categories = { "指标A", "指标B", "指标C", "指标D", "指标E" };
        decimal[] schoolValues = { 82m, 91m, 76m, 68m, 88m };
        decimal[] provinceValues = { 75m, 80m, 72m, 70m, 79m };
        if (seriesLengthMismatch)
            provinceValues = provinceValues[..4];

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
                        new DW.EffectExtent(),
                        new DW.DocProperties { Id = 1U, Name = "Radar Chart 1" },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(
                                new C.ChartReference { Id = relationshipId })
                            {
                                Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                            })))))));

            C.RadarStyle style = new();
            style.SetAttribute(new OpenXmlAttribute("val", string.Empty, radarStyle));
            C.RadarChart radarChart = new(
                style,
                new C.VaryColors { Val = false },
                CreateRadarSeries(
                    0U,
                    "学校值",
                    categories,
                    schoolValues,
                    includeCategories,
                    includeValueCache,
                    C.MarkerStyleValues.Diamond),
                CreateRadarSeries(
                    1U,
                    "全省值",
                    categories,
                    provinceValues,
                    includeCategories,
                    includeValueCache,
                    C.MarkerStyleValues.Circle),
                new C.AxisId { Val = 1U },
                new C.AxisId { Val = 2U });

            C.Scaling valueScaling = new(new C.Orientation
            {
                Val = C.OrientationValues.MinMax,
            });
            if (includeAxisRange)
            {
                valueScaling.Append(
                    new C.MinAxisValue { Val = 0D },
                    new C.MaxAxisValue { Val = 100D });
            }

            C.CategoryAxis categoryAxis = new(
                new C.AxisId { Val = 1U },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                new C.CrossingAxis { Val = 2U });
            C.ValueAxis valueAxis = new(
                new C.AxisId { Val = 2U },
                valueScaling,
                new C.AxisPosition { Val = C.AxisPositionValues.Left },
                new C.CrossingAxis { Val = 1U });

            chartPart.ChartSpace = new C.ChartSpace(
                new C.Chart(
                    new C.Title(
                        new C.ChartText(
                            new C.RichText(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.Text("雷达图标题")))))),
                    new C.PlotArea(
                        new C.Layout(),
                        radarChart,
                        categoryAxis,
                        valueAxis),
                    new C.Legend(
                        new C.LegendPosition { Val = C.LegendPositionValues.Bottom })));
            if (includeEmbeddedWorkbook)
            {
                EmbeddedPackagePart workbookPart = chartPart.AddEmbeddedPackagePart(
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                string workbookRelationshipId = chartPart.GetIdOfPart(workbookPart);
                WriteRadarWorkbook(
                    workbookPart,
                    categories,
                    schoolValues,
                    provinceValues);
                chartPart.ChartSpace.Append(new C.ExternalData
                {
                    Id = workbookRelationshipId,
                });
            }
            chartPart.ChartSpace.Save();
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 读取第一个图表的分类和系列数值缓存。
    /// </summary>
    internal static (
        IReadOnlyList<string> Categories,
        IReadOnlyList<IReadOnlyList<decimal>> SeriesValues) ReadFirstChartData(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        ChartPart chartPart = document.MainDocumentPart?.ChartParts.First()
            ?? throw new InvalidOperationException("测试文档不包含图表。");
        C.BarChartSeries[] series = chartPart.ChartSpace
            .Descendants<C.BarChartSeries>()
            .ToArray();
        IReadOnlyList<string> categories = series[0]
            .GetFirstChild<C.CategoryAxisData>()!
            .Descendants<C.StringPoint>()
            .OrderBy(point => point.Index?.Value)
            .Select(point => point.NumericValue?.Text ?? string.Empty)
            .ToList()
            .AsReadOnly();
        IReadOnlyList<IReadOnlyList<decimal>> values = series
            .Select(item => (IReadOnlyList<decimal>)item
                .Descendants<C.NumericPoint>()
                .OrderBy(point => point.Index?.Value)
                .Select(point => decimal.Parse(
                    point.NumericValue?.Text ?? "0",
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToList()
                .AsReadOnly())
            .ToList()
            .AsReadOnly();
        return (categories, values);
    }

    private static C.BarChartSeries CreateBarSeries(
        uint index,
        string name,
        IReadOnlyList<string> categories,
        IReadOnlyList<decimal> values)
    {
        C.StringCache nameCache = new(new C.PointCount { Val = 1U });
        nameCache.Append(new C.StringPoint(
            new C.NumericValue(name))
        {
            Index = 0U,
        });
        C.StringCache categoryCache = new(new C.PointCount { Val = (uint)categories.Count });
        for (int pointIndex = 0; pointIndex < categories.Count; pointIndex++)
        {
            categoryCache.Append(new C.StringPoint(
                new C.NumericValue(categories[pointIndex]))
            {
                Index = (uint)pointIndex,
            });
        }

        C.NumberingCache valueCache = new(
            new C.FormatCode("General"),
            new C.PointCount { Val = (uint)values.Count });
        for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
        {
            valueCache.Append(new C.NumericPoint(
                new C.NumericValue(values[pointIndex].ToString(
                    System.Globalization.CultureInfo.InvariantCulture)))
            {
                Index = (uint)pointIndex,
            });
        }

        return new C.BarChartSeries(
            new C.Index { Val = index },
            new C.Order { Val = index },
            new C.SeriesText(new C.StringReference(
                new C.Formula($"Sheet1!${(char)('B' + index)}$1"),
                nameCache)),
            new C.CategoryAxisData(new C.StringReference(
                new C.Formula("Sheet1!$A$2:$A$3"),
                categoryCache)),
            new C.Values(new C.NumberReference(
                new C.Formula($"Sheet1!${(char)('B' + index)}$2:${(char)('B' + index)}$3"),
                valueCache)));
    }

    private static C.RadarChartSeries CreateRadarSeries(
        uint index,
        string name,
        IReadOnlyList<string> categories,
        IReadOnlyList<decimal> values,
        bool includeCategories,
        bool includeValueCache,
        C.MarkerStyleValues markerStyle)
    {
        C.StringCache nameCache = new(new C.PointCount { Val = 1U });
        nameCache.Append(new C.StringPoint(new C.NumericValue(name)) { Index = 0U });

        C.StringReference categoryReference = new(
            new C.Formula($"RadarData!$B$1:${(char)('A' + categories.Count)}$1"));
        if (includeCategories)
        {
            C.StringCache categoryCache = new(new C.PointCount { Val = (uint)categories.Count });
            for (int pointIndex = 0; pointIndex < categories.Count; pointIndex++)
            {
                categoryCache.Append(new C.StringPoint(
                    new C.NumericValue(categories[pointIndex]))
                {
                    Index = (uint)pointIndex,
                });
            }
            categoryReference.Append(categoryCache);
        }

        C.NumberReference valueReference = new(
            new C.Formula(
                $"RadarData!$B${index + 2}:${(char)('A' + categories.Count)}${index + 2}"));
        if (includeValueCache)
        {
            C.NumberingCache valueCache = new(
                new C.FormatCode("General"),
                new C.PointCount { Val = (uint)values.Count });
            for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
            {
                valueCache.Append(new C.NumericPoint(
                    new C.NumericValue(values[pointIndex].ToString(
                        System.Globalization.CultureInfo.InvariantCulture)))
                {
                    Index = (uint)pointIndex,
                });
            }
            valueReference.Append(valueCache);
        }

        C.ChartShapeProperties shape = new(
            new A.SolidFill(new A.RgbColorModelHex
            {
                Val = index == 0U ? "4472C4" : "ED7D31",
            }),
            new A.Outline(
                new A.SolidFill(new A.RgbColorModelHex
                {
                    Val = index == 0U ? "4472C4" : "ED7D31",
                })));

        return new C.RadarChartSeries(
            new C.Index { Val = index },
            new C.Order { Val = index },
            new C.SeriesText(new C.StringReference(
                new C.Formula($"RadarData!$A${index + 2}"),
                nameCache)),
            shape,
            new C.Marker(new C.Symbol { Val = markerStyle }, new C.Size { Val = 6 }),
            new C.CategoryAxisData(categoryReference),
            new C.Values(valueReference));
    }

    private static void WriteRadarWorkbook(
        EmbeddedPackagePart packagePart,
        IReadOnlyList<string> categories,
        IReadOnlyList<decimal> schoolValues,
        IReadOnlyList<decimal> provinceValues)
    {
        using Stream workbookStream = packagePart.GetStream(FileMode.Create, FileAccess.ReadWrite);
        using SpreadsheetDocument spreadsheet = SpreadsheetDocument.Create(
            workbookStream,
            SpreadsheetDocumentType.Workbook,
            true);
        WorkbookPart workbookPart = spreadsheet.AddWorkbookPart();
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        S.SheetData sheetData = new();
        worksheetPart.Worksheet = new S.Worksheet(sheetData);

        S.Row header = new() { RowIndex = 1U };
        for (int index = 0; index < categories.Count; index++)
        {
            header.Append(CreateSpreadsheetCell(
                $"{(char)('B' + index)}1",
                categories[index],
                S.CellValues.String));
        }
        sheetData.Append(header);

        sheetData.Append(CreateRadarWorkbookRow(2U, "学校值", schoolValues));
        sheetData.Append(CreateRadarWorkbookRow(3U, "全省值", provinceValues));

        string worksheetRelationshipId = workbookPart.GetIdOfPart(worksheetPart);
        workbookPart.Workbook = new S.Workbook(
            new S.Sheets(
                new S.Sheet
                {
                    Id = worksheetRelationshipId,
                    SheetId = 1U,
                    Name = "RadarData",
                }));
        worksheetPart.Worksheet.Save();
        workbookPart.Workbook.Save();
    }

    private static S.Row CreateRadarWorkbookRow(
        uint rowIndex,
        string name,
        IReadOnlyList<decimal> values)
    {
        S.Row row = new() { RowIndex = rowIndex };
        row.Append(CreateSpreadsheetCell($"A{rowIndex}", name, S.CellValues.String));
        for (int index = 0; index < values.Count; index++)
        {
            row.Append(CreateSpreadsheetCell(
                $"{(char)('B' + index)}{rowIndex}",
                values[index].ToString(System.Globalization.CultureInfo.InvariantCulture),
                S.CellValues.Number));
        }
        return row;
    }

    private static S.Cell CreateSpreadsheetCell(
        string reference,
        string value,
        S.CellValues type) => new()
    {
        CellReference = reference,
        DataType = type,
        CellValue = new S.CellValue(value),
    };

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
    /// 读取 DOCX 中全部页脚的拼接文本。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回按部件 URI 排序后的页脚文本。</returns>
    internal static string ReadFooterText(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        return string.Join(
            "\n",
            document.MainDocumentPart?.FooterParts
                .OrderBy(part => part.Uri.OriginalString, StringComparer.Ordinal)
                .Select(part => part.Footer?.InnerText ?? string.Empty)
                ?? Array.Empty<string>());
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

    /// <summary>读取首个表格的全部单元格文本。</summary>
    internal static IReadOnlyList<IReadOnlyList<string>> ReadFirstTableRows(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        Table table = document.MainDocumentPart?.Document?.Body?.Descendants<Table>().First()
            ?? throw new InvalidOperationException("测试文档不包含表格。");
        return table.Elements<TableRow>()
            .Select(row => (IReadOnlyList<string>)row.Elements<TableCell>()
                .Select(cell => cell.InnerText)
                .ToList()
                .AsReadOnly())
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 读取 DOCX 主文档中的全部 Run。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回全部 Run 的独立克隆。</returns>
    internal static IReadOnlyList<Run> ReadBodyRuns(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        Body? body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Array.Empty<Run>();
        }

        return body
            .Descendants<Run>()
            .Select(run => (Run)run.CloneNode(true))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 读取本系统绑定 Manifest XML。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回包含固定命名空间的清单 XML。</returns>
    internal static string ReadBindingManifest(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        CustomXmlPart part = document.MainDocumentPart?.CustomXmlParts.First(item =>
            ReadPartText(item).Contains(
                "urn:word-template-binding:bindings:v1",
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("测试文档不包含绑定 Manifest。");
        return ReadPartText(part);
    }

    /// <summary>
    /// 读取第一个图表部件的原始 XML。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回图表 XML。</returns>
    internal static string ReadFirstChartXml(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        ChartPart part = document.MainDocumentPart?.ChartParts.First()
            ?? throw new InvalidOperationException("测试文档不包含图表。");
        return ReadPartText(part);
    }

    /// <summary>
    /// 向 DOCX 添加任意自定义 XML 部件。
    /// </summary>
    /// <param name="bytes">原始 DOCX 字节。</param>
    /// <param name="xml">自定义 XML 文本。</param>
    /// <returns>返回修改后的 DOCX 字节。</returns>
    internal static byte[] AddCustomXmlPart(byte[] bytes, string xml)
    {
        using MemoryStream stream = new();
        stream.Write(bytes);
        stream.Position = 0;
        using (WordprocessingDocument document = WordprocessingDocument.Open(stream, true))
        {
            MainDocumentPart mainPart = document.MainDocumentPart
                ?? throw new InvalidOperationException("测试文档缺少主文档部件。");
            CustomXmlPart part = mainPart.AddCustomXmlPart("application/xml");
            using Stream output = part.GetStream(FileMode.Create, FileAccess.Write);
            using StreamWriter writer = new(output, new UTF8Encoding(false), leaveOpen: false);
            writer.Write(xml);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 读取 DOCX 中全部自定义 XML 部件。
    /// </summary>
    /// <param name="bytes">DOCX 字节。</param>
    /// <returns>返回自定义 XML 文本列表。</returns>
    internal static IReadOnlyList<string> ReadAllCustomXmlParts(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        if (document.MainDocumentPart is null)
        {
            return Array.Empty<string>();
        }

        return document.MainDocumentPart.CustomXmlParts
            .Select(ReadPartText)
            .ToList()
            .AsReadOnly();
    }

    private static string ReadPartText(OpenXmlPart part)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using StreamReader reader = new(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
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
