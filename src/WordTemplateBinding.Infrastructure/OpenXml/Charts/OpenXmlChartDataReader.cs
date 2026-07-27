using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 从 ChartPart 提取精简的 ChartDataDefinition，仅包含分类、系列、公式和值。
/// 不解析颜色、坐标轴、图例、样式等保持不变的元素。
/// </summary>
internal static class OpenXmlChartDataReader
{
    internal static ChartDataDefinition Read(
        ChartPart chartPart,
        ChartTemplateItem chartItem)
    {
        List<ChartDiagnosticItem> diagnostics = new();
        ChartSpace chartSpace = chartPart.ChartSpace;

        // Determine if embedded workbook exists
        bool hasEmbeddedWorkbook = false;
        foreach (IdPartPair partPair in chartPart.Parts)
        {
            if (partPair.OpenXmlPart is EmbeddedPackagePart)
            {
                hasEmbeddedWorkbook = true;
                break;
            }
        }

        // Read chart type and mode
        OpenXmlElement? chart = Descendants(chartSpace).FirstOrDefault(e => e.LocalName == "chart");
        OpenXmlElement? plotArea = chart is null ? null : FindChild(chart, "plotArea");

        var chartTypeMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["barChart"] = "bar", ["lineChart"] = "line", ["pieChart"] = "pie",
            ["doughnutChart"] = "doughnut", ["areaChart"] = "area",
            ["scatterChart"] = "scatter", ["bubbleChart"] = "bubble",
            ["radarChart"] = "radar", ["stockChart"] = "stock",
        };
        string chartType = "unsupported";
        var chartTypeElements = plotArea?.ChildElements
            .Where(e => chartTypeMap.ContainsKey(e.LocalName)).ToList() ?? new();
        if (chartTypeElements.Count > 0)
            chartType = chartTypeMap[chartTypeElements[0].LocalName];

        bool isScatter = chartType is "scatter" or "bubble";
        string dataMode = isScatter ? "xy-series" : "category-series";

        // Read series
        List<OpenXmlElement> seriesElements = chartTypeElements
            .SelectMany(e => e.ChildElements.Where(c => c.LocalName == "ser")).ToList();

        List<ChartSeriesDefinition> seriesDefs = new(seriesElements.Count);
        for (int i = 0; i < seriesElements.Count; i++)
        {
            OpenXmlElement ser = seriesElements[i];
            string name = ReadSeriesText(ser, i);
            string? nameFormula = ReadChildFormula(FindChild(ser, "tx"), "strRef");
            string? nameSheet = ParseSheetName(nameFormula);
            string? nameCell = ParseStartCell(nameFormula);

            var valueElem = FindChild(ser, "val") ?? FindChild(ser, "yVal");
            string? valueFormula = ReadChildFormula(valueElem, "numRef");
            string? valueSheet = ParseSheetName(valueFormula);
            string? valueStart = ParseStartCell(valueFormula);
            string? valueEnd = ParseEndCell(valueFormula);
            string? numFmt = GetFormatCode(valueElem);

            List<decimal?> values = ReadNumberCache(valueElem);

            seriesDefs.Add(new ChartSeriesDefinition
            {
                SeriesIndex = i,
                SeriesKey = $"series-{i}",
                Name = name,
                NameFormula = nameFormula,
                NameSheetName = nameSheet,
                NameCell = nameCell,
                ValueFormula = valueFormula,
                ValueSheetName = valueSheet,
                ValueStartCell = valueStart,
                ValueEndCell = valueEnd,
                Values = values.AsReadOnly(),
                NumberFormat = numFmt,
            });
        }

        // Read categories
        OpenXmlElement? firstSer = seriesElements.FirstOrDefault();
        var catElem = firstSer is null ? null : FindChild(firstSer, "cat") ?? FindChild(firstSer, "xVal");
        string? catFormula = ReadChildFormula(catElem, "strRef")
            ?? ReadChildFormula(catElem, "numRef")
            ?? ReadChildFormula(catElem, "multiLvlStrRef");
        string? catStart = ParseStartCell(catFormula);
        string? catEnd = ParseEndCell(catFormula);
        string? catSheet = ParseSheetName(catFormula);
        List<string?> catValues = ReadStringCache(catElem);

        ChartCategoryDefinition category = new()
        {
            Name = "分类",
            Formula = catFormula,
            SheetName = catSheet,
            StartCell = catStart,
            EndCell = catEnd,
            Values = catValues.AsReadOnly(),
        };

        if (catValues.Count == 0) { diagnostics.Add(Diag("missing_category_cache", "warning", "未找到分类缓存")); }

        // Current data rows
        int rowCount = catValues.Count;
        if (rowCount == 0)
            rowCount = seriesDefs.Count == 0 ? 0 : seriesDefs.Max(s => s.Values.Count);
        List<ChartDataRowSnapshot> currentData = new(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            var cells = new Dictionary<string, object?>();
            var missing = new Dictionary<string, bool>();
            cells["category"] = r < catValues.Count ? catValues[r] : null;
            missing["category"] = r >= catValues.Count || catValues[r] is null;
            foreach (var s in seriesDefs)
            {
                cells[s.SeriesKey] = r < s.Values.Count ? s.Values[r] : null;
                missing[s.SeriesKey] = r >= s.Values.Count || s.Values[r] is null;
            }
            currentData.Add(new ChartDataRowSnapshot { Index = r, Cells = cells.AsReadOnly(), Missing = missing.AsReadOnly() });
        }

        // Write capability
        bool categoryCacheWritable = firstSer is not null && HasWritableCategoryCache(firstSer);
        bool allValueCachesWritable = seriesElements.Count > 0 &&
            seriesElements.All(HasWritableValueCache);

        string writeCapability = hasEmbeddedWorkbook
            ? "workbook-and-cache"
            : "cache-only";
        if (chartType == "unsupported" ||
            seriesDefs.Count == 0 ||
            !allValueCachesWritable ||
            (chartType == "radar" && (!categoryCacheWritable || catValues.Count == 0)))
        {
            writeCapability = "unsupported";
        }

        if (chartType == "radar")
        {
            if (seriesDefs.Count == 0)
                diagnostics.Add(Diag("radar_missing_series", "warning", "雷达图没有数据系列。"));
            if (catValues.Count == 0)
                diagnostics.Add(Diag("radar_missing_categories", "warning", "雷达图没有分类指标。"));
            if (!categoryCacheWritable)
                diagnostics.Add(Diag("radar_missing_category_cache", "warning", "雷达图没有可写分类缓存。"));

            for (int i = 0; i < seriesElements.Count; i++)
            {
                if (!HasWritableValueCache(seriesElements[i]))
                {
                    diagnostics.Add(new ChartDiagnosticItem
                    {
                        Code = "radar_missing_value_cache",
                        Level = "warning",
                        Message = $"雷达图系列 {i + 1} 没有可写数值缓存。",
                        SeriesIndex = i,
                        Recoverable = true,
                    });
                }

                if (catValues.Count > 0 && seriesDefs[i].Values.Count != catValues.Count)
                {
                    diagnostics.Add(new ChartDiagnosticItem
                    {
                        Code = "radar_series_length_mismatch",
                        Level = "warning",
                        Message = $"雷达图系列 \"{seriesDefs[i].Name}\" 的数值数量 {seriesDefs[i].Values.Count} 与指标数量 {catValues.Count} 不一致。",
                        SeriesIndex = i,
                        Recoverable = true,
                    });
                }
            }
        }
        bool isBindable = writeCapability != "unsupported";

        if (!isBindable) { diagnostics.Add(Diag("chart_not_bindable", "warning", "该图表不支持数据写回")); }

        return new ChartDataDefinition
        {
            SchemaVersion = "1.0",
            LocatorId = chartItem.LocatorId,
            PartKey = chartItem.Locator.PartKey,
            RelationshipId = chartItem.Locator.RelationshipId,
            DocumentOrder = chartItem.Locator.DocumentOrder,
            ChartType = chartType,
            DataMode = dataMode,
            Category = category,
            Series = seriesDefs.AsReadOnly(),
            CurrentData = currentData.AsReadOnly(),
            WriteCapability = writeCapability,
            Diagnostics = diagnostics.AsReadOnly(),
        };
    }

    // --- Helpers ---

    private static string ReadSeriesText(OpenXmlElement ser, int idx)
    {
        var tx = FindChild(ser, "tx");
        var val = tx is null ? null : Descendants(tx).FirstOrDefault(e => e.LocalName == "v")?.InnerText.Trim();
        return string.IsNullOrWhiteSpace(val) ? $"系列 {idx + 1}" : val;
    }

    private static string? ReadChildFormula(OpenXmlElement? parent, string containerName)
    {
        if (parent is null) return null;
        var refEl = FindChild(parent, containerName);
        return refEl is null ? null : GetChildText(refEl, "f");
    }

    private static List<decimal?> ReadNumberCache(OpenXmlElement? container)
    {
        var result = new List<decimal?>();
        if (container is null) return result;
        // Find cache within ref/lit
        foreach (var child in container.ChildElements)
        {
            if (child.LocalName is not ("numRef" or "strRef" or "numLit" or "strLit")) continue;
            var cache = child.ChildElements.FirstOrDefault(e => e.LocalName is "numCache" or "strCache" or "numLit" or "strLit");
            var source = cache ?? child;
            int maxIdx = -1;
            foreach (var pt in source.ChildElements.Where(e => e.LocalName == "pt"))
            {
                uint idxVal = 0;
                var idxAttr = pt.GetAttribute("idx", string.Empty);
                if (!string.IsNullOrEmpty(idxAttr.Value))
                    uint.TryParse(idxAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idxVal);
                var raw = GetChildText(pt, "v");
                decimal? val = null;
                if (raw is not null && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    val = num;
                while (result.Count < (int)idxVal) { result.Add(null); }
                result.Add(val);
                maxIdx = Math.Max(maxIdx, (int)idxVal);
            }
            // Read ptCount
            var ptCountEl = source.ChildElements.FirstOrDefault(e => e.LocalName == "ptCount");
            int ptCount = 0;
            if (ptCountEl is not null)
            {
                var pcAttr = ptCountEl.GetAttribute("val", string.Empty);
                if (!string.IsNullOrEmpty(pcAttr.Value))
                    int.TryParse(pcAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ptCount);
            }
            int expected = Math.Max(ptCount, maxIdx + 1);
            while (result.Count < expected) result.Add(null);
            break;
        }
        return result;
    }

    private static List<string?> ReadStringCache(OpenXmlElement? container)
    {
        var result = new List<string?>();
        if (container is null) return result;

        OpenXmlElement? multiRef = FindChild(container, "multiLvlStrRef");
        OpenXmlElement? multiCache = multiRef is null
            ? null
            : FindChild(multiRef, "multiLvlStrCache");
        if (multiCache is not null)
        {
            OpenXmlElement? finestLevel = multiCache.ChildElements
                .FirstOrDefault(e => e.LocalName == "lvl");
            if (finestLevel is null) return result;
            return ReadIndexedStringPoints(finestLevel, ReadPointCount(multiCache));
        }

        foreach (var child in container.ChildElements)
        {
            if (child.LocalName is not ("strRef" or "strLit" or "numRef")) continue;
            var cache = child.ChildElements.FirstOrDefault(e => e.LocalName is "strCache" or "numCache" or "strLit" or "numLit");
            var source = cache ?? child;
            int maxIdx = -1;
            foreach (var pt in source.ChildElements.Where(e => e.LocalName == "pt"))
            {
                uint idxVal = 0;
                var idxAttr = pt.GetAttribute("idx", string.Empty);
                if (!string.IsNullOrEmpty(idxAttr.Value))
                    uint.TryParse(idxAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idxVal);
                var raw = GetChildText(pt, "v");
                while (result.Count < (int)idxVal) { result.Add(null); }
                result.Add(raw);
                maxIdx = Math.Max(maxIdx, (int)idxVal);
            }
            var ptCountEl = source.ChildElements.FirstOrDefault(e => e.LocalName == "ptCount");
            int ptCount = 0;
            if (ptCountEl is not null)
            {
                var pcAttr = ptCountEl.GetAttribute("val", string.Empty);
                if (!string.IsNullOrEmpty(pcAttr.Value))
                    int.TryParse(pcAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ptCount);
            }
            int expected = Math.Max(ptCount, maxIdx + 1);
            while (result.Count < expected) result.Add(null);
            break;
        }
        return result;
    }

    private static List<string?> ReadIndexedStringPoints(
        OpenXmlElement source,
        int declaredPointCount)
    {
        List<string?> result = new();
        int maxIdx = -1;
        foreach (OpenXmlElement pt in source.ChildElements.Where(e => e.LocalName == "pt"))
        {
            uint idxVal = 0;
            OpenXmlAttribute idxAttr = pt.GetAttribute("idx", string.Empty);
            if (!string.IsNullOrEmpty(idxAttr.Value))
                uint.TryParse(idxAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idxVal);
            while (result.Count < (int)idxVal) result.Add(null);
            result.Add(GetChildText(pt, "v"));
            maxIdx = Math.Max(maxIdx, (int)idxVal);
        }

        int expected = Math.Max(declaredPointCount, maxIdx + 1);
        while (result.Count < expected) result.Add(null);
        return result;
    }

    private static int ReadPointCount(OpenXmlElement source)
    {
        OpenXmlElement? pointCount = FindChild(source, "ptCount");
        string? raw = pointCount is null
            ? null
            : pointCount.GetAttribute("val", string.Empty).Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    private static string? GetFormatCode(OpenXmlElement? container)
    {
        if (container is null) return null;
        foreach (var child in container.ChildElements)
        {
            if (child.LocalName is "numRef" or "strRef")
            {
                var cache = child.ChildElements.FirstOrDefault(e => e.LocalName is "numCache" or "strCache");
                var fc = cache?.ChildElements.FirstOrDefault(e => e.LocalName == "formatCode");
                return fc?.InnerText;
            }
        }
        return null;
    }

    private static string? ParseSheetName(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        var m = Regex.Match(formula, @"^'?([^'!]+)'?!");
        return m.Success ? m.Groups[1].Value : "Sheet1";
    }

    private static string? ParseStartCell(string? formula) => ParseCellRange(formula, true);
    private static string? ParseEndCell(string? formula) => ParseCellRange(formula, false);

    private static string? ParseCellRange(string? formula, bool start)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        var bang = formula.IndexOf('!');
        var range = (bang >= 0 ? formula[(bang + 1)..] : formula).Replace("$", string.Empty);
        var parts = range.Split(':');
        if (start) return parts[0];
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    private static bool HasWritableCategoryCache(OpenXmlElement series)
    {
        OpenXmlElement? container = FindChild(series, "cat") ?? FindChild(series, "xVal");
        return HasCache(
            container,
            ("strRef", "strCache"),
            ("numRef", "numCache"),
            ("multiLvlStrRef", "multiLvlStrCache"),
            ("strLit", "strLit"),
            ("numLit", "numLit"));
    }

    private static bool HasWritableValueCache(OpenXmlElement series)
    {
        OpenXmlElement? container = FindChild(series, "val") ?? FindChild(series, "yVal");
        return HasCache(
            container,
            ("numRef", "numCache"),
            ("numLit", "numLit"));
    }

    private static bool HasCache(
        OpenXmlElement? container,
        params (string Source, string Cache)[] shapes)
    {
        if (container is null) return false;
        foreach ((string sourceName, string cacheName) in shapes)
        {
            OpenXmlElement? source = FindChild(container, sourceName);
            if (source is null) continue;
            if (sourceName == cacheName || FindChild(source, cacheName) is not null)
                return true;
        }
        return false;
    }

    private static ChartDiagnosticItem Diag(string code, string level, string msg) => new()
    { Code = code, Level = level, Message = msg, Recoverable = true };

    private static string? GetChildText(OpenXmlElement? e, string name) =>
        e?.ChildElements.FirstOrDefault(c => c.LocalName == name)?.InnerText;

    private static OpenXmlElement? FindChild(OpenXmlElement? e, string name) =>
        e?.ChildElements.FirstOrDefault(c => c.LocalName == name);

    private static IEnumerable<OpenXmlElement> Descendants(OpenXmlElement e)
    {
        foreach (var c in e.ChildElements) { yield return c; foreach (var d in Descendants(c)) yield return d; }
    }
}
