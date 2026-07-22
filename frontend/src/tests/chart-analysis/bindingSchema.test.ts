import { describe, it, expect } from "vitest";
import { buildBindingSchema } from "../../features/docx/chart-analysis/normalizers/bindingSchema";
import type {
  ChartCategoryDefinition,
  ChartIdentity,
  ChartValueSource,
  ParsedChartSeries,
} from "../../features/docx/chart-analysis/models/types";

function identity(chartId = "chart-1-rId5"): ChartIdentity {
  return {
    chartId,
    slotId: chartId,
    partKey: "/word/charts/chart1.xml",
    relationshipId: "rId5",
    documentOrder: 0,
    marker: `[[DOCX_CHART_SLOT:${chartId}]]`,
  };
}

function numSource(pointCount: number, formula: string | null = null): ChartValueSource<number> {
  return { sourceKind: formula ? "reference" : "literal", formula, formatCode: null, pointCount, points: [] };
}

function makeSeries(key: string, name: string, overrides: Partial<ParsedChartSeries> = {}): ParsedChartSeries {
  return {
    key,
    index: 0,
    order: 0,
    name,
    nameSource: { sourceKind: "literal", formula: null, formatCode: null, pointCount: 1, points: [] },
    chartType: "column",
    plotGroupId: "pg0",
    axisRole: "primary",
    axisIds: [],
    values: numSource(3, "Sheet1!$B$2:$B$4"),
    style: { fill: null, line: null, explosion: null, invertIfNegative: null, pointOverrides: [] },
    marker: null,
    line: null,
    dataLabels: null,
    hidden: false,
    ...overrides,
  };
}

function makeCategories(): ChartCategoryDefinition[] {
  return [0, 1, 2].map((i) => ({
    index: i,
    value: `cat${i}`,
    displayValue: `cat${i}`,
    valueType: "string",
    levels: [],
    sourceFormula: "Sheet1!$A$2:$A$4",
    numberFormat: null,
    isMissing: false,
  }));
}

describe("buildBindingSchema — category chart", () => {
  it("generates dataset/categories/series-name/series-values slots", () => {
    const schema = buildBindingSchema(identity(), "column", makeCategories(), [makeSeries("s0", "销售额")]);
    const roles = schema.slots.map((s) => s.role);
    expect(roles).toContain("dataset");
    expect(roles).toContain("categories");
    expect(roles).toContain("series-name");
    expect(roles).toContain("series-values");
  });

  it("defaults to whole-dataset mode with the dataset slot bindable", () => {
    const schema = buildBindingSchema(identity(), "column", makeCategories(), [makeSeries("s0", "销售额")]);
    expect(schema.defaultMode).toBe("whole-dataset");
    expect(schema.supportedModes).toContain("whole-dataset");
    const dataset = schema.slots.find((s) => s.role === "dataset");
    expect(dataset?.bindable).toBe(true);
  });

  it("marks every fine-grained slot as not yet bindable", () => {
    const schema = buildBindingSchema(identity(), "column", makeCategories(), [makeSeries("s0", "销售额")]);
    const fineGrained = schema.slots.filter((s) => s.role !== "dataset");
    expect(fineGrained.every((s) => s.bindable === false)).toBe(true);
  });

  it("produces stable slot ids across repeated calls with the same identity", () => {
    const a = buildBindingSchema(identity(), "column", makeCategories(), [makeSeries("s0", "销售额")]);
    const b = buildBindingSchema(identity(), "column", makeCategories(), [makeSeries("s0", "销售额")]);
    expect(a.slots.map((s) => s.id)).toEqual(b.slots.map((s) => s.id));
  });
});

describe("buildBindingSchema — pie chart", () => {
  it("generates categories and series-values slots (no x/y slots)", () => {
    const schema = buildBindingSchema(identity(), "pie", makeCategories(), [makeSeries("s0", "占比")]);
    const roles = schema.slots.map((s) => s.role);
    expect(roles).toContain("categories");
    expect(roles).toContain("series-values");
    expect(roles).not.toContain("x-values");
  });
});

describe("buildBindingSchema — scatter chart", () => {
  it("generates x-values/y-values slots instead of categories/series-values", () => {
    const series = makeSeries("s0", "实验组", { chartType: "scatter", xValues: numSource(2), yValues: numSource(2) });
    const schema = buildBindingSchema(identity(), "scatter", [], [series]);
    const roles = schema.slots.map((s) => s.role);
    expect(roles).toContain("x-values");
    expect(roles).toContain("y-values");
    expect(roles).not.toContain("categories");
    expect(schema.supportedModes).toContain("xy-series");
  });
});
