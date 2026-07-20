import type { EChartsOption } from "echarts";
import type { WordComboChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";

export function wordComboChartToECharts(model: WordComboChartModel): EChartsOption {
  const categoryData = model.categories.map((c) => c.displayValue ?? c.value);
  let colorIdx = 0;

  const series = model.series.map((s) => {
    const color = s.color ?? DEFAULT_OFFICE_PALETTE[colorIdx++ % DEFAULT_OFFICE_PALETTE.length];
    const isLine = s.chartType === "line";
    const isSecondary = s.axis === "secondary";

    const item: Record<string, unknown> = {
      type: isLine ? "line" : "bar",
      name: s.name,
      data: s.values,
      itemStyle: { color },
    };

    if (isLine) {
      item.lineStyle = { color, width: 2 };
      item.symbol = "circle";
      item.symbolSize = 6;
    }

    if (isSecondary) {
      item.yAxisIndex = 1;
    }

    if (s.showValueLabel) {
      item.label = {
        show: true,
        position: isLine ? "top" : "insideTop",
        fontSize: 10,
        color: "#333",
      };
    }

    return item;
  });

  const xAxis: Record<string, unknown> = {
    type: "category",
    data: categoryData,
    axisLabel: { interval: 0 },
    name: model.xAxis?.title,
  };

  const yAxis: Record<string, unknown>[] = [{
    type: "value",
    name: model.yAxis?.title,
    min: model.yAxis?.min,
    max: model.yAxis?.max,
  }];

  if (model.useSecondaryAxis && model.secondaryYAxis) {
    yAxis.push({
      type: "value",
      name: model.secondaryYAxis.title,
      min: model.secondaryYAxis.min,
      max: model.secondaryYAxis.max,
    });
  }

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "axis" },
    legend: buildLegend(model),
    grid: { containLabel: true, left: 24, right: model.useSecondaryAxis ? 60 : 20, top: 28, bottom: 28 },
    xAxis,
    yAxis,
    series,
  };
}

function buildLegend(model: WordComboChartModel): Record<string, unknown> {
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
