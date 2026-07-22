import { describe, expect, it, vi } from "vitest";
import type { ChartItem } from "../api/types";
import {
  decorateRenderedCharts,
  refreshChartBindingTargetStates,
} from "../features/binding/renderedChartBindings";

function createChart(overrides: Partial<ChartItem> = {}): ChartItem {
  return {
    locatorId: "chart-locator-1",
    locator: {
      partKey: "/word/charts/chart1.xml",
      relationshipId: "rId7",
      documentOrder: 0,
    },
    chartType: "bar",
    title: "学生成绩",
    categories: ["四年级", "八年级"],
    series: [
      { seriesIndex: 0, name: "你县", values: [543, 505] },
      { seriesIndex: 1, name: "全省", values: [506, 493] },
    ],
    isBindable: true,
    isBound: false,
    boundDataPath: null,
    boundDataType: null,
    analysis: null,
    dataDefinition: null,
    chartMapping: null,
    ...overrides,
  };
}

describe("rendered chart bindings", () => {
  it("matches a backend ChartPart to its ECharts target and selects it", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <span class="docx-chart-slot" data-chart-part-key="word/charts/chart1.xml"></span>
    `;
    const onSelect = vi.fn();
    const chart = createChart();

    const result = decorateRenderedCharts(container, [chart], {
      onSelect,
      onBind: vi.fn(),
      onError: vi.fn(),
    });
    const target = container.querySelector<HTMLElement>(".docx-chart-slot")!;
    target.click();

    expect(result.renderedCount).toBe(1);
    expect(result.unresolvedLocatorIds).toEqual([]);
    expect(target.dataset.chartLocatorId).toBe(chart.locatorId);
    expect(target.classList.contains("template-chart-target")).toBe(true);
    expect(onSelect).toHaveBeenCalledWith(chart);
  });

  it("refreshes the bound state without rebuilding the chart", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <span
        class="docx-chart-slot template-chart-target"
        data-chart-part-key="/word/charts/chart1.xml"
        data-chart-locator-id="chart-locator-1"
      ></span>
    `;
    const chart = createChart({
      isBound: true,
      boundDataPath: "ChartData.ScienceScores",
      boundDataType: "Array",
    });

    refreshChartBindingTargetStates(container, [chart]);

    const target = container.querySelector<HTMLElement>(".docx-chart-slot")!;
    expect(target.classList.contains("is-bound")).toBe(true);
    expect(target.title).toContain("ChartData.ScienceScores");
  });

  it("reports a chart whose part is absent from the rendered preview", () => {
    const result = decorateRenderedCharts(document.createElement("div"), [createChart()], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });

    expect(result.renderedCount).toBe(0);
    expect(result.unresolvedLocatorIds).toEqual(["chart-locator-1"]);
  });
});
