export type WordChartType =
  | "bar"
  | "column"
  | "line"
  | "pie"
  | "doughnut"
  | "area"
  | "scatter"
  | "combo"
  | "unsupported";

// ---- Series ----

export interface WordChartSeries {
  name: string;
  values: Array<number | null>;

  /** For scatter: paired x values corresponding to each y in `values`. */
  xValues?: Array<number | null>;

  /** Sub-type for combo charts; e.g. "bar" or "line". */
  chartType?: string;
  axis?: "primary" | "secondary";

  color?: string;

  showValueLabel?: boolean;
  dataLabelPosition?: string;

  sourceFormula?: string;
  numberFormat?: string;
}

// ---- Categories ----

export interface WordChartCategory {
  value: string;
  levels?: string[];

  displayValue?: string;
  isGroupStart?: boolean;
}

// ---- Base Model ----

export interface WordChartModel {
  id: string;
  relationshipId: string;
  sourcePath: string;

  type: WordChartType;

  title?: string;

  categories: WordChartCategory[];

  series: WordChartSeries[];

  legend?: {
    visible: boolean;
    position?: "top" | "bottom" | "left" | "right";
  };

  xAxis?: {
    title?: string;
    min?: number;
    max?: number;
    majorUnit?: number;
    numberFormat?: string;
    reversed?: boolean;
  };

  yAxis?: {
    title?: string;
    min?: number;
    max?: number;
    majorUnit?: number;
    numberFormat?: string;
    reversed?: boolean;
  };

  widthPx?: number;
  heightPx?: number;

  unsupportedReason?: string;
}

// ---- Bar / Column ----

export interface WordBarChartModel extends WordChartModel {
  type: "bar" | "column";
  grouping: "clustered" | "stacked" | "percentStacked" | "standard";
  barDirection: "bar" | "col";
  gapWidth?: number;
  overlap?: number;
}

// ---- Line ----

export type LineGrouping = "standard" | "stacked" | "percentStacked";

export interface WordLineChartModel extends WordChartModel {
  type: "line";
  grouping: LineGrouping;
  /** Whether markers are shown on data points. */
  showMarker?: boolean;
  /** Whether the line is smoothed. */
  smooth?: boolean;
}

// ---- Pie ----

export interface WordPieChartModel extends WordChartModel {
  type: "pie";
  /** First slice explosion offset (Word supports per-slice explosion). */
  explosion?: number;
}

// ---- Doughnut ----

export interface WordDoughnutChartModel extends WordChartModel {
  type: "doughnut";
  holeSize?: number;
  explosion?: number;
}

// ---- Area ----

export type AreaGrouping = "standard" | "stacked" | "percentStacked";

export interface WordAreaChartModel extends WordChartModel {
  type: "area";
  grouping: AreaGrouping;
}

// ---- Scatter ----

export interface WordScatterChartModel extends WordChartModel {
  type: "scatter";
  /** Scatter style: "lineMarker" | "line" | "marker" | "smooth" */
  scatterStyle?: string;
}

// ---- Combo ----

export interface WordComboChartModel extends WordChartModel {
  type: "combo";
  /** Whether to use dual y-axes. */
  useSecondaryAxis?: boolean;
  secondaryYAxis?: {
    title?: string;
    min?: number;
    max?: number;
    majorUnit?: number;
    numberFormat?: string;
  };
}
