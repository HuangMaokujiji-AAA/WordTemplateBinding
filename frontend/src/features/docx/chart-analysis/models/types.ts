// ParsedWordChart — the normalized, ECharts-agnostic, JSON-serializable
// description of a single Word native chart. This is the model returned
// by chart-analysis/parsers/chartXmlAnalyzer.ts and consumed by:
//  - normalizers/dataTable.ts and normalizers/bindingSchema.ts (pure functions)
//  - render/toWordChartModel.ts (projection into the legacy ECharts pipeline)
//  - the ChartStructurePanel UI
//
// Nothing in this file may reference DOM nodes, XML Document/Element,
// JSZip, Map, ECharts types, or functions — every field must survive
// JSON.stringify/JSON.parse unchanged.

import type { ChartDiagnostics } from "../diagnostics/diagnostics";

export type WordChartType =
  | "bar"
  | "column"
  | "line"
  | "pie"
  | "doughnut"
  | "area"
  | "scatter"
  | "bubble"
  | "radar"
  | "stock"
  | "surface"
  | "combo"
  | "unsupported";

// ---- Identity & source ----

export interface ChartIdentity {
  chartId: string;
  slotId: string;

  partKey: string;
  relationshipId: string;
  documentOrder: number;

  marker: string;
}

export type ChartFormulaRole =
  | "series-name"
  | "category"
  | "value"
  | "x-value"
  | "y-value"
  | "bubble-size";

export interface ChartFormulaReference {
  role: ChartFormulaRole;
  seriesIndex: number | null;
  formula: string;
  sheetName: string | null;
  rangeAddress: string | null;
}

export type ChartCacheSourceKind =
  | "series-name"
  | "category"
  | "value"
  | "x-value"
  | "y-value"
  | "bubble-size";

export interface ChartCacheSource {
  kind: ChartCacheSourceKind;
  seriesIndex: number | null;
  pointCount: number | null;
  hasCache: boolean;
}

export interface ChartSourceMetadata {
  chartPartPath: string;
  chartRelationshipPath: string | null;

  externalDataRelationshipId: string | null;
  embeddedWorkbookPath: string | null;
  embeddedWorkbookDetected: boolean;

  formulas: ChartFormulaReference[];
  cacheSources: ChartCacheSource[];

  themePath: string | null;
}

// ---- Data points ----

export interface ChartDataPoint<T> {
  index: number;
  value: T | null;
  displayValue?: string | null;
  formatCode?: string | null;
  isMissing: boolean;
}

export type ChartValueSourceKind = "reference" | "literal" | "cache-only" | "missing";

export interface ChartValueSource<T> {
  sourceKind: ChartValueSourceKind;

  formula: string | null;
  formatCode: string | null;
  pointCount: number | null;

  points: Array<ChartDataPoint<T>>;
}

// ---- Categories ----

export type ChartCategoryValueType = "string" | "number" | "date" | "mixed";

export interface ChartCategoryDefinition {
  index: number;
  value: string | number | null;
  displayValue: string;
  valueType: ChartCategoryValueType;
  levels: string[];

  sourceFormula: string | null;
  numberFormat: string | null;

  isMissing: boolean;
}

// ---- Series ----

export type ChartAxisRole = "primary" | "secondary" | "none";

export interface ChartMarkerDefinition {
  symbol: string | null;
  size: number | null;
  color: ChartColorValue | null;
}

export interface ChartLineDefinition {
  color: ChartColorValue | null;
  widthPt: number | null;
  dashStyle: string | null;
  smooth: boolean;
  noFill: boolean;
}

export interface ChartPointStyleOverride {
  pointIndex: number;
  fill: ChartFill | null;
}

export interface ChartSeriesStyle {
  fill: ChartFill | null;
  line: ChartLineDefinition | null;

  explosion: number | null;
  invertIfNegative: boolean | null;

  pointOverrides: ChartPointStyleOverride[];
}

export interface ParsedChartSeries {
  key: string;

  index: number;
  order: number;

  name: string;
  nameSource: ChartValueSource<string>;

  chartType: WordChartType;
  plotGroupId: string;

  axisRole: ChartAxisRole;
  axisIds: string[];

  values: ChartValueSource<number>;

  xValues?: ChartValueSource<number>;
  yValues?: ChartValueSource<number>;
  bubbleSizes?: ChartValueSource<number>;

  style: ChartSeriesStyle;
  marker: ChartMarkerDefinition | null;
  line: ChartLineDefinition | null;
  dataLabels: ChartDataLabelDefinition | null;

  hidden: boolean;
}

// ---- Plot groups (combo chart support) ----

export type ChartGrouping = "standard" | "clustered" | "stacked" | "percentStacked" | null;
export type RadarStyle = "standard" | "marker" | "filled";

export interface ChartPlotGroup {
  id: string;
  order: number;

  type: WordChartType;

  grouping: ChartGrouping;

  barDirection: "bar" | "col" | null;
  scatterStyle: string | null;
  radarStyle: RadarStyle | null;

  seriesKeys: string[];
  axisIds: string[];

  gapWidth: number | null;
  overlap: number | null;

  varyColors: boolean | null;

  /** Doughnut charts only: <c:holeSize val="..."/> as a percentage (0-90). */
  holeSizePercent: number | null;
}

// ---- Axes ----

export type ChartAxisType = "category" | "value" | "date" | "series";
export type ChartAxisRolePosition = "x" | "y" | "secondary-x" | "secondary-y" | "unknown";

export interface ChartAxisDefinition {
  id: string;

  type: ChartAxisType;
  role: ChartAxisRolePosition;

  position: "left" | "right" | "top" | "bottom" | null;

  title: ChartTextContent | null;

  min: number | null;
  max: number | null;
  majorUnit: number | null;
  minorUnit: number | null;

  logarithmicBase: number | null;
  reversed: boolean;
  visible: boolean;

  numberFormat: string | null;
  sourceLinked: boolean | null;

  crosses: string | null;
  crossesAt: number | null;
  crossAxisId: string | null;

  labelPosition: string | null;
  tickLabelPosition: string | null;

  majorTickMark: string | null;
  minorTickMark: string | null;

  delete: boolean;
}

// ---- Text / legend / data labels ----

export interface ChartTextRun {
  text: string;
  bold?: boolean;
  italic?: boolean;
  fontSizePt?: number;
  color?: string;
}

export interface ChartTextParagraph {
  runs: ChartTextRun[];
}

export interface ChartTextContent {
  plainText: string;
  paragraphs: ChartTextParagraph[];
}

export type ChartLegendPosition = "top" | "bottom" | "left" | "right" | "topRight" | null;

export interface ChartLegendDefinition {
  visible: boolean;
  position: ChartLegendPosition;
  overlay: boolean;
}

export interface ChartDataLabelDefinition {
  showValue: boolean;
  showCategoryName: boolean;
  showSeriesName: boolean;
  showPercent: boolean;
  showLegendKey: boolean;
  showLeaderLines: boolean;
  separator: string | null;
  position: string | null;
  numberFormat: string | null;
}

// ---- Style / color ----

export type ChartColorSourceKind = "srgb" | "scheme" | "sys" | "preset" | "unresolved";

export interface ChartColorValue {
  sourceKind: ChartColorSourceKind;
  raw: string;
  resolvedHex: string | null;
  alphaPercent: number | null;
}

export interface ChartFill {
  color: ChartColorValue | null;
  noFill: boolean;
}

export interface ChartStyleDefinition {
  chartStyleId: number | null;
  colorStyleId: number | null;

  chartAreaFill: ChartFill | null;
  plotAreaFill: ChartFill | null;

  roundedCorners: boolean | null;
}

// ---- Layout / dimensions ----

export interface ChartDimensions {
  widthPx: number;
  heightPx: number;
  widthEmu: number | null;
  heightEmu: number | null;
}

export interface ChartLayout {
  manualLayout: boolean;
}

// ---- Data table ----

export type ChartDataTableOrientation = "categories-as-rows" | "series-as-rows" | "xy-pairs";

export type ChartDataColumnRole =
  | "category"
  | "series-name"
  | "value"
  | "x-value"
  | "y-value"
  | "bubble-size";

export type ChartDataValueType = "string" | "number" | "date" | "mixed";

export interface ChartDataColumn {
  key: string;
  label: string;

  role: ChartDataColumnRole;

  valueType: ChartDataValueType;
  seriesKey: string | null;

  sourceFormula: string | null;
}

export type ChartDataCell = string | number | null;

export interface ChartDataRow {
  index: number;
  cells: Record<string, ChartDataCell>;
  isMissing: Record<string, boolean>;
}

export interface ChartDataTable {
  orientation: ChartDataTableOrientation;

  columns: ChartDataColumn[];
  rows: ChartDataRow[];

  categoryColumnKey: string | null;
  seriesColumnKeys: string[];

  rowCount: number;
  columnCount: number;
}

// ---- Binding schema ----

export type ChartBindingMode =
  | "whole-dataset"
  | "category-and-series"
  | "series-values"
  | "xy-series";

export type ChartBindingSlotRole =
  | "dataset"
  | "categories"
  | "series-name"
  | "series-values"
  | "x-values"
  | "y-values"
  | "bubble-sizes";

export type ChartBindingExpectedType =
  | "Array<Object>"
  | "Array<String>"
  | "Array<Number>"
  | "String";

export interface ChartBindingSlot {
  id: string;

  role: ChartBindingSlotRole;

  label: string;
  description: string;

  seriesKey: string | null;

  expectedDataType: ChartBindingExpectedType;

  required: boolean;
  bindable: boolean;

  currentSourceFormula: string | null;
  currentPointCount: number | null;
}

export interface ChartBindingSchema {
  schemaVersion: "1.0";

  supportedModes: ChartBindingMode[];

  defaultMode: "whole-dataset";

  slots: ChartBindingSlot[];
}

// ---- Top-level model ----

export interface ParsedWordChart {
  schemaVersion: "1.0";

  identity: ChartIdentity;
  source: ChartSourceMetadata;

  type: WordChartType;
  typeLabel: string;
  supportedForParsing: boolean;
  supportedForPreview: boolean;
  supportedForBinding: boolean;

  title: ChartTextContent | null;
  autoTitleDeleted: boolean;

  dimensions: ChartDimensions;
  layout: ChartLayout;

  plotGroups: ChartPlotGroup[];
  axes: ChartAxisDefinition[];
  legend: ChartLegendDefinition | null;
  dataLabels: ChartDataLabelDefinition | null;

  categories: ChartCategoryDefinition[];
  series: ParsedChartSeries[];

  dataTable: ChartDataTable;
  bindingSchema: ChartBindingSchema;

  style: ChartStyleDefinition;
  diagnostics: ChartDiagnostics;
}
