import * as echarts from "echarts";
import type { WordDoughnutChartModel } from "../types";
import { wordDoughnutChartToECharts } from "../mappers/wordDoughnutChartToECharts";

export function renderDoughnutChart(
  model: WordDoughnutChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordDoughnutChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render doughnut chart:", err);
    return null;
  }
}
