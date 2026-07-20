import type { EChartsOption } from "echarts";
import type { WordLineChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";

export function wordLineChartToECharts(model: WordLineChartModel): EChartsOption {
  const categoryData = model.categories.map((c) => c.displayValue ?? c.value);
  const isStacked = model.grouping === "stacked" || model.grouping === "percentStacked";
  const isPercent = model.grouping === "percentStacked";

  const series = model.series.map((s, i) => {
    const color = s.color ?? DEFAULT_OFFICE_PALETTE[i % DEFAULT_OFFICE_PALETTE.length];
    const item: Record<string, unknown> = {
      type: "line",
      name: s.name,
      data: s.values,
      itemStyle: { color },
      lineStyle: { color, width: 2 },
      symbol: model.showMarker !== false ? "circle" : "none",
      symbolSize: model.showMarker !== false ? 6 : 0,
      smooth: model.smooth ?? false,
    };
    if (isStacked) {
      item.stack = "word-line-stack";
      item.areaStyle = {};
    }
    if (s.showValueLabel) {
      item.label = { show: true, position: "top", fontSize: 10, color: "#333" };
    }
    return item;
  });

  const yAxis: Record<string, unknown> = { type: "value" };
  if (isPercent) (yAxis as Record<string, unknown>).max = 100;
  if (model.yAxis?.min !== undefined) (yAxis as Record<string, unknown>).min = model.yAxis.min;
  if (model.yAxis?.max !== undefined) (yAxis as Record<string, unknown>).max = model.yAxis.max;
  if (model.yAxis?.title) (yAxis as Record<string, unknown>).name = model.yAxis.title;

  const xAxis: Record<string, unknown> = {
    type: "category",
    data: categoryData,
    axisLabel: { interval: 0 },
  };
  if (model.xAxis?.title) (xAxis as Record<string, unknown>).name = model.xAxis.title;

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "axis" },
    legend: buildLegend(model),
    grid: { containLabel: true, left: 24, right: 20, top: 28, bottom: 28 },
    xAxis,
    yAxis,
    series,
  };
}

function buildLegend(model: WordLineChartModel): Record<string, unknown> {
  if (!model.legend?.visible) return { show: false };
  const pos: Record<string, unknown> = { show: true };
  switch (model.legend.position) {
    case "top": pos.top = 0; pos.left = "center"; break;
    case "bottom": pos.bottom = 0; pos.left = "center"; break;
    case "left": pos.left = 0; pos.top = "center"; pos.orient = "vertical"; break;
    case "right": pos.right = 0; pos.top = "center"; pos.orient = "vertical"; break;
    default: pos.bottom = 0; pos.left = "center";
  }
  return pos;
}
