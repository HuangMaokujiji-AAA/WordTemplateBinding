import * as echarts from "echarts";
import type { WordRadarChartModel } from "../types";
import { wordRadarChartToECharts } from "../mappers/wordRadarChartToECharts";

export function renderRadarChart(
  model: WordRadarChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordRadarChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (error) {
    console.error("Failed to render radar chart:", error);
    return null;
  }
}
