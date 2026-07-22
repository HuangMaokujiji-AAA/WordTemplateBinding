import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { detectBarChart } from "./barChartDetector";
import { parseChartViaAnalyzer } from "./analyzerBridge";

/**
 * Handler for 2D bar/column charts.
 *
 * Recognizes <c:barChart> elements (both clustered and stacked,
 * both vertical "col" and horizontal "bar" directions).
 */
export const BarChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const detection = detectBarChart(chartXml);
    return detection.supported;
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseChartViaAnalyzer(context);
  },
};
