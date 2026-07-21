using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 将集合数据写入 Word ChartPart 的分类和系列缓存。
/// </summary>
internal static class OpenXmlChartWriter
{
    private static readonly HashSet<string> ChartElementNames = new(StringComparer.Ordinal)
    {
        "barChart", "bar3DChart", "lineChart", "line3DChart", "pieChart",
        "pie3DChart", "doughnutChart", "areaChart", "area3DChart", "scatterChart",
        "radarChart", "bubbleChart", "surfaceChart", "surface3DChart", "stockChart",
    };

    internal static void Write(
        MainDocumentPart mainPart,
        ChartTemplateItem chartItem,
        object? value)
    {
        ChartPart? chartPart = mainPart.ChartParts.FirstOrDefault(part =>
            string.Equals(
                part.Uri.OriginalString,
                chartItem.Locator.PartKey,
                StringComparison.Ordinal));
        if (chartPart?.ChartSpace is null)
        {
            throw new LocatorNotFoundException(chartItem.LocatorId);
        }

        ChartDataSet dataSet = ChartDataSetParser.Parse(value);
        IReadOnlyList<OpenXmlElement> seriesElements = FindSeries(chartPart.ChartSpace);
        if (seriesElements.Count == 0)
        {
            throw new ReportRenderingException($"图表 {chartItem.Title} 没有可写的数据系列。");
        }

        IReadOnlyList<ChartDataSeries> mappedSeries = MapSeries(
            chartItem,
            dataSet,
            seriesElements.Count);
        for (int index = 0; index < seriesElements.Count; index++)
        {
            OpenXmlElement series = seriesElements[index];
            ChartDataSeries source = mappedSeries[index];
            UpdateSeriesName(series, source.Name);
            UpdateCategories(series, dataSet.Categories);
            UpdateNumbers(
                FindChild(series, "val") ?? FindChild(series, "yVal"),
                source.Values,
                chartItem.Title);
        }

        foreach (OpenXmlElement autoUpdate in Descendants(chartPart.ChartSpace)
                     .Where(element => element.LocalName == "autoUpdate"))
        {
            autoUpdate.SetAttribute(new OpenXmlAttribute("val", string.Empty, "0"));
        }

        chartPart.ChartSpace.Save();
    }

    private static IReadOnlyList<ChartDataSeries> MapSeries(
        ChartTemplateItem chartItem,
        ChartDataSet dataSet,
        int requiredCount)
    {
        List<ChartDataSeries> result = new(requiredCount);
        HashSet<int> used = new();
        for (int index = 0; index < requiredCount; index++)
        {
            string templateName = index < chartItem.Series.Count
                ? chartItem.Series[index].Name
                : string.Empty;
            int matchedIndex = FindSeriesIndex(dataSet.Series, templateName, used);
            if (matchedIndex < 0)
            {
                matchedIndex = Enumerable.Range(0, dataSet.Series.Count)
                    .FirstOrDefault(candidate => !used.Contains(candidate), -1);
            }

            if (matchedIndex < 0)
            {
                throw new FormatException(
                    $"集合只有 {dataSet.Series.Count} 个数值列，图表 {chartItem.Title} 需要 {requiredCount} 个系列。");
            }

            used.Add(matchedIndex);
            result.Add(dataSet.Series[matchedIndex]);
        }

        return result.AsReadOnly();
    }

    private static int FindSeriesIndex(
        IReadOnlyList<ChartDataSeries> series,
        string templateName,
        IReadOnlySet<int> used)
    {
        for (int index = 0; index < series.Count; index++)
        {
            if (!used.Contains(index) && string.Equals(
                    NormalizeName(series[index].Name),
                    NormalizeName(templateName),
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeName(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static IReadOnlyList<OpenXmlElement> FindSeries(ChartSpace chartSpace)
    {
        OpenXmlElement? chart = Descendants(chartSpace)
            .FirstOrDefault(element => element.LocalName == "chart");
        OpenXmlElement? plotArea = chart is null ? null : FindChild(chart, "plotArea");
        if (plotArea is null)
        {
            return Array.Empty<OpenXmlElement>();
        }

        return plotArea.ChildElements
            .Where(element => ChartElementNames.Contains(element.LocalName))
            .SelectMany(element => element.ChildElements.Where(child => child.LocalName == "ser"))
            .ToList()
            .AsReadOnly();
    }

    private static void UpdateSeriesName(OpenXmlElement series, string name)
    {
        OpenXmlElement? text = FindChild(series, "tx");
        StringCache? cache = text?.Descendants<StringCache>().FirstOrDefault();
        StringLiteral? literal = text?.Descendants<StringLiteral>().FirstOrDefault();
        if (cache is not null)
        {
            ReplaceStringPoints(cache, new[] { name });
        }
        else if (literal is not null)
        {
            ReplaceStringPoints(literal, new[] { name });
        }
    }

    private static void UpdateCategories(OpenXmlElement series, IReadOnlyList<string> categories)
    {
        OpenXmlElement? container = FindChild(series, "cat") ?? FindChild(series, "xVal");
        if (container is null) return;

        StringCache? cache = container.Descendants<StringCache>().FirstOrDefault();
        StringLiteral? literal = container.Descendants<StringLiteral>().FirstOrDefault();
        MultiLevelStringCache? multiLevelCache =
            container.Descendants<MultiLevelStringCache>().FirstOrDefault();
        if (cache is not null)
        {
            ReplaceStringPoints(cache, categories);
        }
        else if (literal is not null)
        {
            ReplaceStringPoints(literal, categories);
        }
        else if (multiLevelCache is not null)
        {
            ReplaceMultiLevelPoints(multiLevelCache, categories);
        }
        else if (container.Descendants<NumberingCache>().FirstOrDefault() is NumberingCache numberCache)
        {
            ReplaceNumericPoints(numberCache, categories.Select(ParseCategoryNumber).ToList());
        }
        else if (container.Descendants<NumberLiteral>().FirstOrDefault() is NumberLiteral numberLiteral)
        {
            ReplaceNumericPoints(numberLiteral, categories.Select(ParseCategoryNumber).ToList());
        }
    }

    private static void UpdateNumbers(
        OpenXmlElement? container,
        IReadOnlyList<decimal?> values,
        string chartTitle)
    {
        NumberingCache? cache = container?.Descendants<NumberingCache>().FirstOrDefault();
        NumberLiteral? literal = container?.Descendants<NumberLiteral>().FirstOrDefault();
        if (cache is not null)
        {
            ReplaceNumericPoints(cache, values);
            return;
        }

        if (literal is not null)
        {
            ReplaceNumericPoints(literal, values);
            return;
        }

        throw new ReportRenderingException($"图表 {chartTitle} 的系列没有可写数值缓存。");
    }

    private static void ReplaceStringPoints(
        OpenXmlCompositeElement parent,
        IReadOnlyList<string> values)
    {
        parent.RemoveAllChildren<StringPoint>();
        PointCount pointCount = EnsurePointCount(parent, values.Count);
        OpenXmlElement? reference = parent.ChildElements
            .FirstOrDefault(element => element.LocalName == "extLst");
        foreach ((string value, int index) in values.Select((value, index) => (value, index)))
        {
            StringPoint point = new() { Index = (uint)index };
            point.Append(new NumericValue(value));
            parent.InsertBefore(point, reference);
        }
        pointCount.Val = (uint)values.Count;
    }

    private static void ReplaceMultiLevelPoints(
        MultiLevelStringCache cache,
        IReadOnlyList<string> values)
    {
        cache.RemoveAllChildren<Level>();
        PointCount pointCount = EnsurePointCount(cache, values.Count);
        Level level = new();
        foreach ((string value, int index) in values.Select((value, index) => (value, index)))
        {
            StringPoint point = new() { Index = (uint)index };
            point.Append(new NumericValue(value));
            level.Append(point);
        }
        cache.Append(level);
        pointCount.Val = (uint)values.Count;
    }

    private static void ReplaceNumericPoints(
        OpenXmlCompositeElement parent,
        IReadOnlyList<decimal?> values)
    {
        parent.RemoveAllChildren<NumericPoint>();
        PointCount pointCount = EnsurePointCount(parent, values.Count);
        OpenXmlElement? reference = parent.ChildElements
            .FirstOrDefault(element => element.LocalName == "extLst");
        foreach ((decimal? value, int index) in values.Select((value, index) => (value, index)))
        {
            NumericPoint point = new() { Index = (uint)index };
            point.Append(new NumericValue(value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            parent.InsertBefore(point, reference);
        }
        pointCount.Val = (uint)values.Count;
    }

    private static PointCount EnsurePointCount(OpenXmlCompositeElement parent, int count)
    {
        PointCount? pointCount = parent.GetFirstChild<PointCount>();
        if (pointCount is null)
        {
            pointCount = new PointCount { Val = (uint)count };
            OpenXmlElement? firstPoint = parent.ChildElements
                .FirstOrDefault(element => element.LocalName is "pt" or "lvl" or "extLst");
            parent.InsertBefore(pointCount, firstPoint);
        }
        return pointCount;
    }

    private static decimal? ParseCategoryNumber(string value)
    {
        if (decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal parsed))
        {
            return parsed;
        }

        throw new FormatException($"分类值“{value}”无法写入数值型横轴。");
    }

    private static OpenXmlElement? FindChild(OpenXmlElement element, string localName) =>
        element.ChildElements.FirstOrDefault(child => child.LocalName == localName);

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
