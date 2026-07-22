import type {
  ChartCategoryDefinition,
  ChartDataCell,
  ChartDataColumn,
  ChartDataRow,
  ChartDataTable,
  ParsedChartSeries,
  WordChartType,
} from "../models/types";

const XY_TYPES: ReadonlySet<WordChartType> = new Set(["scatter", "bubble"]);

/**
 * Pure function: ParsedWordChart's categories + series → a normalized 2D
 * data table for display and (eventually) fine-grained binding. Never
 * re-reads XML — every value here already exists on the series/category
 * models produced by chartXmlAnalyzer.
 */
export function buildDataTable(
  chartType: WordChartType,
  categories: ChartCategoryDefinition[],
  series: ParsedChartSeries[]
): ChartDataTable {
  if (XY_TYPES.has(chartType)) {
    return buildXyDataTable(series);
  }
  return buildCategoryDataTable(categories, series);
}

function buildCategoryDataTable(
  categories: ChartCategoryDefinition[],
  series: ParsedChartSeries[]
): ChartDataTable {
  const columns: ChartDataColumn[] = [];
  const categoryColumnKey = categories.length > 0 ? "category" : null;

  if (categoryColumnKey) {
    columns.push({
      key: categoryColumnKey,
      label: "分类",
      role: "category",
      valueType: inferCategoryValueType(categories),
      seriesKey: null,
      sourceFormula: categories.find((c) => c.sourceFormula)?.sourceFormula ?? null,
    });
  }

  const seriesColumnKeys: string[] = [];
  for (const s of series) {
    const key = s.key;
    seriesColumnKeys.push(key);
    columns.push({
      key,
      label: s.name,
      role: "value",
      valueType: "number",
      seriesKey: s.key,
      sourceFormula: s.values.formula,
    });
  }

  const rowCount = Math.max(
    categories.length,
    ...series.map((s) => s.values.points.length),
    0
  );

  const rows: ChartDataRow[] = [];
  for (let i = 0; i < rowCount; i++) {
    const cells: Record<string, ChartDataCell> = {};
    const isMissing: Record<string, boolean> = {};

    if (categoryColumnKey) {
      const cat = categories[i];
      cells[categoryColumnKey] = cat ? cat.value : null;
      isMissing[categoryColumnKey] = !cat || cat.isMissing;
    }

    for (const s of series) {
      const point = s.values.points[i];
      cells[s.key] = point?.value ?? null;
      isMissing[s.key] = !point || point.isMissing;
    }

    rows.push({ index: i, cells, isMissing });
  }

  return {
    orientation: "categories-as-rows",
    columns,
    rows,
    categoryColumnKey,
    seriesColumnKeys,
    rowCount,
    columnCount: columns.length,
  };
}

function buildXyDataTable(series: ParsedChartSeries[]): ChartDataTable {
  const columns: ChartDataColumn[] = [];
  const seriesColumnKeys: string[] = [];

  for (const s of series) {
    seriesColumnKeys.push(s.key);
    columns.push({
      key: `${s.key}-name`,
      label: `${s.name}（系列名）`,
      role: "series-name",
      valueType: "string",
      seriesKey: s.key,
      sourceFormula: s.nameSource.formula,
    });
    columns.push({
      key: `${s.key}-x`,
      label: `${s.name}（X）`,
      role: "x-value",
      valueType: "number",
      seriesKey: s.key,
      sourceFormula: s.xValues?.formula ?? null,
    });
    columns.push({
      key: `${s.key}-y`,
      label: `${s.name}（Y）`,
      role: "y-value",
      valueType: "number",
      seriesKey: s.key,
      sourceFormula: s.yValues?.formula ?? null,
    });
    if (s.bubbleSizes) {
      columns.push({
        key: `${s.key}-size`,
        label: `${s.name}（大小）`,
        role: "bubble-size",
        valueType: "number",
        seriesKey: s.key,
        sourceFormula: s.bubbleSizes.formula,
      });
    }
  }

  const rowCount = Math.max(
    0,
    ...series.map((s) => Math.max(s.xValues?.points.length ?? 0, s.yValues?.points.length ?? 0))
  );

  const rows: ChartDataRow[] = [];
  for (let i = 0; i < rowCount; i++) {
    const cells: Record<string, ChartDataCell> = {};
    const isMissing: Record<string, boolean> = {};

    for (const s of series) {
      const xPoint = s.xValues?.points[i];
      const yPoint = s.yValues?.points[i];
      cells[`${s.key}-name`] = s.name;
      isMissing[`${s.key}-name`] = false;
      cells[`${s.key}-x`] = xPoint?.value ?? null;
      isMissing[`${s.key}-x`] = !xPoint || xPoint.isMissing;
      cells[`${s.key}-y`] = yPoint?.value ?? null;
      isMissing[`${s.key}-y`] = !yPoint || yPoint.isMissing;
      if (s.bubbleSizes) {
        const sizePoint = s.bubbleSizes.points[i];
        cells[`${s.key}-size`] = sizePoint?.value ?? null;
        isMissing[`${s.key}-size`] = !sizePoint || sizePoint.isMissing;
      }
    }

    rows.push({ index: i, cells, isMissing });
  }

  return {
    orientation: "xy-pairs",
    columns,
    rows,
    categoryColumnKey: null,
    seriesColumnKeys,
    rowCount,
    columnCount: columns.length,
  };
}

function inferCategoryValueType(categories: ChartCategoryDefinition[]): "string" | "number" | "date" | "mixed" {
  const types = new Set(categories.map((c) => c.valueType));
  if (types.size === 0) return "string";
  if (types.size === 1) return [...types][0];
  return "mixed";
}
