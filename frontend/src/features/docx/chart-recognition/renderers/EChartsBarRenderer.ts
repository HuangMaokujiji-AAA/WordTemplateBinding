import * as echarts from "echarts";
import type { WordChartModel } from "../types";
import type { WordBarChartModel } from "../types";
import { wordBarChartToECharts } from "../mappers/wordBarChartToECharts";

/**
 * Render a bar/column chart using ECharts into a container element.
 *
 * Returns an ECharts instance that the caller must dispose when done.
 * The instance is registered with the chartInstanceManager.
 */
export function renderBarChart(
  model: WordChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const barModel = model as WordBarChartModel;

    if (barModel.type !== "bar" && barModel.type !== "column") {
      console.warn(
        `renderBarChart called with non-bar model: ${barModel.type}`
      );
      return null;
    }

    const option = wordBarChartToECharts(barModel);

    // Add grid adjustments for bar charts (need more left space for labels)
    if (barModel.type === "bar") {
      const gridOption = option.grid as Record<string, unknown> | undefined;
      if (gridOption) {
        // Horizontal bar charts need more left space for category labels
        gridOption.left = Math.max(
          (gridOption.left as number) ?? 24,
          estimateLabelWidth(barModel) + 10
        );
        gridOption.containLabel = true;
      }
    }

    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || barModel.widthPx || 560,
      height: container.clientHeight || barModel.heightPx || 320,
    });

    instance.setOption(option);

    return instance;
  } catch (err) {
    console.error("Failed to render bar chart:", err);
    return null;
  }
}

/**
 * Estimate the maximum label width for horizontal bar charts
 * to reserve adequate left margin.
 */
function estimateLabelWidth(model: WordBarChartModel): number {
  let maxLen = 0;
  for (const cat of model.categories) {
    const text = cat.displayValue ?? cat.value;
    // Rough estimate: CJK characters ~14px, ASCII ~8px at typical font size
    let width = 0;
    for (const ch of text) {
      width += /[一-鿿　-〿＀-￯]/.test(ch) ? 14 : 8;
    }
    maxLen = Math.max(maxLen, width);
  }
  return Math.min(maxLen + 20, 200);
}
