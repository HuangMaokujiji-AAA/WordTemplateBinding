import * as echarts from "echarts";
import type { WordAreaChartModel } from "../types";
import { wordAreaChartToECharts } from "../mappers/wordAreaChartToECharts";

export function renderAreaChart(
  model: WordAreaChartModel,
  container: HTMLElement
): echarts.ECharts | null {
  try {
    const option = wordAreaChartToECharts(model);
    const instance = echarts.init(container, undefined, {
      width: container.clientWidth || model.widthPx || 560,
      height: container.clientHeight || model.heightPx || 320,
    });
    instance.setOption(option);
    return instance;
  } catch (err) {
    console.error("Failed to render area chart:", err);
    return null;
  }
}
