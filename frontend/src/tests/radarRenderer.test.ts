import { beforeEach, describe, expect, it, vi } from "vitest";
import type { WordRadarChartModel } from "../features/docx/chart-recognition/types";

const mocks = vi.hoisted(() => {
  const setOption = vi.fn();
  const instance = { setOption, resize: vi.fn(), dispose: vi.fn(), isDisposed: vi.fn(() => false) };
  return { setOption, instance, init: vi.fn(() => instance) };
});

vi.mock("echarts", () => ({ init: mocks.init }));

import { renderRadarChart } from "../features/docx/chart-recognition/renderers/EChartsRadarRenderer";

function radarModel(): WordRadarChartModel {
  return {
    id: "radar-1",
    relationshipId: "rId1",
    sourcePath: "/word/charts/chart1.xml",
    type: "radar",
    radarStyle: "marker",
    showMarker: true,
    filled: false,
    categories: ["A", "B", "C"].map((value) => ({ value })),
    series: [{ name: "S1", values: [1, 2, 3] }],
    widthPx: 480,
    heightPx: 300,
  };
}

describe("renderRadarChart", () => {
  beforeEach(() => {
    mocks.init.mockClear();
    mocks.setOption.mockClear();
  });

  it("initializes ECharts, sets the radar option and returns the instance", () => {
    const container = document.createElement("div");
    const result = renderRadarChart(radarModel(), container);
    expect(result).toBe(mocks.instance);
    expect(mocks.init).toHaveBeenCalledWith(
      container,
      undefined,
      expect.objectContaining({ width: 480, height: 300 })
    );
    expect(mocks.setOption).toHaveBeenCalledWith(
      expect.objectContaining({ radar: expect.any(Object) })
    );
  });

  it("returns null when model conversion fails", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const result = renderRadarChart(
      { ...radarModel(), categories: [] },
      document.createElement("div")
    );
    expect(result).toBeNull();
    expect(consoleError).toHaveBeenCalled();
    consoleError.mockRestore();
  });
});
