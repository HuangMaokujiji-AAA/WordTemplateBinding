import { describe, it, expect } from "vitest";
import { buildDataTable } from "../../features/docx/chart-analysis/normalizers/dataTable";
import type {
  ChartCategoryDefinition,
  ChartValueSource,
  ParsedChartSeries,
} from "../../features/docx/chart-analysis/models/types";

function numSource(values: Array<number | null>, formula: string | null = null): ChartValueSource<number> {
  return {
    sourceKind: formula ? "reference" : "literal",
    formula,
    formatCode: null,
    pointCount: values.length,
    points: values.map((v, i) => ({ index: i, value: v, isMissing: v === null })),
  };
}

function makeSeries(key: string, name: string, values: Array<number | null>, overrides: Partial<ParsedChartSeries> = {}): ParsedChartSeries {
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
    values: numSource(values),
    style: { fill: null, line: null, explosion: null, invertIfNegative: null, pointOverrides: [] },
    marker: null,
    line: null,
    dataLabels: null,
    hidden: false,
    ...overrides,
  };
}

function makeCategories(values: string[]): ChartCategoryDefinition[] {
  return values.map((v, i) => ({
    index: i,
    value: v,
    displayValue: v,
    valueType: "string",
    levels: [],
    sourceFormula: null,
    numberFormat: null,
    isMissing: false,
  }));
}

describe("buildDataTable — category-oriented charts", () => {
  it("builds a single-series table with category + value columns", () => {
    const categories = makeCategories(["计算机学院", "软件学院"]);
    const series = [makeSeries("s0", "平均成绩", [88.5, 90.1])];
    const table = buildDataTable("column", categories, series);

    expect(table.orientation).toBe("categories-as-rows");
    expect(table.columnCount).toBe(2);
    expect(table.rowCount).toBe(2);
    expect(table.rows[0].cells.category).toBe("计算机学院");
    expect(table.rows[0].cells.s0).toBe(88.5);
  });

  it("builds a multi-series table with stable column keys per series", () => {
    const categories = makeCategories(["一月", "二月"]);
    const series = [
      makeSeries("s0", "销售额", [100, 120]),
      makeSeries("s1", "利润", [10, 12]),
    ];
    const table = buildDataTable("column", categories, series);
    expect(table.seriesColumnKeys).toEqual(["s0", "s1"]);
    expect(table.rows[1].cells.s0).toBe(120);
    expect(table.rows[1].cells.s1).toBe(12);
  });

  it("uses the longer of category-length / series-point-length as row count and marks missing", () => {
    const categories = makeCategories(["A", "B", "C"]);
    const series = [makeSeries("s0", "值", [1, 2])];
    const table = buildDataTable("column", categories, series);
    expect(table.rowCount).toBe(3);
    expect(table.rows[2].isMissing.s0).toBe(true);
  });

  it("marks a null value point as missing, distinct from a real 0", () => {
    const categories = makeCategories(["A", "B"]);
    const series = [makeSeries("s0", "值", [0, null])];
    const table = buildDataTable("column", categories, series);
    expect(table.rows[0].cells.s0).toBe(0);
    expect(table.rows[0].isMissing.s0).toBe(false);
    expect(table.rows[1].isMissing.s0).toBe(true);
  });
});

describe("buildDataTable — xy-pairs charts", () => {
  it("builds an xy-pairs table with name/x/y columns per series", () => {
    const series: ParsedChartSeries[] = [
      makeSeries("s0", "实验组", [], {
        chartType: "scatter",
        xValues: numSource([1, 2]),
        yValues: numSource([10, 20]),
      }),
    ];
    const table = buildDataTable("scatter", [], series);
    expect(table.orientation).toBe("xy-pairs");
    expect(table.rowCount).toBe(2);
    expect(table.rows[0].cells["s0-x"]).toBe(1);
    expect(table.rows[0].cells["s0-y"]).toBe(10);
    expect(table.rows[0].cells["s0-name"]).toBe("实验组");
  });

  it("does not force scatter data into a categories table", () => {
    const series: ParsedChartSeries[] = [
      makeSeries("s0", "组1", [], { chartType: "scatter", xValues: numSource([1]), yValues: numSource([2]) }),
    ];
    const table = buildDataTable("scatter", [], series);
    expect(table.categoryColumnKey).toBeNull();
  });
});
