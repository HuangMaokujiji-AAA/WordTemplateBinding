import type { WordChartModel, WordChartType } from "./types";

/**
 * Context passed to ChartTypeHandler.parse().
 */
export interface ChartParseContext {
  chartXml: Document;
  chartXmlPath: string;
  chartId: string;
  relationshipId: string;
  zip: import("jszip");
  widthPx: number;
  heightPx: number;
}

export interface ChartDetectionResult {
  supported: boolean;
  detectedType: string;
  modelType: WordChartType;
  reason?: string;
}

/**
 * Registry handler for a specific chart type.
 */
export interface ChartTypeHandler {
  /** Returns true if this handler can parse the given chart XML. */
  canHandle(chartXml: Document): boolean;

  /** Parse the chart XML into a unified WordChartModel. */
  parse(context: ChartParseContext): Promise<WordChartModel>;
}
