import * as echarts from "echarts";
import type { WordScatterChartModel } from "../types";
import { wordScatterChartToECharts } from "../mappers/wordScatterChartToECharts";

export function renderScatterChart(
  model: WordScatterChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordScatterChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render scatter chart:", err);
    return null;
  }
}
