import type { EChartsOption } from "echarts";
import type { WordDoughnutChartModel } from "../types";

export function wordDoughnutChartToECharts(model: WordDoughnutChartModel): EChartsOption {
  const categoryData = model.categories.map((c) => c.displayValue ?? c.value);
  const firstSeries = model.series[0];
  const values = firstSeries?.values ?? [];

  const data = categoryData.map((name, i) => ({
    name,
    value: values[i] ?? 0,
  }));

  const holePct = model.holeSize ? `${model.holeSize}%` : "50%";

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "item", formatter: "{b}: {c} ({d}%)" },
    legend: {
      show: model.legend?.visible !== false,
      bottom: 0,
      type: "plain",
    },
    series: [{
      type: "pie",
      data,
      radius: [holePct, "75%"],
      center: ["50%", "45%"],
      label: {
        show: firstSeries?.showValueLabel ?? false,
        formatter: "{b}: {c}",
      },
    }],
  };
}
