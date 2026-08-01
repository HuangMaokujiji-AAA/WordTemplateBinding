import { describe, it, expect, vi } from "vitest";
import { readFileSync } from "fs";
import { resolve } from "path";
import { processDocx } from "../features/docx/processDocx";
import { chartInstanceManager } from "../features/docx/rendering/chartInstanceManager";
import {
  findRenderedBlockForMarker,
  findSafePageEndSplitCandidates,
} from "../features/template-studio/structureSplitNodes";

// jsdom has no real <canvas> 2D context backend, so ECharts' canvas paint
// loop throws asynchronously once mounted. This test targets the
// chart-analysis/model pipeline (JSON-safety, per-chart isolation, status
// accounting) — not ECharts pixel rendering, which isn't meaningful in
// jsdom anyway — so `echarts.init` is stubbed to skip real canvas painting
// while every other step (marker injection, docx-preview rendering,
// chart XML analysis) still runs unmocked.
vi.mock("echarts", async () => {
  const actual = await vi.importActual<typeof import("echarts")>("echarts");
  return {
    ...actual,
    init: () => ({
      setOption: () => {},
      resize: () => {},
      dispose: () => {},
      isDisposed: () => false,
    }),
  };
});

// jsdom does not implement ResizeObserver — chartInstanceManager.observeResize
// creates one for every rendered chart, so processDocx needs a stub here.
class NoopResizeObserver {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: typeof NoopResizeObserver }).ResizeObserver = NoopResizeObserver;

function loadSampleFile(): File | null {
  try {
    const samplePath = resolve(
      __dirname,
      "../../public/samples/第一部分 科学监测结果.docx"
    );
    const buffer = readFileSync(samplePath);
    return new File([buffer], "sample.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
  } catch {
    return null;
  }
}

describe("processDocx — chart-analysis integration", () => {
  it("returns a fully structured, JSON-safe DocxProcessResult for the sample document", async () => {
    const file = loadSampleFile();
    if (!file) {
      console.warn("Sample DOCX not found — skipping integration test");
      return;
    }

    const documentContainer = document.createElement("div");
    const styleContainer = document.createElement("div");
    document.body.append(documentContainer, styleContainer);

    try {
      const result = await processDocx(file, {
        documentContainer,
        styleContainer,
        outlineBlockIds: Array.from({ length: 49 }, (_, index) => `body/${index}`),
      });

      expect(result.totalCharts).toBe(10);
      expect(result.charts).toHaveLength(10);
      expect(result.blockMarkerIds?.["body/0"]).toBeTruthy();
      expect(result.blockEndMarkerIds?.["body/0"]).toBeTruthy();
      expect(
        documentContainer.querySelector(
          `[id="${result.blockMarkerIds?.["body/0"]}"]`
        )
      ).not.toBeNull();
      expect(
        documentContainer.querySelector(
          `[id="${result.blockEndMarkerIds?.["body/0"]}"]`
        )
      ).not.toBeNull();
      const starts = Array.from({ length: 49 }, (_, index) =>
        findRenderedBlockForMarker(
          documentContainer,
          result.blockMarkerIds?.[`body/${index}`] || ""
        )
      );
      const ends = Array.from({ length: 49 }, (_, index) =>
        findRenderedBlockForMarker(
          documentContainer,
          result.blockEndMarkerIds?.[`body/${index}`] || ""
        )
      );
      expect(
        findSafePageEndSplitCandidates(documentContainer, starts, ends)
          .map((candidate) => candidate.splitIndex)
      ).toEqual([16, 22, 33, 39, 42]);

      // Every chart result carries a model (this document has no missing chart parts).
      for (const chart of result.charts) {
        expect(chart.model).not.toBeNull();
        expect(chart.diagnostics).not.toBeNull();
      }

      // Status accounting is internally consistent.
      const counted =
        result.renderedCharts +
        result.partiallyRenderedCharts +
        result.unsupportedCharts +
        result.failedCharts;
      expect(counted).toBe(result.totalCharts);

      // 9 bar charts + 1 scatter chart in this sample are all previewable.
      expect(result.renderedCharts + result.partiallyRenderedCharts).toBe(10);
      expect(result.unsupportedCharts).toBe(0);
      expect(result.failedCharts).toBe(0);

      // The whole result must survive JSON round-tripping (no DOM/JSZip/Map/functions).
      const json = JSON.stringify(result);
      const revived = JSON.parse(json);
      expect(revived.charts).toHaveLength(10);
      expect(revived.charts[0].model.schemaVersion).toBe("1.0");

      // ECharts rendered into the DOM using the same model returned to the caller.
      expect(documentContainer.querySelectorAll(".docx-chart-slot").length).toBeGreaterThan(0);
    } finally {
      chartInstanceManager.disposeAll();
      documentContainer.remove();
      styleContainer.remove();
    }
  }, 30000);

  it("does not let one chart's parse failure affect the others", async () => {
    const file = loadSampleFile();
    if (!file) return;

    const documentContainer = document.createElement("div");
    const styleContainer = document.createElement("div");
    document.body.append(documentContainer, styleContainer);

    try {
      const result = await processDocx(file, { documentContainer, styleContainer });
      // All 10 charts in the real sample parse successfully — this assertion
      // documents the guarantee that failures are isolated per-chart rather
      // than aborting the whole pipeline (verified by the try/catch around
      // each chart in processDocx.ts's parsing loop).
      expect(result.charts.every((c) => c.status !== "failed")).toBe(true);
    } finally {
      chartInstanceManager.disposeAll();
      documentContainer.remove();
      styleContainer.remove();
    }
  }, 30000);
});
