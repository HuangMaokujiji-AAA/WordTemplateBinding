using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml.Charts;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 从主文档关系和 ChartPart 缓存中提取可绑定图表元数据。
/// </summary>
internal static class OpenXmlChartReader
{
    private static readonly IReadOnlyDictionary<string, string> ChartTypeNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["barChart"] = "bar",
            ["bar3DChart"] = "bar3D",
            ["lineChart"] = "line",
            ["line3DChart"] = "line3D",
            ["pieChart"] = "pie",
            ["pie3DChart"] = "pie3D",
            ["doughnutChart"] = "doughnut",
            ["areaChart"] = "area",
            ["area3DChart"] = "area3D",
            ["scatterChart"] = "scatter",
            ["radarChart"] = "radar",
            ["bubbleChart"] = "bubble",
            ["surfaceChart"] = "surface",
            ["surface3DChart"] = "surface3D",
            ["stockChart"] = "stock",
        };

    /// <summary>
    /// 按主文档出现顺序读取图表，并为每个唯一 ChartPart 生成稳定定位。
    /// </summary>
    internal static IReadOnlyList<ChartTemplateItem> Read(
        MainDocumentPart mainPart,
        string contentHash,
        ILocatorIdGenerator locatorIdGenerator)
    {
        List<ChartTemplateItem> charts = new();
        HashSet<string> visitedParts = new(StringComparer.Ordinal);
        IReadOnlyList<ChartReference> references = mainPart.Document
            .Descendants<ChartReference>()
            .ToList()
            .AsReadOnly();

        for (int documentOrder = 0; documentOrder < references.Count; documentOrder++)
        {
            ChartReference reference = references[documentOrder];
            string relationshipId = reference.Id?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                mainPart.GetPartById(relationshipId) is not ChartPart chartPart)
            {
                continue;
            }

            string partKey = chartPart.Uri.OriginalString;
            if (!visitedParts.Add(partKey) || chartPart.ChartSpace is null)
            {
                continue;
            }

            ChartLocator locator = new()
            {
                PartKey = partKey,
                RelationshipId = relationshipId,
                DocumentOrder = documentOrder,
            };
            charts.Add(ReadChart(
                chartPart.ChartSpace,
                chartPart,
                locator,
                locatorIdGenerator.Generate(contentHash, locator),
                charts.Count,
                ReadExternalTitle(reference)));
        }

        return charts.AsReadOnly();
    }

    private static ChartTemplateItem ReadChart(
        ChartSpace chartSpace,
        ChartPart chartPart,
        ChartLocator locator,
        string locatorId,
        int chartIndex,
        string? externalTitle)
    {
        OpenXmlElement? chart = FindFirst(chartSpace, "chart");
        OpenXmlElement? plotArea = chart is null ? null : FindChild(chart, "plotArea");
        List<OpenXmlElement> chartTypeElements = plotArea?.ChildElements
            .Where(element => ChartTypeNames.ContainsKey(element.LocalName))
            .ToList() ?? new List<OpenXmlElement>();
        string chartType = chartTypeElements.Count switch
        {
            0 => "unsupported",
            > 1 => "combo",
            _ => ChartTypeNames[chartTypeElements[0].LocalName],
        };

        List<OpenXmlElement> seriesElements = chartTypeElements
            .SelectMany(element => element.ChildElements.Where(child => child.LocalName == "ser"))
            .ToList();
        List<ChartSeriesTemplate> series = new(seriesElements.Count);

        for (int seriesIndex = 0; seriesIndex < seriesElements.Count; seriesIndex++)
        {
            OpenXmlElement seriesElement = seriesElements[seriesIndex];
            string name = ReadSeriesName(seriesElement, seriesIndex);
            IReadOnlyList<decimal?> values = ReadNumericValues(
                FindChild(seriesElement, "val") ?? FindChild(seriesElement, "yVal"));
            series.Add(new ChartSeriesTemplate
            {
                SeriesIndex = seriesIndex,
                Name = name,
                Values = values,
            });
        }

        IReadOnlyList<string> categories = seriesElements.Count == 0
            ? Array.Empty<string>()
            : ReadCategoryValues(
                FindChild(seriesElements[0], "cat") ?? FindChild(seriesElements[0], "xVal"));
        string title = externalTitle ?? ReadChartTitle(chart) ?? $"图表 {chartIndex + 1}";
        bool isBindable = series.Count > 0 && series.All(item => item.Values.Count > 0);

        // Build deep analysis snapshot (best-effort; partial failures do not block)
        ChartAnalysisSnapshot? analysis = null;
        try
        {
            analysis = OpenXmlWordChartAnalyzer.Analyze(
                chartPart, locator, locatorId, externalTitle, chartIndex);
        }
        catch
        {
            // Analysis is best-effort; leave as null when ChartPart cannot be parsed
        }

        // Build slim data definition for binding
        ChartTemplateItem tempItem = new()
        {
            LocatorId = locatorId,
            Locator = locator,
            ChartType = chartType,
            Title = title,
            Categories = categories,
            Series = series.AsReadOnly(),
            IsBindable = isBindable,
            IsBound = false,
            BoundDataPath = null,
            Analysis = analysis,
        };
        ChartDataDefinition? dataDef = null;
        try
        {
            dataDef = OpenXmlChartDataReader.Read(chartPart, tempItem);
        }
        catch
        {
            // Best-effort
        }

        return tempItem with { DataDefinition = dataDef };
    }

    private static string ReadSeriesName(OpenXmlElement series, int seriesIndex)
    {
        OpenXmlElement? text = FindChild(series, "tx");
        string? value = text is null
            ? null
            : Descendants(text)
                .FirstOrDefault(element => element.LocalName == "v")
                ?.InnerText
                .Trim();
        return string.IsNullOrWhiteSpace(value) ? $"系列 {seriesIndex + 1}" : value;
    }

    private static string? ReadChartTitle(OpenXmlElement? chart)
    {
        OpenXmlElement? title = chart is null ? null : FindChild(chart, "title");
        if (title is null) return null;

        string value = string.Concat(
                Descendants(title)
                    .Where(element => element.LocalName == "t")
                    .Select(element => element.InnerText))
            .Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadExternalTitle(ChartReference reference)
    {
        Paragraph? chartParagraph = reference.Ancestors<Paragraph>().FirstOrDefault();
        if (chartParagraph is null) return null;

        if (chartParagraph.ParagraphProperties?.KeepNext is not null)
        {
            string nextText = chartParagraph.NextSibling<Paragraph>()?.InnerText.Trim()
                ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(nextText)) return nextText;
        }

        Paragraph? previous = chartParagraph.PreviousSibling<Paragraph>();
        if (previous?.ParagraphProperties?.KeepNext is not null)
        {
            string previousText = previous.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(previousText)) return previousText;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadCategoryValues(OpenXmlElement? categoryContainer)
    {
        if (categoryContainer is null) return Array.Empty<string>();

        OpenXmlElement? multiLevelCache = Descendants(categoryContainer)
            .FirstOrDefault(element => element.LocalName == "multiLvlStrCache");
        OpenXmlElement source = multiLevelCache is null
            ? categoryContainer
            : Descendants(multiLevelCache)
                .LastOrDefault(element => element.LocalName == "lvl") ?? multiLevelCache;

        return ReadPointValues(source)
            .Select(value => value ?? string.Empty)
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<decimal?> ReadNumericValues(OpenXmlElement? valueContainer)
    {
        if (valueContainer is null) return Array.Empty<decimal?>();

        return ReadPointValues(valueContainer)
            .Select(value => decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal parsed)
                    ? (decimal?)parsed
                    : null)
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<string?> ReadPointValues(OpenXmlElement container)
    {
        return Descendants(container)
            .Where(element => element.LocalName == "pt")
            .Select(point => new
            {
                Index = uint.TryParse(
                    point.GetAttribute("idx", string.Empty).Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint index)
                        ? index
                        : uint.MaxValue,
                Value = point.ChildElements
                    .FirstOrDefault(element => element.LocalName == "v")
                    ?.InnerText,
            })
            .OrderBy(point => point.Index)
            .Select(point => point.Value)
            .ToList()
            .AsReadOnly();
    }

    private static OpenXmlElement? FindChild(OpenXmlElement element, string localName) =>
        element.ChildElements.FirstOrDefault(child => child.LocalName == localName);

    private static OpenXmlElement? FindFirst(OpenXmlElement element, string localName) =>
        Descendants(element).FirstOrDefault(child => child.LocalName == localName);

    private static IEnumerable<OpenXmlElement> Descendants(OpenXmlElement element)
    {
        foreach (OpenXmlElement child in element.ChildElements)
        {
            yield return child;
            foreach (OpenXmlElement descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
