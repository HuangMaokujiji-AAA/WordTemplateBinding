import type { EChartsOption } from "echarts";
import type { WordScatterChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";

export function wordScatterChartToECharts(model: WordScatterChartModel): EChartsOption {
  const isSmooth = model.scatterStyle === "smooth";
  const hasLines = model.scatterStyle?.includes("line") ?? true;
  const hasMarkers = model.scatterStyle?.includes("marker") ?? true;

  const series = model.series.map((s, i) => {
    const color = s.color ?? DEFAULT_OFFICE_PALETTE[i % DEFAULT_OFFICE_PALETTE.length];

    // Build [[x, y], ...] pairs, filtering out null values
    const pairs: Array<[number, number]> = [];
    for (let j = 0; j < s.values.length; j++) {
      const y = s.values[j];
      const x = (s.xValues && j < s.xValues.length) ? s.xValues[j] : j;
      if (x !== null && y !== null) {
        pairs.push([x, y]);
      }
    }

    // Sort by x-value so lines draw correctly
    pairs.sort((a, b) => a[0] - b[0]);

    // Build ECharts series config
    const item: Record<string, unknown> = {
      name: s.name,
      data: pairs,
      itemStyle: { color },
    };

    if (hasLines) {
      // Line series with optional markers
      item.type = "line";
      item.lineStyle = { color, width: 1.5 };
      item.showSymbol = hasMarkers;
      item.symbol = "circle";
      item.symbolSize = hasMarkers ? 6 : 0;
      item.smooth = isSmooth;
    } else {
      // Pure scatter (markers only, no connecting lines)
      item.type = "scatter";
      item.symbol = "circle";
      item.symbolSize = 6;
    }

    if (s.showValueLabel) {
      item.label = { show: true, position: "top", fontSize: 10, color: "#333" };
    }

    // Use connectNulls so gaps in data don't break lines
    if (hasLines) {
      item.connectNulls = false;
    }

    return item;
  });

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "axis" },
    legend: buildLegend(model),
    grid: { containLabel: true, left: 24, right: 20, top: 28, bottom: 28 },
    xAxis: {
      type: "value",
      name: model.xAxis?.title,
      min: model.xAxis?.min,
      max: model.xAxis?.max,
      nameLocation: "middle",
      nameGap: 30,
    },
    yAxis: {
      type: "value",
      name: model.yAxis?.title,
      min: model.yAxis?.min,
      max: model.yAxis?.max,
      nameLocation: "middle",
      nameGap: 40,
    },
    series,
  };
}

function buildLegend(model: WordScatterChartModel): Record<string, unknown> {
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
