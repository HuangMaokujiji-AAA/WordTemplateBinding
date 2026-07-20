import * as echarts from "echarts";
import type { WordComboChartModel } from "../types";
import { wordComboChartToECharts } from "../mappers/wordComboChartToECharts";

export function renderComboChart(
  model: WordComboChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordComboChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render combo chart:", err);
    return null;
  }
}
