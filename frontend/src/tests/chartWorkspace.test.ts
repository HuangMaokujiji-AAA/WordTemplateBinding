import { describe, it, expect } from "vitest";
import { buildChartWorkspace } from "../features/binding/chartWorkspace";
import type { ChartItem } from "../api/types";
import type { ParsedWordChart } from "../features/docx/chart-analysis/models/types";

function makeParsed(partKey: string, relationshipId: string, documentOrder: number): ParsedWordChart {
  return {
    schemaVersion: "1.0",
    identity: {
      chartId: `chart-${documentOrder + 1}-${relationshipId}`,
      slotId: `chart-${documentOrder + 1}-${relationshipId}`,
      partKey,
      relationshipId,
      documentOrder,
      marker: "",
    },
    source: {
      chartPartPath: partKey,
      chartRelationshipPath: null,
      externalDataRelationshipId: null,
      embeddedWorkbookPath: null,
      embeddedWorkbookDetected: false,
      formulas: [],
      cacheSources: [],
      themePath: null,
    },
    type: "column",
    typeLabel: "柱形图",
    supportedForParsing: true,
    supportedForPreview: true,
    supportedForBinding: true,
    title: null,
    autoTitleDeleted: false,
    dimensions: { widthPx: 1, heightPx: 1, widthEmu: null, heightEmu: null },
    layout: { manualLayout: false },
    plotGroups: [],
    axes: [],
    legend: null,
    dataLabels: null,
    categories: [],
    series: [],
    dataTable: { orientation: "categories-as-rows", columns: [], rows: [], categoryColumnKey: null, seriesColumnKeys: [], rowCount: 0, columnCount: 0 },
    bindingSchema: { schemaVersion: "1.0", supportedModes: [], defaultMode: "whole-dataset", slots: [] },
    style: { chartStyleId: null, colorStyleId: null, chartAreaFill: null, plotAreaFill: null, roundedCorners: null },
    diagnostics: { items: [], hasErrors: false, hasWarnings: false, completenessScore: 100, modules: { identity: "complete", data: "complete", axes: "complete", style: "complete", workbook: "missing" } },
  };
}

function makeBackend(partKey: string, relationshipId: string, documentOrder: number, locatorId: string): ChartItem {
  return {
    locatorId,
    locator: { partKey, relationshipId, documentOrder },
    chartType: "Column",
    title: "图表",
    categories: [],
    series: [],
    isBindable: true,
    isBound: false,
    boundDataPath: null,
    boundDataType: null,
    analysis: null,
    dataDefinition: null,
    chartMapping: null,
  };
}

describe("buildChartWorkspace", () => {
  it("matches by partKey + relationshipId first (highest priority)", () => {
    const parsed = [makeParsed("/word/charts/chart1.xml", "rId5", 0)];
    const backend = [makeBackend("/word/charts/chart1.xml", "rId5", 0, "loc-1")];
    const workspace = buildChartWorkspace(parsed, backend);
    expect(workspace[0].locatorId).toBe("loc-1");
    expect(workspace[0].mergeWarnings).toEqual([]);
  });

  it("normalizes partKey (leading slash, backslashes) before matching", () => {
    const parsed = [makeParsed("word/charts/chart1.xml", "rId5", 0)];
    const backend = [makeBackend("/word/charts/chart1.xml", "rId5", 0, "loc-1")];
    const workspace = buildChartWorkspace(parsed, backend);
    expect(workspace[0].locatorId).toBe("loc-1");
  });

  it("falls back to partKey-only match when relationshipId differs, with a warning", () => {
    const parsed = [makeParsed("/word/charts/chart1.xml", "rId99", 0)];
    const backend = [makeBackend("/word/charts/chart1.xml", "rId5", 0, "loc-1")];
    const workspace = buildChartWorkspace(parsed, backend);
    expect(workspace[0].locatorId).toBe("loc-1");
    expect(workspace[0].mergeWarnings.length).toBeGreaterThan(0);
  });

  it("falls back to documentOrder match when partKey also differs, with a warning", () => {
    const parsed = [makeParsed("/word/charts/chartX.xml", "rId99", 2)];
    const backend = [makeBackend("/word/charts/chart3.xml", "rId9", 2, "loc-3")];
    const workspace = buildChartWorkspace(parsed, backend);
    expect(workspace[0].locatorId).toBe("loc-3");
    expect(workspace[0].mergeWarnings.length).toBeGreaterThan(0);
  });

  it("reports an unmatched chart with isBound/canBind false and a warning, not a throw", () => {
    const parsed = [makeParsed("/word/charts/chart1.xml", "rId5", 0)];
    const workspace = buildChartWorkspace(parsed, []);
    expect(workspace[0].backend).toBeNull();
    expect(workspace[0].locatorId).toBeNull();
    expect(workspace[0].canBind).toBe(false);
    expect(workspace[0].mergeWarnings.length).toBeGreaterThan(0);
  });

  it("does not double-assign the same backend chart to two parsed charts", () => {
    const parsed = [
      makeParsed("/word/charts/chart1.xml", "rIdA", 0),
      makeParsed("/word/charts/chart1.xml", "rIdB", 1),
    ];
    const backend = [makeBackend("/word/charts/chart1.xml", "rIdA", 0, "loc-1")];
    const workspace = buildChartWorkspace(parsed, backend);
    expect(workspace[0].locatorId).toBe("loc-1");
    expect(workspace[1].locatorId).toBeNull();
  });

  it("keeps backend as the source of truth for isBound/boundDataPath", () => {
    const parsed = [makeParsed("/word/charts/chart1.xml", "rId5", 0)];
    const backend = makeBackend("/word/charts/chart1.xml", "rId5", 0, "loc-1");
    const bound: ChartItem = { ...backend, isBound: true, boundDataPath: "Students.Scores" };
    const workspace = buildChartWorkspace(parsed, [bound]);
    expect(workspace[0].isBound).toBe(true);
    expect(workspace[0].boundDataPath).toBe("Students.Scores");
  });
});
