import type { EChartsOption } from "echarts";
import type { WordPieChartModel } from "../types";

export function wordPieChartToECharts(model: WordPieChartModel): EChartsOption {
  const categoryData = model.categories.map((c) => c.displayValue ?? c.value);
  const firstSeries = model.series[0];
  const values = firstSeries?.values ?? [];

  // Build pie data array of { name, value }
  const data = categoryData.map((name, i) => ({
    name,
    value: values[i] ?? 0,
  }));

  return {
    animation: false,
    backgroundColor: "transparent",
    textStyle: { fontFamily: '"Microsoft YaHei","PingFang SC",Arial,sans-serif' },
    tooltip: { trigger: "item", formatter: "{b}: {c} ({d}%)" },
    legend: buildLegend(model),
    series: [{
      type: "pie",
      data,
      radius: model.explosion ? ["0%", "60%"] : "60%",
      center: ["50%", "50%"],
      label: {
        show: firstSeries?.showValueLabel ?? false,
        formatter: "{b}: {c}",
      },
      emphasis: {
        itemStyle: { shadowBlur: 0 },
      },
    }],
  };
}

function buildLegend(model: WordPieChartModel): Record<string, unknown> {
  if (!model.legend?.visible) return { show: false };
  const pos: Record<string, unknown> = { show: true, type: "plain" };
  switch (model.legend.position) {
    case "top": pos.top = 0; break;
    case "bottom": pos.bottom = 0; break;
    case "left": pos.left = 0; pos.orient = "vertical"; break;
    case "right": pos.right = 0; pos.orient = "vertical"; break;
    default: pos.bottom = 0;
  }
  return pos;
}
