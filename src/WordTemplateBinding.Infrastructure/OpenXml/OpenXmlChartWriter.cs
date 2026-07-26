using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 将标准化图表数据写入 Word ChartPart 的分类和系列缓存，并更新公式范围。
/// </summary>
internal static class OpenXmlChartWriter
{
    private static readonly HashSet<string> ChartElementNames = new(StringComparer.Ordinal)
    {
        "barChart", "bar3DChart", "lineChart", "line3DChart", "pieChart",
        "pie3DChart", "doughnutChart", "areaChart", "area3DChart", "scatterChart",
        "radarChart", "bubbleChart", "surfaceChart", "surface3DChart", "stockChart",
    };

    /// <summary>
    /// 使用 NormalizedChartData 写入图表（新接口：带公式更新）。
    /// </summary>
    internal static void Write(
        MainDocumentPart mainPart,
        ChartTemplateItem chartItem,
        NormalizedChartData data,
        ChartDataDefinition? definition)
    {
        ChartPart? chartPart = mainPart.ChartParts.FirstOrDefault(part =>
            string.Equals(part.Uri.OriginalString, chartItem.Locator.PartKey, StringComparison.Ordinal));
        if (chartPart?.ChartSpace is null)
            throw new InvalidOperationException($"找不到图表部件：{chartItem.LocatorId}");

        IReadOnlyList<OpenXmlElement> seriesElements = FindSeries(chartPart.ChartSpace);
        if (seriesElements.Count == 0)
            throw new InvalidOperationException($"图表 {chartItem.Title} 没有可写的数据系列。");

        int requiredCount = seriesElements.Count;
        if (data.Series.Count < requiredCount)
            throw new FormatException($"图表 {chartItem.Title} 需要 {requiredCount} 个系列，但标准化数据只提供了 {data.Series.Count} 个。");

        for (int index = 0; index < requiredCount; index++)
        {
            OpenXmlElement seriesElement = seriesElements[index];
            // Find matching normalized series
            NormalizedChartSeries? source = data.Series.FirstOrDefault(s => s.SeriesIndex == index)
                ?? data.Series[index];

            UpdateSeriesName(seriesElement, source.Name);
            UpdateCategories(seriesElement, data.Categories);
            UpdateNumbers(
                FindChild(seriesElement, "val") ?? FindChild(seriesElement, "yVal"),
                source.Values,
                chartItem.Title);

            // Update formulas if definition available
            if (definition is not null)
            {
                UpdateCategoryFormula(seriesElement, data.Categories.Count);
                UpdateValueFormula(seriesElement, data.Categories.Count);
                UpdateNameFormula(seriesElement);
            }
        }

        // Disable auto-update
        foreach (OpenXmlElement autoUpdate in Descendants(chartPart.ChartSpace)
                     .Where(e => e.LocalName == "autoUpdate"))
        {
            autoUpdate.SetAttribute(new OpenXmlAttribute("val", string.Empty, "0"));
        }

        chartPart.ChartSpace.Save();
    }

    /// <summary>
    /// 兼容旧接口：使用 ChartDataSet 格式。
    /// </summary>
    internal static void Write(
        MainDocumentPart mainPart,
        ChartTemplateItem chartItem,
        object? value)
    {
        ChartDataSet dataSet = ChartDataSetParser.Parse(value);
        var normData = new NormalizedChartData
        {
            Categories = dataSet.Categories.Select(c => (string?)c).ToList().AsReadOnly(),
            Series = dataSet.Series.Select((s, i) => new NormalizedChartSeries
            {
                SeriesIndex = i,
                SeriesKey = $"series-{i}",
                Name = s.Name,
                Values = s.Values,
            }).ToList().AsReadOnly(),
        };
        Write(mainPart, chartItem, normData, chartItem.DataDefinition);
    }

    private static void UpdateCategoryFormula(OpenXmlElement seriesElement, int newCount)
    {
        OpenXmlElement? container = FindChild(seriesElement, "cat") ?? FindChild(seriesElement, "xVal");
        if (container is null) return;
        UpdateFormulasInElement(container, newCount);
    }

    private static void UpdateValueFormula(OpenXmlElement seriesElement, int newCount)
    {
        OpenXmlElement? container = FindChild(seriesElement, "val") ?? FindChild(seriesElement, "yVal");
        if (container is null) return;
        UpdateFormulasInElement(container, newCount);
    }

    private static void UpdateNameFormula(OpenXmlElement seriesElement)
    {
        // Name formulas are single-cell, no update needed
    }

    private static void UpdateFormulasInElement(OpenXmlElement container, int newCount)
    {
        foreach (OpenXmlElement child in container.ChildElements)
        {
            if (child.LocalName is "strRef" or "numRef" or "multiLvlStrRef")
            {
                OpenXmlElement? formulaEl = child.ChildElements.FirstOrDefault(e => e.LocalName == "f");
                if (formulaEl is not null)
                {
                    string newFormula = UpdateFormulaRange(formulaEl.InnerText, newCount);
                    formulaEl.Remove();
                    child.InsertAt(new DocumentFormat.OpenXml.Drawing.Charts.Formula(newFormula), 0);
                }
            }
        }
    }

    private static string UpdateFormulaRange(string formula, int newRowCount)
    {
        int bang = formula.IndexOf('!');
        if (bang < 0) return formula;
        string prefix = formula[..(bang + 1)];
        string range = formula[(bang + 1)..];
        var parts = range.Split(':');
        if (parts.Length is < 1 or > 2 ||
            !TryParseCellReference(parts[0], out string startColumn, out int startRow))
        {
            return formula;
        }

        string endCellText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseCellReference(endCellText, out string endColumn, out int endRow))
            return formula;

        bool horizontal = startRow == endRow &&
                          !string.Equals(startColumn, endColumn, StringComparison.OrdinalIgnoreCase);
        string newEndColumn = horizontal
            ? ToColumnName(ToColumnNumber(startColumn) + newRowCount - 1)
            : startColumn;
        int newEndRow = horizontal ? startRow : startRow + newRowCount - 1;
        string newRange =
            $"${startColumn.ToUpperInvariant()}${startRow}:${newEndColumn}${newEndRow}";

        return prefix + newRange;
    }

    private static bool TryParseCellReference(
        string text,
        out string column,
        out int row)
    {
        Match match = Regex.Match(text, @"^\$?([A-Za-z]+)\$?(\d+)$");
        if (match.Success &&
            int.TryParse(
                match.Groups[2].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out row))
        {
            column = match.Groups[1].Value;
            return true;
        }

        column = string.Empty;
        row = 0;
        return false;
    }

    private static int ToColumnNumber(string column)
    {
        int result = 0;
        foreach (char character in column.ToUpperInvariant())
            result = checked(result * 26 + character - 'A' + 1);
        return result;
    }

    private static string ToColumnName(int number)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        string result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private static IReadOnlyList<OpenXmlElement> FindSeries(ChartSpace chartSpace)
    {
        OpenXmlElement? chart = Descendants(chartSpace)
            .FirstOrDefault(e => e.LocalName == "chart");
        OpenXmlElement? plotArea = chart is null ? null : FindChild(chart, "plotArea");
        if (plotArea is null) return Array.Empty<OpenXmlElement>();

        return plotArea.ChildElements
            .Where(e => ChartElementNames.Contains(e.LocalName))
            .SelectMany(e => e.ChildElements.Where(c => c.LocalName == "ser"))
            .ToList()
            .AsReadOnly();
    }

    private static void UpdateSeriesName(OpenXmlElement series, string name)
    {
        OpenXmlElement? text = FindChild(series, "tx");
        if (text is null) return;
        UpdateStringCacheValue(text, name);
    }

    private static void UpdateStringCacheValue(OpenXmlElement container, string value)
    {
        OpenXmlElement? directValue = FindChild(container, "v");
        if (directValue is not null)
        {
            directValue.InsertAfterSelf(new NumericValue(value));
            directValue.Remove();
        }

        foreach (OpenXmlElement child in container.ChildElements.Where(
                     e => e.LocalName is "strRef" or "strLit"))
        {
            OpenXmlElement? cache = child.ChildElements.FirstOrDefault(
                e => e.LocalName is "strCache" or "strLit");
            if (cache is null) continue;

            // Remove old pt
            foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt").ToList())
                pt.Remove();

            // Add new pt
            OpenXmlElement? refBefore = cache.ChildElements.FirstOrDefault(e => e.LocalName == "extLst");
            StringPoint newPt = new() { Index = 0U };
            newPt.Append(new NumericValue(value));
            if (refBefore is not null)
                cache.InsertBefore(newPt, refBefore);
            else
                cache.Append(newPt);

            // Update ptCount
            EnsurePointCount(cache, 1);
        }
    }

    private static void UpdateCategories(OpenXmlElement series, IReadOnlyList<string?> categories)
    {
        OpenXmlElement? container = FindChild(series, "cat") ?? FindChild(series, "xVal");
        if (container is null) return;

        bool updated = false;
        foreach (OpenXmlElement refEl in container.ChildElements.Where(
                     e => e.LocalName is "strRef" or "strLit" or "multiLvlStrRef"))
        {
            OpenXmlElement? cache = refEl.ChildElements.FirstOrDefault(
                e => e.LocalName is "strCache" or "strLit");
            if (cache is not null)
            {
                ReplaceCategoryCache(cache, categories);
                updated = true;
            }
            else if (refEl.LocalName == "multiLvlStrRef")
            {
                OpenXmlElement? mlCache = refEl.ChildElements.FirstOrDefault(
                    e => e.LocalName == "multiLvlStrCache");
                if (mlCache is not null)
                {
                    ReplaceMultiLevelCache(mlCache, categories);
                    updated = true;
                }
            }
        }
        if (updated) return;

        // Fallback: numRef
        foreach (OpenXmlElement refEl in container.ChildElements.Where(
                     e => e.LocalName is "numRef" or "numLit"))
        {
            OpenXmlElement? cache = refEl.ChildElements.FirstOrDefault(
                e => e.LocalName is "numCache" or "numLit");
            if (cache is not null)
            {
                ReplaceNumericCategoryCache(cache, categories);
            }
        }
    }

    private static void ReplaceCategoryCache(OpenXmlElement cache, IReadOnlyList<string?> categories)
    {
        // Remove old
        foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt").ToList())
            pt.Remove();

        // Update ptCount
        EnsurePointCount(cache, categories.Count);

        // Add new points
        OpenXmlElement? extLst = cache.ChildElements.FirstOrDefault(e => e.LocalName == "extLst");
        for (int i = 0; i < categories.Count; i++)
        {
            StringPoint pt = new() { Index = (uint)i };
            pt.Append(new NumericValue(categories[i] ?? string.Empty));
            if (extLst is not null)
                cache.InsertBefore(pt, extLst);
            else
                cache.Append(pt);
        }
    }

    private static void ReplaceMultiLevelCache(OpenXmlElement cache, IReadOnlyList<string?> categories)
    {
        // Remove old levels
        foreach (OpenXmlElement lvl in cache.ChildElements.Where(e => e.LocalName == "lvl").ToList())
            lvl.Remove();

        // Update ptCount
        EnsurePointCount(cache, categories.Count);

        Level level = new();
        for (int i = 0; i < categories.Count; i++)
        {
            StringPoint pt = new() { Index = (uint)i };
            pt.Append(new NumericValue(categories[i] ?? string.Empty));
            level.Append(pt);
        }
        cache.Append(level);
    }

    private static void ReplaceNumericCategoryCache(OpenXmlElement cache, IReadOnlyList<string?> categories)
    {
        foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt").ToList())
            pt.Remove();

        EnsurePointCount(cache, categories.Count);

        OpenXmlElement? extLst = cache.ChildElements.FirstOrDefault(e => e.LocalName == "extLst");
        for (int i = 0; i < categories.Count; i++)
        {
            decimal? num = null;
            if (categories[i] is not null &&
                decimal.TryParse(categories[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                num = parsed;

            NumericPoint pt = new() { Index = (uint)i };
            pt.Append(new NumericValue(num?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            if (extLst is not null)
                cache.InsertBefore(pt, extLst);
            else
                cache.Append(pt);
        }
    }

    private static void UpdateNumbers(
        OpenXmlElement? container,
        IReadOnlyList<decimal?> values,
        string chartTitle)
    {
        if (container is null) return;

        foreach (OpenXmlElement refEl in container.ChildElements.Where(
                     e => e.LocalName is "numRef" or "numLit"))
        {
            OpenXmlElement? cache = refEl.ChildElements.FirstOrDefault(
                e => e.LocalName is "numCache" or "numLit");
            if (cache is not null)
            {
                ReplaceNumericCache(cache, values);
                return;
            }
        }

        throw new InvalidOperationException($"图表 {chartTitle} 的系列没有可写数值缓存。");
    }

    private static void ReplaceNumericCache(OpenXmlElement cache, IReadOnlyList<decimal?> values)
    {
        foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt").ToList())
            pt.Remove();

        EnsurePointCount(cache, values.Count);

        OpenXmlElement? extLst = cache.ChildElements.FirstOrDefault(e => e.LocalName == "extLst");
        for (int i = 0; i < values.Count; i++)
        {
            NumericPoint pt = new() { Index = (uint)i };
            pt.Append(new NumericValue(values[i]?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            if (extLst is not null)
                cache.InsertBefore(pt, extLst);
            else
                cache.Append(pt);
        }
    }

    private static OpenXmlElement? FindChild(OpenXmlElement element, string localName) =>
        element.ChildElements.FirstOrDefault(e => e.LocalName == localName);

    private static void EnsurePointCount(OpenXmlElement cache, int count)
    {
        OpenXmlElement? pointCount = cache.ChildElements
            .FirstOrDefault(element => element.LocalName == "ptCount");
        if (pointCount is not null)
        {
            pointCount.SetAttribute(new OpenXmlAttribute(
                "val",
                string.Empty,
                count.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        PointCount created = new() { Val = (uint)count };
        OpenXmlElement? formatCode = cache.ChildElements
            .FirstOrDefault(element => element.LocalName == "formatCode");
        if (formatCode is not null)
            cache.InsertAfter(created, formatCode);
        else
            cache.InsertAt(created, 0);
    }

    private static IEnumerable<OpenXmlElement> Descendants(OpenXmlElement element)
    {
        foreach (OpenXmlElement child in element.ChildElements)
        {
            yield return child;
            foreach (OpenXmlElement descendant in Descendants(child))
                yield return descendant;
        }
    }
}
