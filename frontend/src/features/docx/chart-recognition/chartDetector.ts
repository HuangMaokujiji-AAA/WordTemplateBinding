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

  /**
   * Fields below are optional for backward compatibility with call sites
   * that construct a ChartParseContext without going through processDocx's
   * full LocatedChart (e.g. targeted unit/integration tests). When absent,
   * handlers fall back to chartId/documentOrder 0/empty marker/null EMU —
   * safe because these fields only affect chart-analysis identity/dimension
   * metadata, never parsing correctness.
   */
  slotId?: string;
  documentOrder?: number;
  marker?: string;
  widthEmu?: number | null;
  heightEmu?: number | null;
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
