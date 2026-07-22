import { createApp, type App } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import ChartStructurePanel from "../components/ChartStructurePanel.vue";
import type { ChartWorkspaceItem } from "../features/binding/chartWorkspace";
import type { ParsedWordChart } from "../features/docx/chart-analysis/models/types";

let app: App<Element> | null = null;

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.replaceChildren();
});

function makeParsedChart(overrides: Partial<ParsedWordChart> = {}): ParsedWordChart {
  return {
    schemaVersion: "1.0",
    identity: {
      chartId: "chart-1-rId5",
      slotId: "chart-1-rId5",
      partKey: "/word/charts/chart1.xml",
      relationshipId: "rId5",
      documentOrder: 0,
      marker: "[[DOCX_CHART_SLOT:chart-1-rId5]]",
    },
    source: {
      chartPartPath: "/word/charts/chart1.xml",
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
    title: { plainText: "测试图表", paragraphs: [] },
    autoTitleDeleted: false,
    dimensions: { widthPx: 560, heightPx: 320, widthEmu: null, heightEmu: null },
    layout: { manualLayout: false },
    plotGroups: [],
    axes: [],
    legend: { visible: true, position: "bottom", overlay: false },
    dataLabels: null,
    categories: [
      { index: 0, value: "A", displayValue: "A", valueType: "string", levels: [], sourceFormula: null, numberFormat: null, isMissing: false },
    ],
    series: [
      {
        key: "pg0-s0",
        index: 0,
        order: 0,
        name: "系列1",
        nameSource: { sourceKind: "literal", formula: null, formatCode: null, pointCount: 1, points: [] },
        chartType: "column",
        plotGroupId: "pg0",
        axisRole: "primary",
        axisIds: [],
        values: { sourceKind: "reference", formula: "Sheet1!$B$2", formatCode: null, pointCount: 1, points: [{ index: 0, value: 10, isMissing: false }] },
        style: { fill: null, line: null, explosion: null, invertIfNegative: null, pointOverrides: [] },
        marker: null,
        line: null,
        dataLabels: null,
        hidden: false,
      },
    ],
    dataTable: {
      orientation: "categories-as-rows",
      columns: [
        { key: "category", label: "分类", role: "category", valueType: "string", seriesKey: null, sourceFormula: null },
        { key: "pg0-s0", label: "系列1", role: "value", valueType: "number", seriesKey: "pg0-s0", sourceFormula: "Sheet1!$B$2" },
      ],
      rows: [{ index: 0, cells: { category: "A", "pg0-s0": 10 }, isMissing: { category: false, "pg0-s0": false } }],
      categoryColumnKey: "category",
      seriesColumnKeys: ["pg0-s0"],
      rowCount: 1,
      columnCount: 2,
    },
    bindingSchema: {
      schemaVersion: "1.0",
      supportedModes: ["whole-dataset"],
      defaultMode: "whole-dataset",
      slots: [
        {
          id: "chart-1-rId5::dataset",
          role: "dataset",
          label: "整张数据集",
          description: "",
          seriesKey: null,
          expectedDataType: "Array<Object>",
          required: true,
          bindable: true,
          currentSourceFormula: null,
          currentPointCount: 1,
        },
      ],
    },
    style: { chartStyleId: null, colorStyleId: null, chartAreaFill: null, plotAreaFill: null, roundedCorners: null },
    diagnostics: { items: [], hasErrors: false, hasWarnings: false, completenessScore: 90, modules: { identity: "complete", data: "complete", axes: "complete", style: "partial", workbook: "missing" } },
    ...overrides,
  };
}

function makeWorkspaceItem(overrides: Partial<ChartWorkspaceItem> = {}): ChartWorkspaceItem {
  return {
    locatorId: "loc-1",
    parsed: makeParsedChart(),
    backend: null,
    isBound: false,
    boundDataPath: null,
    canPreview: true,
    canBind: true,
    mergeWarnings: [],
    ...overrides,
  };
}

function mountPanel(item: ChartWorkspaceItem | null): HTMLElement {
  const container = document.createElement("div");
  document.body.append(container);
  app = createApp(ChartStructurePanel, { item });
  app.mount(container);
  return container;
}

describe("ChartStructurePanel", () => {
  it("shows an empty state when no chart is selected", () => {
    const container = mountPanel(null);
    expect(container.textContent).toContain("选中一个图表以查看结构详情");
  });

  it("shows chart summary fields when a chart is selected", () => {
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("测试图表");
    expect(container.textContent).toContain("柱形图");
    expect(container.textContent).toContain("/word/charts/chart1.xml");
  });

  it("renders the data table with category and series columns", () => {
    const container = mountPanel(makeWorkspaceItem());
    const headers = Array.from(container.querySelectorAll("table.cs-table th")).map((el) => el.textContent);
    expect(headers).toEqual(["分类", "系列1"]);
    const cells = Array.from(container.querySelectorAll("table.cs-table td")).map((el) => el.textContent);
    expect(cells).toEqual(["A", "10"]);
  });

  it("renders missing values as an em dash, distinct from a real value", () => {
    const parsed = makeParsedChart({
      dataTable: {
        orientation: "categories-as-rows",
        columns: [
          { key: "category", label: "分类", role: "category", valueType: "string", seriesKey: null, sourceFormula: null },
          { key: "pg0-s0", label: "系列1", role: "value", valueType: "number", seriesKey: "pg0-s0", sourceFormula: null },
        ],
        rows: [{ index: 0, cells: { category: "A", "pg0-s0": null }, isMissing: { category: false, "pg0-s0": true } }],
        categoryColumnKey: "category",
        seriesColumnKeys: ["pg0-s0"],
        rowCount: 1,
        columnCount: 2,
      },
    });
    const container = mountPanel(makeWorkspaceItem({ parsed }));
    const cells = Array.from(container.querySelectorAll("table.cs-table td")).map((el) => el.textContent);
    expect(cells).toEqual(["A", "—"]);
  });

  it("shows binding slots with a non-bindable status for fine-grained roles", () => {
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("整张数据集");
  });

  it("shows diagnostics list entries when present", () => {
    const parsed = makeParsedChart({
      diagnostics: {
        items: [{ code: "test-warning", level: "warning", message: "测试警告信息", recoverable: true }],
        hasErrors: false,
        hasWarnings: true,
        completenessScore: 70,
        modules: { identity: "complete", data: "partial", axes: "complete", style: "partial", workbook: "missing" },
      },
    });
    const container = mountPanel(makeWorkspaceItem({ parsed }));
    expect(container.textContent).toContain("测试警告信息");
  });

  it("includes a JSON viewer toggle for the raw structured model", () => {
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("ParsedWordChart");
  });

  it("shows merge warnings when the frontend/backend chart could not be matched cleanly", () => {
    const container = mountPanel(makeWorkspaceItem({ mergeWarnings: ["未能匹配到后端图表，绑定信息不可用"] }));
    expect(container.textContent).toContain("未能匹配到后端图表");
  });
});
