import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseChartViaAnalyzer } from "./analyzerBridge";

/**
 * Handler for chart types the ECharts pipeline cannot render (radar,
 * bubble, stock, surface, 3D variants, etc).
 *
 * This handler is registered last and catches any chart that isn't
 * handled by a more specific handler. It still runs the chart through
 * the full chart-analysis pipeline — structural parsing (title, series,
 * categories, formulas, cache) is attempted for every chart type, even
 * when ECharts preview is not available. toWordChartModel() detects
 * `supportedForPreview: false` on the resulting ParsedWordChart and
 * projects it into the placeholder "unsupported" WordChartModel shape.
 */
export const UnsupportedChartHandler: ChartTypeHandler = {
  canHandle(_chartXml: Document): boolean {
    // Always returns true as the fallback — it must be registered last
    return true;
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseChartViaAnalyzer(context);
  },
};
