using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 对 Word ChartPart 执行深度结构化分析，生成 ChartAnalysisSnapshot。
/// </summary>
internal static class OpenXmlWordChartAnalyzer
{
    private const string CurrentSchemaVersion = "1.0";

    private static readonly Dictionary<string, string> ChartTypeMap = new(StringComparer.Ordinal)
    {
        ["barChart"] = "bar", ["bar3DChart"] = "bar3D",
        ["lineChart"] = "line", ["line3DChart"] = "line3D",
        ["pieChart"] = "pie", ["pie3DChart"] = "pie3D",
        ["doughnutChart"] = "doughnut",
        ["areaChart"] = "area", ["area3DChart"] = "area3D",
        ["scatterChart"] = "scatter",
        ["radarChart"] = "radar",
        ["bubbleChart"] = "bubble",
        ["surfaceChart"] = "surface", ["surface3DChart"] = "surface3D",
        ["stockChart"] = "stock",
    };

    private static readonly Dictionary<string, string> ChartTypeLabels = new(StringComparer.Ordinal)
    {
        ["bar"] = "条形图", ["bar3D"] = "3D 条形图",
        ["line"] = "折线图", ["line3D"] = "3D 折线图",
        ["pie"] = "饼图", ["pie3D"] = "3D 饼图",
        ["doughnut"] = "环形图",
        ["area"] = "面积图", ["area3D"] = "3D 面积图",
        ["scatter"] = "散点图",
        ["radar"] = "雷达图",
        ["bubble"] = "气泡图",
        ["surface"] = "曲面图", ["surface3D"] = "3D 曲面图",
        ["stock"] = "股价图",
        ["combo"] = "组合图",
        ["unsupported"] = "不支持的图表类型",
    };

    internal static ChartAnalysisSnapshot Analyze(
        ChartPart chartPart,
        ChartLocator locator,
        string locatorId,
        string? externalTitle,
        int chartIndex)
    {
        List<ChartDiagnosticItem> diagnostics = new();
        ChartSpace chartSpace = chartPart.ChartSpace;

        ChartIdentitySnapshot identity = new()
        {
            LocatorId = locatorId,
            PartKey = locator.PartKey,
            RelationshipId = locator.RelationshipId,
            DocumentOrder = locator.DocumentOrder,
        };

        ChartRelationshipInfo relInfo = OpenXmlChartRelationshipReader.Read(chartPart);

        List<ChartFormulaSnapshot> formulas = new();
        List<ChartCacheSummary> caches = new();

        ChartSourceSnapshot source = new()
        {
            ChartPartPath = chartPart.Uri.OriginalString,
            ChartRelationshipPartPath = relInfo.ChartRelationshipPartPath,
            ExternalDataRelationshipId = relInfo.ExternalDataRelationshipId,
            EmbeddedWorkbookPath = relInfo.EmbeddedWorkbookPath,
            EmbeddedWorkbookDetected = relInfo.EmbeddedWorkbookDetected,
            Formulas = formulas.AsReadOnly(),
            Caches = caches.AsReadOnly(),
        };

        OpenXmlElement? chartElement = Descendants(chartSpace)
            .FirstOrDefault(e => e.LocalName == "chart");
        OpenXmlElement? plotArea = chartElement is null
            ? null : FindChild(chartElement, "plotArea");

        List<OpenXmlElement> chartTypeElements = plotArea?.ChildElements
            .Where(e => ChartTypeMap.ContainsKey(e.LocalName))
            .ToList() ?? new List<OpenXmlElement>();

        string chartType = chartTypeElements.Count switch
        {
            0 => "unsupported",
            > 1 => "combo",
            _ => ChartTypeMap[chartTypeElements[0].LocalName],
        };
        string typeLabel = ChartTypeLabels.TryGetValue(chartType, out string? label)
            ? label : chartType;

        string title = externalTitle ?? ReadChartTitle(chartElement) ?? $"图表 {chartIndex + 1}";

        // Plot Groups
        List<ChartPlotGroupSnapshot> plotGroups = new(chartTypeElements.Count);
        List<OpenXmlElement> allSeriesElements = new();

        for (int groupOrder = 0; groupOrder < chartTypeElements.Count; groupOrder++)
        {
            OpenXmlElement groupElement = chartTypeElements[groupOrder];
            string groupType = ChartTypeMap.TryGetValue(groupElement.LocalName, out string? mapped)
                ? mapped : "unsupported";

            string? grouping = GetAttribute(FindChild(groupElement, "grouping"), "val");
            string? barDir = GetAttribute(FindChild(groupElement, "barDir"), "val");
            string? radarStyle = null;
            if (groupType == "radar")
            {
                string? rawRadarStyle = GetAttribute(FindChild(groupElement, "radarStyle"), "val");
                if (rawRadarStyle is "standard" or "marker" or "filled")
                {
                    radarStyle = rawRadarStyle;
                }
                else
                {
                    radarStyle = "standard";
                    if (!string.IsNullOrWhiteSpace(rawRadarStyle))
                    {
                        diagnostics.Add(new ChartDiagnosticItem
                        {
                            Code = "radar_unknown_style",
                            Level = "warning",
                            Message = $"未知雷达图样式 \"{rawRadarStyle}\"，已回退为 standard。",
                            Path = $"plot-group-{groupOrder}",
                            Recoverable = true,
                        });
                    }
                }
            }

            List<OpenXmlElement> groupSeriesElements = groupElement.ChildElements
                .Where(e => e.LocalName == "ser").ToList();

            string groupId = chartTypeElements.Count == 1
                ? "plot-group-0"
                : $"plot-group-{groupOrder}";

            List<string> seriesKeys = new(groupSeriesElements.Count);
            for (int j = 0; j < groupSeriesElements.Count; j++)
                seriesKeys.Add($"series-{allSeriesElements.Count + j}");
            allSeriesElements.AddRange(groupSeriesElements);

            List<string> axisIds = ReadPlotGroupAxisIds(groupElement);

            plotGroups.Add(new ChartPlotGroupSnapshot
            {
                Id = groupId,
                Order = groupOrder,
                Type = groupType,
                Grouping = grouping,
                BarDirection = barDir,
                RadarStyle = radarStyle,
                SeriesKeys = seriesKeys.AsReadOnly(),
                AxisIds = axisIds.AsReadOnly(),
            });
        }

        List<ChartAxisSnapshot> axes = ReadAxes(plotArea);

        List<ChartSeriesSnapshot> seriesList = new(allSeriesElements.Count);
        for (int seriesIndex = 0; seriesIndex < allSeriesElements.Count; seriesIndex++)
        {
            OpenXmlElement seriesElement = allSeriesElements[seriesIndex];
            ChartPlotGroupSnapshot? parentGroup = plotGroups.FirstOrDefault(g =>
                g.SeriesKeys.Contains($"series-{seriesIndex}"));

            ChartSeriesSnapshot series = ReadSeries(
                seriesElement, seriesIndex, parentGroup, formulas);
            seriesList.Add(series);
            ReadSeriesCacheInfo(seriesElement, caches);
        }

        OpenXmlElement? firstSeries = allSeriesElements.FirstOrDefault();
        List<ChartCategorySnapshot> categories = ReadCategories(firstSeries, formulas);

        if (allSeriesElements.Count > 0 && categories.Count == 0)
        {
            diagnostics.Add(new ChartDiagnosticItem
            {
                Code = "missing_category_cache", Level = "warning",
                Message = "未找到分类缓存数据。", Recoverable = true,
            });
        }

        if (chartType == "radar")
        {
            AddRadarDiagnostics(
                allSeriesElements,
                seriesList,
                categories,
                diagnostics);
        }

        decimal? radarMinimum = null;
        decimal? radarMaximum = null;
        if (chartType == "radar")
        {
            ChartAxisSnapshot? valueAxis = axes.FirstOrDefault(axis => axis.Type == "value");
            (decimal minimum, decimal maximum) = RadarScaleCalculator.Resolve(
                seriesList,
                valueAxis?.Min,
                valueAxis?.Max,
                diagnostics);
            radarMinimum = minimum;
            radarMaximum = maximum;
        }

        ChartDefinitionSnapshot definition = new()
        {
            Type = chartType,
            TypeLabel = typeLabel,
            Title = title,
            SupportedForBinding = IsSupportedForBinding(
                chartType,
                allSeriesElements,
                seriesList,
                categories),
            RadarMinimum = radarMinimum,
            RadarMaximum = radarMaximum,
            WidthEmu = 0,
            HeightEmu = 0,
        };

        ChartDataTableSnapshot dataTable = ChartDataTableBuilder.Build(
            chartType, seriesList, categories);

        ChartBindingContract bindingContract = ChartBindingContractBuilder.Build(
            seriesList, categories, locatorId, dataTable,
            out List<ChartDiagnosticItem> contractDiagnostics);
        diagnostics.AddRange(contractDiagnostics);

        if (!definition.SupportedForBinding &&
            allSeriesElements.Count > 0 &&
            chartType != "radar")
        {
            diagnostics.Add(new ChartDiagnosticItem
            {
                Code = "unsupported_chart_type", Level = "warning",
                Message = $"图表类型 {typeLabel} 暂不支持数据写回。", Recoverable = true,
            });
        }

        if (chartType == "unsupported")
        {
            diagnostics.Add(new ChartDiagnosticItem
            {
                Code = "unsupported_chart_type", Level = "error",
                Message = "无法识别图表类型。", Recoverable = true,
            });
        }

        bool hasErrors = diagnostics.Any(d => d.Level == "error");
        bool hasWarnings = diagnostics.Any(d => d.Level == "warning");

        ChartAnalysisDiagnostics analysisDiagnostics = new()
        {
            HasErrors = hasErrors,
            HasWarnings = hasWarnings,
            CompletenessScore = CalcCompleteness(seriesList, categories, diagnostics),
            Items = diagnostics.AsReadOnly(),
        };

        return new ChartAnalysisSnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            Identity = identity,
            Source = source,
            Chart = definition,
            PlotGroups = plotGroups.AsReadOnly(),
            Axes = axes.AsReadOnly(),
            Categories = categories.AsReadOnly(),
            Series = seriesList.AsReadOnly(),
            DataTable = dataTable,
            BindingContract = bindingContract,
            Diagnostics = analysisDiagnostics,
        };
    }

    private static ChartSeriesSnapshot ReadSeries(
        OpenXmlElement seriesElement, int seriesIndex,
        ChartPlotGroupSnapshot? parentGroup,
        List<ChartFormulaSnapshot> formulas)
    {
        string name = ReadSeriesName(seriesElement, seriesIndex);
        string? nameFormula = ReadChildFormula(FindChild(seriesElement, "tx"), "strRef");
        if (nameFormula is not null)
        {
            formulas.Add(new ChartFormulaSnapshot
            {
                Role = "SeriesName", SeriesIndex = seriesIndex,
                Formula = nameFormula,
                SheetName = ParseSheetName(nameFormula),
                RangeAddress = ParseRangeAddress(nameFormula),
            });
        }

        string axisRole = parentGroup is not null && parentGroup.Order > 0 ? "secondary" : "primary";

        string? catFormula = ReadFirstChildFormula(
            FindChild(seriesElement, "cat") ?? FindChild(seriesElement, "xVal"),
            "strRef",
            "numRef",
            "multiLvlStrRef");
        if (catFormula is not null)
        {
            formulas.Add(new ChartFormulaSnapshot
            {
                Role = "Category", SeriesIndex = seriesIndex,
                Formula = catFormula,
                SheetName = ParseSheetName(catFormula),
                RangeAddress = ParseRangeAddress(catFormula),
            });
        }

        string? valFormula = ReadChildFormula(
            FindChild(seriesElement, "val") ?? FindChild(seriesElement, "yVal"), "numRef");
        if (valFormula is not null)
        {
            formulas.Add(new ChartFormulaSnapshot
            {
                Role = "Value", SeriesIndex = seriesIndex,
                Formula = valFormula,
                SheetName = ParseSheetName(valFormula),
                RangeAddress = ParseRangeAddress(valFormula),
            });
        }

        IReadOnlyList<ChartDataPointSnapshot> values = ReadDataPoints(
            FindChild(seriesElement, "val") ?? FindChild(seriesElement, "yVal"), "number");

        string groupType = parentGroup?.Type ?? "unknown";

        int parsedIndex = ParseIntegerChildAttribute(seriesElement, "idx", seriesIndex);
        int parsedOrder = ParseIntegerChildAttribute(seriesElement, "order", seriesIndex);

        return new ChartSeriesSnapshot
        {
            Key = $"series-{seriesIndex}",
            SeriesIndex = parsedIndex, Order = parsedOrder,
            Name = name, ChartType = groupType,
            PlotGroupId = parentGroup?.Id ?? "plot-group-0",
            AxisRole = axisRole,
            AxisIds = parentGroup?.AxisIds.ToList().AsReadOnly()
                ?? (IReadOnlyList<string>)Array.Empty<string>(),
            NameFormula = nameFormula,
            CategoryFormula = catFormula,
            ValueFormula = valFormula,
            Values = values,
        };
    }

    private static List<ChartCategorySnapshot> ReadCategories(
        OpenXmlElement? firstSeriesElement, List<ChartFormulaSnapshot> formulas)
    {
        if (firstSeriesElement is null) return new List<ChartCategorySnapshot>();

        OpenXmlElement? catContainer = FindChild(firstSeriesElement, "cat")
            ?? FindChild(firstSeriesElement, "xVal");
        if (catContainer is null) return new List<ChartCategorySnapshot>();

        OpenXmlElement? strRef = FindChild(catContainer, "strRef");
        if (strRef is not null)
        {
            string? formula = ReadFormula(strRef);
            if (formula is not null)
            {
                formulas.Add(new ChartFormulaSnapshot
                {
                    Role = "Category", SeriesIndex = null,
                    Formula = formula,
                    SheetName = ParseSheetName(formula),
                    RangeAddress = ParseRangeAddress(formula),
                });
            }
            return ReadCategoryPoints(strRef);
        }

        OpenXmlElement? numRef = FindChild(catContainer, "numRef");
        if (numRef is not null)
        {
            string? formula = ReadFormula(numRef);
            if (formula is not null)
            {
                formulas.Add(new ChartFormulaSnapshot
                {
                    Role = "Category", SeriesIndex = null,
                    Formula = formula,
                    SheetName = ParseSheetName(formula),
                    RangeAddress = ParseRangeAddress(formula),
                });
            }
            return ReadCategoryPoints(numRef);
        }

        OpenXmlElement? strLit = FindChild(catContainer, "strLit");
        if (strLit is not null) return ReadCategoryPoints(strLit);

        OpenXmlElement? numLit = FindChild(catContainer, "numLit");
        if (numLit is not null) return ReadCategoryPoints(numLit);

        OpenXmlElement? multiRef = FindChild(catContainer, "multiLvlStrRef");
        if (multiRef is not null)
        {
            string? formula = ReadFormula(multiRef);
            if (formula is not null)
            {
                formulas.Add(new ChartFormulaSnapshot
                {
                    Role = "Category", SeriesIndex = null,
                    Formula = formula,
                    SheetName = ParseSheetName(formula),
                    RangeAddress = ParseRangeAddress(formula),
                });
            }

            OpenXmlElement? multiCache = FindChild(multiRef, "multiLvlStrCache");
            if (multiCache is null)
            {
                return new List<ChartCategorySnapshot>();
            }

            List<ChartCategorySnapshot> multi = new();
            int lastIdx = 0;
            foreach (OpenXmlElement level in multiCache.ChildElements.Where(e => e.LocalName == "lvl"))
            {
                foreach (OpenXmlElement pt in level.ChildElements.Where(e => e.LocalName == "pt"))
                {
                    uint idx = ParseIndex(pt);
                    string val = GetChildText(pt, "v") ?? string.Empty;
                    ChartCategorySnapshot? existing = multi.FirstOrDefault(c => c.Index == (int)idx);
                    if (existing is not null)
                    {
                        var newLevels = existing.Levels.ToList();
                        newLevels.Add(val);
                        // Can't mutate record; skip
                    }
                    else
                    {
                        multi.Add(new ChartCategorySnapshot
                        {
                            Index = (int)idx, Value = val, DisplayValue = val,
                            Levels = new List<string> { val }.AsReadOnly(), IsMissing = false,
                        });
                    }
                    lastIdx = Math.Max(lastIdx, (int)idx);
                }
            }
            multi = FillCategoryGaps(multi, lastIdx + 1);
            return multi.OrderBy(c => c.Index).ToList();
        }

        return new List<ChartCategorySnapshot>();
    }

    private static List<ChartCategorySnapshot> ReadCategoryPoints(OpenXmlElement container)
    {
        // Look inside cache elements (strCache/numCache/strLit/numLit) first, then direct children
        OpenXmlElement? cache = container.ChildElements
            .FirstOrDefault(e => e.LocalName is "strCache" or "numCache" or "strLit" or "numLit") ?? container;
        return ReadCategoryPointsFromCache(cache);
    }

    private static List<ChartCategorySnapshot> ReadCategoryPointsFromCache(OpenXmlElement cache)
    {
        List<ChartCategorySnapshot> result = new();
        int maxIdx = -1;
        int pointCount = 0;
        string? pointCountText = GetAttribute(FindChild(cache, "ptCount"), "val");
        if (pointCountText is not null)
        {
            int.TryParse(
                pointCountText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out pointCount);
        }
        foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt"))
        {
            uint idx = ParseIndex(pt);
            string val = GetChildText(pt, "v") ?? string.Empty;
            string? numFmt = GetAttribute(pt, "formatCode");
            result.Add(new ChartCategorySnapshot
            {
                Index = (int)idx, Value = val, DisplayValue = val,
                NumberFormat = numFmt, IsMissing = false,
            });
            maxIdx = Math.Max(maxIdx, (int)idx);
        }
        return FillCategoryGaps(result, Math.Max(pointCount, maxIdx + 1));
    }

    private static IReadOnlyList<ChartDataPointSnapshot> ReadDataPoints(
        OpenXmlElement? container, string valueType)
    {
        if (container is null) return Array.Empty<ChartDataPointSnapshot>();

        List<ChartDataPointSnapshot> points = new();
        int maxIdx = 0;

        // Search for cache within reference elements (numRef/strRef) first,
        // then literal elements (numLit/strLit), then direct children
        OpenXmlElement? cache = null;
        foreach (OpenXmlElement child in container.ChildElements)
        {
            if (child.LocalName is "numRef" or "strRef" or "numLit" or "strLit")
            {
                cache = child.ChildElements
                    .FirstOrDefault(e => e.LocalName is "numCache" or "strCache" or "numLit" or "strLit");
                if (cache is not null) break;
            }
        }
        cache ??= container.ChildElements
            .FirstOrDefault(e => e.LocalName is "numCache" or "strCache" or "numLit" or "strLit");
        cache ??= container;

        int ptCount = 0;
        OpenXmlElement? ptCountEl = cache.ChildElements.FirstOrDefault(e => e.LocalName == "ptCount");
        if (ptCountEl is not null)
        {
            string? valAttr = GetAttribute(ptCountEl, "val");
            if (valAttr is not null && uint.TryParse(valAttr, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out uint pc))
                ptCount = (int)pc;
        }

        foreach (OpenXmlElement pt in cache.ChildElements.Where(e => e.LocalName == "pt"))
        {
            uint idx = ParseIndex(pt);
            string? raw = GetChildText(pt, "v");
            string? numFmt = GetAttribute(pt, "formatCode");

            object? val = null;
            if (raw is not null)
            {
                if (valueType == "number" &&
                    decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal num))
                    val = num;
                else
                    val = raw;
            }

            points.Add(new ChartDataPointSnapshot
            {
                Index = (int)idx, Value = val, DisplayValue = raw,
                NumberFormat = numFmt, IsMissing = false,
            });
            maxIdx = Math.Max(maxIdx, (int)idx);
        }

        int totalExpected = Math.Max(ptCount, maxIdx + 1);
        Dictionary<int, ChartDataPointSnapshot> map = points.ToDictionary(p => p.Index);
        ChartDataPointSnapshot[] result = new ChartDataPointSnapshot[totalExpected];

        for (int i = 0; i < totalExpected; i++)
        {
            if (map.TryGetValue(i, out ChartDataPointSnapshot? existing))
                result[i] = existing;
            else
                result[i] = new ChartDataPointSnapshot { Index = i, Value = null, IsMissing = true };
        }

        return result;
    }

    private static List<ChartAxisSnapshot> ReadAxes(OpenXmlElement? plotArea)
    {
        List<ChartAxisSnapshot> axes = new();
        if (plotArea is null) return axes;

        foreach (OpenXmlElement child in plotArea.ChildElements)
        {
            if (child.LocalName is not "catAx" and not "valAx" and not "dateAx" and not "serAx")
                continue;

            string id = GetAttribute(child, "axId") ?? string.Empty;
            string axType = child.LocalName switch
            {
                "catAx" => "category", "valAx" => "value",
                "dateAx" => "date", "serAx" => "series",
                _ => "unknown",
            };

            OpenXmlElement? scaling = FindChild(child, "scaling");
            OpenXmlElement? axPos = FindChild(child, "axPos");
            string? position = GetAttribute(axPos, "val");

            string role = axType switch
            {
                "category" or "date" when position == "b" || position == "t" => "x",
                "category" or "date" when position == "l" || position == "r" => "y",
                "value" when position == "b" || position == "t" => "x",
                "value" when position == "l" || position == "r" => "y",
                _ => position switch { "b" or "t" => "x", "l" or "r" => "y", _ => "unknown" },
            };

            OpenXmlElement? titleEl = FindChild(child, "title");
            string? axisTitle = null;
            if (titleEl is not null)
            {
                axisTitle = string.Concat(
                    Descendants(titleEl).Where(e => e.LocalName == "t").Select(e => e.InnerText)).Trim();
                if (string.IsNullOrWhiteSpace(axisTitle)) axisTitle = null;
            }

            decimal? min = ParseNumericAttribute(scaling, "min");
            decimal? max = ParseNumericAttribute(scaling, "max");
            string? orientation = GetAttribute(scaling, "orientation");

            decimal? majorUnit = ParseNumericAttribute(child, "majorUnit");
            decimal? minorUnit = ParseNumericAttribute(child, "minorUnit");

            OpenXmlElement? numFmtEl = FindChild(child, "numFmt");
            string? numFmt = GetAttribute(numFmtEl, "formatCode")
                ?? GetAttribute(child, "numFmt");

            OpenXmlElement? crossAxEl = FindChild(child, "crossAx");
            string? crossAx = GetAttribute(crossAxEl, "val");

            OpenXmlElement? deleteEl = FindChild(child, "delete");
            bool visible = GetAttribute(deleteEl, "val") != "1";

            axes.Add(new ChartAxisSnapshot
            {
                Id = id, Type = axType, Role = role, Position = position,
                Title = axisTitle, Min = min, Max = max,
                MajorUnit = majorUnit, MinorUnit = minorUnit,
                NumberFormat = numFmt,
                Reversed = string.Equals(orientation, "minMax", StringComparison.OrdinalIgnoreCase),
                Visible = visible, CrossAxisId = crossAx,
            });
        }

        return axes.OrderBy(a => a.Role).ThenBy(a => a.Type).ToList();
    }

    private static List<ChartCategorySnapshot> FillCategoryGaps(
        List<ChartCategorySnapshot> points, int expectedCount)
    {
        List<ChartCategorySnapshot> result = new(expectedCount);
        Dictionary<int, ChartCategorySnapshot> map = points.ToDictionary(p => p.Index);

        for (int i = 0; i < expectedCount; i++)
        {
            if (map.TryGetValue(i, out ChartCategorySnapshot? existing))
                result.Add(existing);
            else
                result.Add(new ChartCategorySnapshot
                {
                    Index = i, Value = null, DisplayValue = string.Empty, IsMissing = true,
                });
        }
        return result;
    }

    private static string ReadSeriesName(OpenXmlElement series, int seriesIndex)
    {
        OpenXmlElement? text = FindChild(series, "tx");
        string? val = text is null ? null
            : Descendants(text).FirstOrDefault(e => e.LocalName == "v")?.InnerText.Trim();
        return string.IsNullOrWhiteSpace(val) ? $"系列 {seriesIndex + 1}" : val;
    }

    private static string? ReadChartTitle(OpenXmlElement? chart)
    {
        if (chart is null) return null;
        OpenXmlElement? title = FindChild(chart, "title");
        if (title is null) return null;
        string val = string.Concat(
            Descendants(title).Where(e => e.LocalName == "t").Select(e => e.InnerText)).Trim();
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    private static string? ReadFormula(OpenXmlElement element) =>
        GetChildText(element, "f");

    private static string? ReadChildFormula(OpenXmlElement? parent, string containerName)
    {
        if (parent is null) return null;
        OpenXmlElement? refEl = FindChild(parent, containerName);
        return refEl is null ? null : ReadFormula(refEl);
    }

    private static string? ReadFirstChildFormula(
        OpenXmlElement? parent,
        params string[] containerNames)
    {
        if (parent is null) return null;
        foreach (string containerName in containerNames)
        {
            string? formula = ReadChildFormula(parent, containerName);
            if (formula is not null) return formula;
        }
        return null;
    }

    private static string? ParseSheetName(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        Match match = Regex.Match(formula, @"^'?([^'!]+)'?!");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ParseRangeAddress(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        int bang = formula.IndexOf('!');
        return bang >= 0 ? formula[(bang + 1)..] : formula;
    }

    private static List<string> ReadPlotGroupAxisIds(OpenXmlElement plotGroupElement)
    {
        List<string> ids = new();
        foreach (OpenXmlElement child in plotGroupElement.ChildElements)
        {
            if (child.LocalName == "axId")
            {
                string? val = GetAttribute(child, "val");
                if (val is not null) ids.Add(val);
            }
        }
        return ids;
    }

    private static void ReadSeriesCacheInfo(OpenXmlElement seriesElement, List<ChartCacheSummary> caches)
    {
        foreach (string tag in new[] { "cat", "xVal", "val", "yVal" })
        {
            OpenXmlElement? container = FindChild(seriesElement, tag);
            if (container is null) continue;

            foreach (OpenXmlElement sourceElement in container.ChildElements
                         .Where(e => e.LocalName is
                             "numCache" or "strCache" or "numRef" or "strRef" or
                             "numLit" or "strLit" or "multiLvlStrRef"))
            {
                OpenXmlElement cacheEl = sourceElement.LocalName switch
                {
                    "numRef" => FindChild(sourceElement, "numCache") ?? sourceElement,
                    "strRef" => FindChild(sourceElement, "strCache") ?? sourceElement,
                    "multiLvlStrRef" => FindChild(sourceElement, "multiLvlStrCache") ?? sourceElement,
                    _ => sourceElement,
                };
                int ptCount = 0;
                bool hasSparse = false;
                int lastIdx = -1;

                IEnumerable<OpenXmlElement> points = cacheEl.LocalName == "multiLvlStrCache"
                    ? Descendants(cacheEl).Where(e => e.LocalName == "pt")
                    : cacheEl.ChildElements.Where(e => e.LocalName == "pt");
                foreach (OpenXmlElement pt in points)
                {
                    uint idx = ParseIndex(pt);
                    if (lastIdx >= 0 && (int)idx != lastIdx + 1) hasSparse = true;
                    lastIdx = (int)idx;
                    ptCount++;
                }

                caches.Add(new ChartCacheSummary
                {
                    Location = tag, PointCount = ptCount, HasSparsePoints = hasSparse,
                });
            }
        }
    }

    private static uint ParseIndex(OpenXmlElement point)
    {
        string? attr = GetAttribute(point, "idx");
        if (attr is not null && uint.TryParse(
                attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint idx))
            return idx;
        return 0;
    }

    private static int ParseIntegerChildAttribute(
        OpenXmlElement element,
        string childName,
        int fallback)
    {
        string? raw = GetAttribute(FindChild(element, childName), "val");
        return raw is not null &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private static decimal? ParseNumericAttribute(OpenXmlElement? element, string childName)
    {
        if (element is null) return null;
        OpenXmlElement? child = FindChild(element, childName);
        string? val = GetAttribute(child, "val");
        if (val is not null && decimal.TryParse(
                val, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal num))
            return num;
        return null;
    }

    private static bool IsSupportedForBinding(
        string chartType,
        IReadOnlyList<OpenXmlElement> seriesElements,
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories)
    {
        if (chartType is "unsupported" or "surface" or "surface3D")
        {
            return false;
        }

        if (chartType != "radar")
        {
            return true;
        }

        return series.Count > 0 &&
               categories.Count > 0 &&
               seriesElements.Count == series.Count &&
               seriesElements.All(HasWritableCategoryCache) &&
               seriesElements.All(HasWritableValueCache);
    }

    private static void AddRadarDiagnostics(
        IReadOnlyList<OpenXmlElement> seriesElements,
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories,
        ICollection<ChartDiagnosticItem> diagnostics)
    {
        if (series.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "radar_missing_series",
                "雷达图没有数据系列，无法绑定。"));
        }

        if (categories.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "radar_missing_categories",
                "雷达图没有分类指标，无法绑定。"));
        }

        for (int index = 0; index < seriesElements.Count; index++)
        {
            if (!HasWritableValueCache(seriesElements[index]))
            {
                diagnostics.Add(Diagnostic(
                    "radar_missing_value_cache",
                    $"雷达图系列 {index + 1} 没有可写数值缓存。",
                    index));
            }

            if (!HasWritableCategoryCache(seriesElements[index]))
            {
                diagnostics.Add(Diagnostic(
                    "radar_missing_category_cache",
                    $"雷达图系列 {index + 1} 没有可写分类缓存。",
                    index));
            }
        }

        foreach (ChartSeriesSnapshot item in series)
        {
            if (categories.Count > 0 && item.Values.Count != categories.Count)
            {
                diagnostics.Add(Diagnostic(
                    "radar_series_length_mismatch",
                    $"雷达图系列 \"{item.Name}\" 的数值数量 {item.Values.Count} 与指标数量 {categories.Count} 不一致。",
                    item.SeriesIndex));
            }

            if (item.Values.Any(point => point.IsMissing))
            {
                diagnostics.Add(Diagnostic(
                    "radar_missing_values",
                    $"雷达图系列 \"{item.Name}\" 包含缺失值；网页预览将使用轴最小值临时绘制，写回仍保留空值。",
                    item.SeriesIndex));
            }
        }
    }

    private static bool HasWritableCategoryCache(OpenXmlElement seriesElement)
    {
        OpenXmlElement? container = FindChild(seriesElement, "cat")
            ?? FindChild(seriesElement, "xVal");
        return HasCache(
            container,
            ("strRef", "strCache"),
            ("numRef", "numCache"),
            ("multiLvlStrRef", "multiLvlStrCache"),
            ("strLit", "strLit"),
            ("numLit", "numLit"));
    }

    private static bool HasWritableValueCache(OpenXmlElement seriesElement)
    {
        OpenXmlElement? container = FindChild(seriesElement, "val")
            ?? FindChild(seriesElement, "yVal");
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
            {
                return true;
            }
        }
        return false;
    }

    private static ChartDiagnosticItem Diagnostic(
        string code,
        string message,
        int? seriesIndex = null) => new()
    {
        Code = code,
        Level = "warning",
        Message = message,
        SeriesIndex = seriesIndex,
        Recoverable = true,
    };

    private static int CalcCompleteness(
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories,
        IReadOnlyList<ChartDiagnosticItem> diagnostics)
    {
        int score = 100;
        if (series.Count == 0) score -= 30;
        if (categories.Count == 0) score -= 20;
        foreach (ChartSeriesSnapshot s in series)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.StartsWith("系列 ", StringComparison.Ordinal))
                score -= 2;
            if (s.Values.Count == 0) score -= 5;
        }
        if (diagnostics.Any(d => d.Level == "error")) score -= 20;
        if (diagnostics.Any(d => d.Level == "warning")) score -= 5;
        return Math.Max(0, Math.Min(100, score));
    }

    // Helpers to safely read OpenXml attributes (GetAttribute throws on unsupported elements)
    private static string? GetAttribute(OpenXmlElement? element, string attributeName)
    {
        if (element is null) return null;
        try
        {
            OpenXmlAttribute val = element.GetAttribute(attributeName, string.Empty);
            return string.IsNullOrEmpty(val.Value) ? null : val.Value;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static string? GetChildText(OpenXmlElement? element, string childName)
    {
        if (element is null) return null;
        OpenXmlElement? child = element.ChildElements.FirstOrDefault(e => e.LocalName == childName);
        return child?.InnerText;
    }

    private static OpenXmlElement? FindChild(OpenXmlElement? element, string localName)
    {
        if (element is null) return null;
        return element.ChildElements.FirstOrDefault(e => e.LocalName == localName);
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
