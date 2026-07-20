import * as echarts from "echarts";
import type { WordPieChartModel } from "../types";
import { wordPieChartToECharts } from "../mappers/wordPieChartToECharts";

export function renderPieChart(
  model: WordPieChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordPieChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render pie chart:", err);
    return null;
  }
}
