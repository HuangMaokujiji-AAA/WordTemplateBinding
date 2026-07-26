import type { EChartsOption } from "echarts";
import type { WordRadarChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";
import { resolveRadarScale } from "../utils/radarScale";

export function wordRadarChartToECharts(
  model: WordRadarChartModel
): EChartsOption {
  if (model.categories.length < 3) {
    throw new Error("雷达图至少需要 3 个分类指标");
  }

  const scale = resolveRadarScale(
    model.series.flatMap((item) => item.values),
    model.min,
    model.max
  );
  const colors = model.series.map(
    (item, index) =>
      item.color ?? DEFAULT_OFFICE_PALETTE[index % DEFAULT_OFFICE_PALETTE.length]
  );

  const data = model.series.map((item, index) => {
    const color = colors[index];
    const showMarker =
      item.markerSymbol != null
        ? item.markerSymbol !== "none"
        : model.showMarker;
    const entry: Record<string, unknown> = {
      name: item.name,
      value: model.categories.map((_, pointIndex) => {
        const value = item.values[pointIndex];
        return value == null || !Number.isFinite(value) ? scale.min : value;
      }),
      itemStyle: { color },
      lineStyle: { color, width: 2 },
      symbol: showMarker ? mapRadarSymbol(item.markerSymbol) : "none",
      symbolSize: showMarker ? item.markerSize ?? 6 : 0,
    };

    if (model.filled) {
      entry.areaStyle = { color, opacity: 0.25 };
    }
    if (item.showValueLabel) {
      entry.label = {
        show: true,
        color: "#333",
        fontSize: 10,
        formatter: "{c}",
      };
    }
    return entry;
  });

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: {
      fontFamily:
        '"Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", Arial, sans-serif',
    },
    title: model.title
      ? { show: true, text: model.title, left: "center", top: 0, textStyle: { fontSize: 14 } }
      : undefined,
    tooltip: { trigger: "item" },
    legend: buildLegend(model),
    radar: {
      indicator: model.categories.map((category) => ({
        name: category.displayValue ?? category.value,
        min: scale.min,
        max: scale.max,
      })),
      shape: "polygon",
      splitNumber: 5,
      radius: model.title ? "62%" : "68%",
      center: ["50%", model.title ? "56%" : "50%"],
      axisName: { color: "#333", fontSize: 11 },
    },
    color: colors,
    series: [
      {
        type: "radar",
        data,
        symbol: model.showMarker ? "circle" : "none",
        symbolSize: model.showMarker ? 6 : 0,
      },
    ],
  };
}

function buildLegend(model: WordRadarChartModel): Record<string, unknown> {
  if (!model.legend?.visible) return { show: false };
  const legend: Record<string, unknown> = { show: true };
  switch (model.legend.position) {
    case "top":
      legend.top = model.title ? 24 : 0;
      legend.left = "center";
      break;
    case "bottom":
      legend.bottom = 0;
      legend.left = "center";
      break;
    case "left":
      legend.left = 0;
      legend.top = "center";
      legend.orient = "vertical";
      break;
    case "right":
      legend.right = 0;
      legend.top = "center";
      legend.orient = "vertical";
      break;
    default:
      legend.bottom = 0;
      legend.left = "center";
  }
  return legend;
}

function mapRadarSymbol(symbol?: string): string {
  switch (symbol) {
    case "triangle":
    case "diamond":
    case "circle":
    case "none":
      return symbol;
    case "square":
      return "rect";
    case "star":
      return "diamond";
    default:
      return "circle";
  }
}
