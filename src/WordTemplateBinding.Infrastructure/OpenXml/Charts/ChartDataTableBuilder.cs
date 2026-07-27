using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 从图表分析结果构建标准化二维数据表。
/// 散点图和气泡图使用 xy-pairs 格式，分类图使用 categories-as-rows 格式。
/// </summary>
internal static class ChartDataTableBuilder
{
    internal static ChartDataTableSnapshot Build(
        string chartType,
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories)
    {
        bool isScatter = chartType is "scatter" or "bubble";

        if (isScatter)
        {
            return BuildScatterTable(series);
        }

        return BuildCategoryTable(series, categories);
    }

    private static ChartDataTableSnapshot BuildCategoryTable(
        IReadOnlyList<ChartSeriesSnapshot> series,
        IReadOnlyList<ChartCategorySnapshot> categories)
    {
        List<ChartDataColumnSnapshot> columns = new()
        {
            new ChartDataColumnSnapshot
            {
                Key = "category",
                Label = "Category",
                Role = "category",
                ValueType = "string",
                SeriesKey = null,
            },
        };

        foreach (ChartSeriesSnapshot s in series)
        {
            columns.Add(new ChartDataColumnSnapshot
            {
                Key = s.Key,
                Label = s.Name,
                Role = "value",
                ValueType = "number",
                SeriesKey = s.Key,
            });
        }

        int rowCount = categories.Count > 0
            ? categories.Count
            : series.Count == 0
                ? 0
                : series.Max(s => s.Values.Count);

        List<ChartDataRowSnapshot> rows = new(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            Dictionary<string, object?> cells = new()
            {
                ["category"] = i < categories.Count ? categories[i].Value : null,
            };
            Dictionary<string, bool> missing = new()
            {
                ["category"] = i >= categories.Count || categories[i].IsMissing,
            };

            foreach (ChartSeriesSnapshot s in series)
            {
                if (i < s.Values.Count)
                {
                    cells[s.Key] = s.Values[i].Value;
                    missing[s.Key] = s.Values[i].IsMissing;
                }
                else
                {
                    cells[s.Key] = null;
                    missing[s.Key] = true;
                }
            }

            rows.Add(new ChartDataRowSnapshot
            {
                Index = i,
                Cells = cells.AsReadOnly(),
                Missing = missing.AsReadOnly(),
            });
        }

        return new ChartDataTableSnapshot
        {
            Orientation = "categories-as-rows",
            Columns = columns.AsReadOnly(),
            Rows = rows.AsReadOnly(),
            RowCount = rowCount,
            ColumnCount = columns.Count,
        };
    }

    private static ChartDataTableSnapshot BuildScatterTable(
        IReadOnlyList<ChartSeriesSnapshot> series)
    {
        List<ChartDataColumnSnapshot> columns = new()
        {
            new ChartDataColumnSnapshot
            {
                Key = "seriesKey",
                Label = "Series",
                Role = "series",
                ValueType = "string",
                SeriesKey = null,
            },
            new ChartDataColumnSnapshot
            {
                Key = "x",
                Label = "X",
                Role = "x-value",
                ValueType = "number",
                SeriesKey = null,
            },
            new ChartDataColumnSnapshot
            {
                Key = "y",
                Label = "Y",
                Role = "y-value",
                ValueType = "number",
                SeriesKey = null,
            },
            new ChartDataColumnSnapshot
            {
                Key = "bubbleSize",
                Label = "Bubble Size",
                Role = "bubble-size",
                ValueType = "number",
                SeriesKey = null,
            },
        };

        List<ChartDataRowSnapshot> rows = new();
        int rowIndex = 0;

        foreach (ChartSeriesSnapshot s in series)
        {
            int xCount = s.XValues.Count;
            int yCount = s.YValues.Count;
            int bubbleCount = s.BubbleSizes.Count;
            int total = Math.Max(Math.Max(xCount, yCount), bubbleCount);

            if (total == 0)
            {
                // For scatter, also check Values (which may hold combined data)
                total = s.Values.Count;
                for (int i = 0; i < total; i++, rowIndex++)
                {
                    rows.Add(new ChartDataRowSnapshot
                    {
                        Index = rowIndex,
                        Cells = new Dictionary<string, object?>
                        {
                            ["seriesKey"] = s.Key,
                            ["x"] = i < s.XValues.Count ? s.XValues[i].Value : (i < s.Values.Count ? s.Values[i].Value : null),
                            ["y"] = i < s.YValues.Count ? s.YValues[i].Value : null,
                            ["bubbleSize"] = null,
                        }.AsReadOnly(),
                        Missing = new Dictionary<string, bool>
                        {
                            ["seriesKey"] = false,
                            ["x"] = false,
                            ["y"] = false,
                            ["bubbleSize"] = false,
                        }.AsReadOnly(),
                    });
                }
            }
            else
            {
                for (int i = 0; i < total; i++, rowIndex++)
                {
                    rows.Add(new ChartDataRowSnapshot
                    {
                        Index = rowIndex,
                        Cells = new Dictionary<string, object?>
                        {
                            ["seriesKey"] = s.Key,
                            ["x"] = i < xCount ? s.XValues[i].Value : null,
                            ["y"] = i < yCount ? s.YValues[i].Value : null,
                            ["bubbleSize"] = i < bubbleCount ? s.BubbleSizes[i].Value : null,
                        }.AsReadOnly(),
                        Missing = new Dictionary<string, bool>
                        {
                            ["seriesKey"] = false,
                            ["x"] = i >= xCount || s.XValues[i].IsMissing,
                            ["y"] = i >= yCount || (i < yCount && s.YValues[i].IsMissing),
                            ["bubbleSize"] = i >= bubbleCount,
                        }.AsReadOnly(),
                    });
                }
            }
        }

        return new ChartDataTableSnapshot
        {
            Orientation = "xy-pairs",
            Columns = columns.AsReadOnly(),
            Rows = rows.AsReadOnly(),
            RowCount = rows.Count,
            ColumnCount = columns.Count,
        };
    }
}
