import type { EChartsOption } from "echarts";
import type { WordBarChartModel } from "../types";
import { DEFAULT_OFFICE_PALETTE } from "../utils/colorUtils";

/**
 * Convert a WordBarChartModel into an ECharts EChartsOption.
 *
 * This mapper ONLY handles model → ECharts transformation.
 * It does NOT read XML — that responsibility belongs to the parsers.
 *
 * Mapping rules:
 *  - type "column" → xAxis category, yAxis value
 *  - type "bar"    → xAxis value, yAxis category
 *  - grouping "stacked" → stack: "word-chart-stack"
 *  - grouping "percentStacked" → stack + yAxis max 100
 *  - grouping "clustered" / "standard" → no stack
 */
export function wordBarChartToECharts(
  model: WordBarChartModel
): EChartsOption {
  const isBar = model.type === "bar";
  const isStacked =
    model.grouping === "stacked" || model.grouping === "percentStacked";
  const isPercentStacked = model.grouping === "percentStacked";

  // Build category data for axes
  const categoryData = model.categories.map((cat) => cat.displayValue ?? cat.value);

  // Build series
  const series = model.series.map((s, i) => {
    const color =
      s.color ?? DEFAULT_OFFICE_PALETTE[i % DEFAULT_OFFICE_PALETTE.length];

    const echartsSeries: Record<string, unknown> = {
      type: "bar",
      name: s.name,
      data: s.values,
      itemStyle: {
        color,
      },
    };

    if (isStacked) {
      echartsSeries.stack = "word-chart-stack";
    }

    // Data labels
    if (s.showValueLabel) {
      const labelPos = mapDataLabelPosition(
        s.dataLabelPosition,
        model.type === "column" ? "column" : "bar"
      );
      echartsSeries.label = {
        show: true,
        position: labelPos,
        color: "#333",
        fontSize: 10,
      };

      if (isPercentStacked) {
        (echartsSeries.label as Record<string, unknown>).formatter = "{c}%";
      }
    }

    return echartsSeries;
  });

  // Build axes
  const xAxis: Record<string, unknown> = isBar
    ? { type: "value" }
    : {
        type: "category",
        data: categoryData,
        axisLabel: {
          interval: 0,
          overflow: "truncate",
          width: 120,
        },
        axisTick: { alignWithLabel: true },
      };

  const yAxis: Record<string, unknown> = isBar
    ? {
        type: "category",
        data: categoryData,
        axisLabel: {
          interval: 0,
          overflow: "truncate",
          width: 120,
        },
      }
    : { type: "value" };

  // Axis titles
  if (model.xAxis?.title) {
    (xAxis as Record<string, unknown>).name = model.xAxis.title;
  }
  if (model.yAxis?.title) {
    (yAxis as Record<string, unknown>).name = model.yAxis.title;
  }

  // Percent stacked: yAxis max = 100
  if (isPercentStacked && !isBar) {
    (yAxis as Record<string, unknown>).max = 100;
  }
  if (isPercentStacked && isBar) {
    (xAxis as Record<string, unknown>).max = 100;
  }

  // Axis min/max
  if (model.yAxis?.min !== undefined && !isBar) {
    (yAxis as Record<string, unknown>).min = model.yAxis.min;
  }
  if (model.yAxis?.max !== undefined && !isBar) {
    (yAxis as Record<string, unknown>).max = model.yAxis.max;
  }

  // Reverse axis if needed
  if (model.xAxis?.reversed) {
    (xAxis as Record<string, unknown>).inverse = true;
  }
  if (model.yAxis?.reversed) {
    (yAxis as Record<string, unknown>).inverse = true;
  }

  // Legend
  const legend: Record<string, unknown> = {};
  if (model.legend?.visible) {
    legend.show = true;
    switch (model.legend.position) {
      case "top":
        legend.top = 0;
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
  } else {
    legend.show = false;
  }

  // Bar gap — Word gapWidth maps roughly to ECharts barCategoryGap.
  // Kept available for future fine-tuning; currently ECharts defaults are close enough.

  const option: EChartsOption = {
    animation: false,
    backgroundColor: "transparent",
    textStyle: {
      fontFamily:
        '"Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", Arial, sans-serif',
    },
    tooltip: {
      trigger: "axis",
    },
    legend,
    grid: {
      containLabel: true,
      left: 24,
      right: 20,
      top: 28,
      bottom: 28,
    },
    xAxis,
    yAxis,
    series,
  };

  return option;
}

/**
 * Map Word data label positions to ECharts positions.
 */
function mapDataLabelPosition(
  wordPos: string | undefined,
  chartDirection: "column" | "bar"
): string {
  if (chartDirection === "column") {
    switch (wordPos) {
      case "outEnd":
        return "top";
      case "inEnd":
        return "insideTop";
      case "inBase":
        return "insideBottom";
      case "ctr":
        return "inside";
      default:
        return "top";
    }
  } else {
    // Horizontal bar
    switch (wordPos) {
      case "outEnd":
        return "right";
      case "inEnd":
        return "insideRight";
      case "inBase":
        return "insideLeft";
      case "ctr":
        return "inside";
      default:
        return "right";
    }
  }
}
