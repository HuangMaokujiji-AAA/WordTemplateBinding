import type { EChartsOption } from "echarts";
import type { WordAreaChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";

export function wordAreaChartToECharts(model: WordAreaChartModel): EChartsOption {
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
      lineStyle: { color, width: 1.5 },
      areaStyle: { color, opacity: 0.25 },
      symbol: "none",
    };
    if (isStacked) item.stack = "word-area-stack";
    if (s.showValueLabel) {
      item.label = { show: true, position: "top", fontSize: 10 };
    }
    return item;
  });

  const yAxis: Record<string, unknown> = { type: "value" };
  if (isPercent) (yAxis as Record<string, unknown>).max = 100;
  if (model.yAxis?.title) (yAxis as Record<string, unknown>).name = model.yAxis.title;
  if (model.yAxis?.min !== undefined) (yAxis as Record<string, unknown>).min = model.yAxis.min;

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "axis" },
    legend: buildLegend(model),
    grid: { containLabel: true, left: 24, right: 20, top: 28, bottom: 28 },
    xAxis: { type: "category", data: categoryData, axisLabel: { interval: 0 }, name: model.xAxis?.title },
    yAxis,
    series,
  };
}

function buildLegend(model: WordAreaChartModel): Record<string, unknown> {
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
