import { createApp, type App } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import ChartStructurePanel from "../components/ChartStructurePanel.vue";
import type { ChartWorkspaceItem } from "../features/binding/chartWorkspace";
import type { ParsedWordChart } from "../features/docx/chart-analysis/models/types";
import type { ChartItem } from "../api/types";

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
      chartId: "chart-1-rId5", slotId: "chart-1-rId5",
      partKey: "/word/charts/chart1.xml", relationshipId: "rId5",
      documentOrder: 0, marker: "[[DOCX_CHART_SLOT:chart-1-rId5]]",
    },
    source: {
      chartPartPath: "/word/charts/chart1.xml", chartRelationshipPath: null,
      externalDataRelationshipId: null, embeddedWorkbookPath: null,
      embeddedWorkbookDetected: false, formulas: [], cacheSources: [], themePath: null,
    },
    type: "column", typeLabel: "柱形图",
    supportedForParsing: true, supportedForPreview: true, supportedForBinding: true,
    title: { plainText: "测试图表", paragraphs: [] }, autoTitleDeleted: false,
    dimensions: { widthPx: 560, heightPx: 320, widthEmu: null, heightEmu: null },
    layout: { manualLayout: false }, plotGroups: [], axes: [],
    legend: { visible: true, position: "bottom", overlay: false }, dataLabels: null,
    categories: [{ index: 0, value: "A", displayValue: "A", valueType: "string", levels: [], sourceFormula: null, numberFormat: null, isMissing: false }],
    series: [{
      key: "pg0-s0", index: 0, order: 0, name: "系列1",
      nameSource: { sourceKind: "literal", formula: null, formatCode: null, pointCount: 1, points: [] },
      chartType: "column", plotGroupId: "pg0", axisRole: "primary", axisIds: [],
      values: { sourceKind: "reference", formula: "Sheet1!$B$2", formatCode: null, pointCount: 1, points: [{ index: 0, value: 10, isMissing: false }] },
      style: { fill: null, line: null, explosion: null, invertIfNegative: null, pointOverrides: [] },
      marker: null, line: null, dataLabels: null, hidden: false,
    }],
    dataTable: {
      orientation: "categories-as-rows",
      columns: [
        { key: "category", label: "分类", role: "category", valueType: "string", seriesKey: null, sourceFormula: null },
        { key: "pg0-s0", label: "系列1", role: "value", valueType: "number", seriesKey: "pg0-s0", sourceFormula: "Sheet1!$B$2" },
      ],
      rows: [{ index: 0, cells: { category: "A", "pg0-s0": 10 }, isMissing: { category: false, "pg0-s0": false } }],
      categoryColumnKey: "category", seriesColumnKeys: ["pg0-s0"], rowCount: 1, columnCount: 2,
    },
    bindingSchema: {
      schemaVersion: "1.0", supportedModes: ["whole-dataset"], defaultMode: "whole-dataset",
      slots: [{ id: "s1", role: "dataset", label: "整张数据集", description: "", seriesKey: null, expectedDataType: "Array<Object>", required: true, bindable: true, currentSourceFormula: null, currentPointCount: 1 }],
    },
    style: { chartStyleId: null, colorStyleId: null, chartAreaFill: null, plotAreaFill: null, roundedCorners: null },
    diagnostics: { items: [], hasErrors: false, hasWarnings: false, completenessScore: 90, modules: { identity: "complete", data: "complete", axes: "complete", style: "partial", workbook: "missing" } },
    ...overrides,
  };
}

function makeBackend(overrides: Partial<ChartItem> = {}): ChartItem {
  return {
    locatorId: "loc-1",
    locator: { partKey: "/word/charts/chart1.xml", relationshipId: "rId5", documentOrder: 0 },
    chartType: "column", title: "测试图表", categories: ["A"], series: [{ seriesIndex: 0, name: "系列1", values: [10] }],
    isBindable: true, isBound: false, boundDataPath: null, boundDataType: null,
    analysis: null, dataDefinition: null, chartMapping: null,
    ...overrides,
  };
}

function makeWorkspaceItem(overrides: Partial<ChartWorkspaceItem> = {}): ChartWorkspaceItem {
  return {
    locatorId: "loc-1", parsed: makeParsedChart(), backend: makeBackend(),
    isBound: false, boundDataPath: null, canPreview: true, canBind: true, mergeWarnings: [],
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
  });

  it("shows all four sub-tab buttons", () => {
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("图表绑定");
    expect(container.textContent).toContain("测试数据");
    expect(container.textContent).toContain("前端解析");
    expect(container.textContent).toContain("后端分析");
  });

  it("shows merge warnings when present (via mergeWarnings prop)", () => {
    const container = mountPanel(makeWorkspaceItem({ mergeWarnings: ["未能匹配到后端图表，绑定信息不可用"] }));
    expect(container.textContent).toContain("未能匹配到后端图表");
  });

  it("renders summary info with category and series count", () => {
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("1 项");
    expect(container.textContent).toContain("1 个");
  });

  it("shows binding tab with unbound note when chart is not bound", () => {
    // Default tab is "binding" — since dataDef is null, binding content is empty
    // but the summary section shows unbound status
    const container = mountPanel(makeWorkspaceItem());
    expect(container.textContent).toContain("未绑定");
  });

  it("shows radar style, range, workbook and binding status", () => {
    const parsed = makeParsedChart({
      type: "radar",
      typeLabel: "雷达图",
      plotGroups: [{
        id: "pg0", order: 0, type: "radar", grouping: null,
        barDirection: null, scatterStyle: null, radarStyle: "filled",
        seriesKeys: ["pg0-s0"], axisIds: ["1", "2"],
        gapWidth: null, overlap: null, varyColors: null, holeSizePercent: null,
      }],
      axes: [{
        id: "2", type: "value", role: "y", position: "left", title: null,
        min: -20, max: 100, majorUnit: 20, minorUnit: null,
        logarithmicBase: null, reversed: false, visible: true,
        numberFormat: null, sourceLinked: null, crosses: null, crossesAt: null,
        crossAxisId: "1", labelPosition: null, tickLabelPosition: null,
        majorTickMark: null, minorTickMark: null, delete: false,
      }],
    });
    const container = mountPanel(makeWorkspaceItem({
      parsed,
      canBind: true,
    }));
    expect(container.textContent).toContain("雷达图");
    expect(container.textContent).toContain("填充");
    expect(container.textContent).toContain("-20");
    expect(container.textContent).toContain("100");
    expect(container.textContent).toContain("可绑定");
    expect(container.textContent).toContain("嵌入工作簿");
  });
});
