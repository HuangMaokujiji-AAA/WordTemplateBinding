import { describe, expect, it } from "vitest";
import type { WordRadarChartModel } from "../features/docx/chart-recognition/types";
import { wordRadarChartToECharts } from "../features/docx/chart-recognition/mappers/wordRadarChartToECharts";
import { resolveRadarScale } from "../features/docx/chart-recognition/utils/radarScale";

function model(
  overrides: Partial<WordRadarChartModel> = {}
): WordRadarChartModel {
  return {
    id: "radar-1",
    relationshipId: "rId5",
    sourcePath: "/word/charts/chart1.xml",
    type: "radar",
    radarStyle: "standard",
    showMarker: false,
    filled: false,
    categories: ["创新", "培养", "师资", "科研", "服务"].map((value) => ({ value })),
    series: [
      { name: "学校值", values: [83, 75, 90, 68, 82], color: "#4472C4" },
      { name: "全省值", values: [72, 70, 78, 74, 76], color: "#ED7D31" },
    ],
    legend: { visible: true, position: "bottom" },
    widthPx: 560,
    heightPx: 320,
    ...overrides,
  };
}

describe("resolveRadarScale", () => {
  it.each([
    [83, 100],
    [543, 600],
    [1.7, 2],
  ])("rounds %s to readable max %s", (value, expected) => {
    expect(resolveRadarScale([value]).max).toBe(expected);
  });

  it("keeps a negative minimum and produces a valid equal-value range", () => {
    expect(resolveRadarScale([-17, -5])).toMatchObject({ min: -20, max: 0 });
    const equal = resolveRadarScale([5, 5]);
    expect(equal.max).toBeGreaterThan(equal.min);
  });

  it("uses safe defaults for empty data", () => {
    expect(resolveRadarScale([])).toMatchObject({ min: 0, max: 100 });
  });
});

describe("wordRadarChartToECharts", () => {
  it("maps indicators, multiple series, Word range, colors and legend", () => {
    const option = wordRadarChartToECharts(model({ min: 0, max: 100 })) as Record<string, any>;
    expect(option.radar.indicator).toHaveLength(5);
    expect(option.radar.indicator.map((item: { name: string }) => item.name))
      .toEqual(["创新", "培养", "师资", "科研", "服务"]);
    expect(option.radar.indicator[0]).toMatchObject({ min: 0, max: 100 });
    expect(option.series[0].data).toHaveLength(2);
    expect(option.series[0].data[0].value).toEqual([83, 75, 90, 68, 82]);
    expect(option.series[0].data[0].lineStyle.color).toBe("#4472C4");
    expect(option.legend).toMatchObject({ show: true, bottom: 0 });
  });

  it("maps standard, marker and filled styles", () => {
    const standard = wordRadarChartToECharts(model()) as Record<string, any>;
    expect(standard.series[0].data[0].symbol).toBe("none");
    expect(standard.series[0].data[0].areaStyle).toBeUndefined();

    const marker = wordRadarChartToECharts(model({
      radarStyle: "marker",
      showMarker: true,
    })) as Record<string, any>;
    expect(marker.series[0].data[0].symbol).toBe("circle");
    expect(marker.series[0].data[0].symbolSize).toBe(6);

    const filled = wordRadarChartToECharts(model({
      radarStyle: "filled",
      filled: true,
    })) as Record<string, any>;
    expect(filled.series[0].data[0].areaStyle).toMatchObject({ opacity: 0.25 });
  });

  it("temporarily maps null to indicator minimum without mutating the model", () => {
    const source = model({
      min: -20,
      max: 100,
      series: [{ name: "学校值", values: [80, null, 60, 50, 40] }],
    });
    const option = wordRadarChartToECharts(source) as Record<string, any>;
    expect(option.series[0].data[0].value[1]).toBe(-20);
    expect(source.series[0].values[1]).toBeNull();
  });

  it("hides the legend and rejects structurally invalid indicators", () => {
    const hidden = wordRadarChartToECharts(model({
      legend: { visible: false },
    })) as Record<string, any>;
    expect(hidden.legend.show).toBe(false);

    expect(() => wordRadarChartToECharts(model({ categories: [] })))
      .toThrow("雷达图至少需要 3 个分类指标");
  });
});
