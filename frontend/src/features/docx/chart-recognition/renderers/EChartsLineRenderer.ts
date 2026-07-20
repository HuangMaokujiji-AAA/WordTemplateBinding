import * as echarts from "echarts";
import type { WordLineChartModel } from "../types";
import { wordLineChartToECharts } from "../mappers/wordLineChartToECharts";

export function renderLineChart(
  model: WordLineChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordLineChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render line chart:", err);
    return null;
  }
}
