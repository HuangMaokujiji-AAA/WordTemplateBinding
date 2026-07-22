import type { ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { analyzeChartXml } from "../../chart-analysis/parsers/chartXmlAnalyzer";
import { toWordChartModel } from "../../chart-analysis/render/toWordChartModel";

/**
 * Bridges the legacy ChartTypeHandler.parse() contract to the unified
 * chart-analysis pipeline: analyze once into a ParsedWordChart, then
 * project into the WordChartModel shape the existing ECharts
 * mappers/renderers expect. Every one of the 7 chart-type handlers calls
 * this instead of re-walking the chart XML itself.
 */
export async function parseChartViaAnalyzer(context: ChartParseContext): Promise<WordChartModel> {
  const parsed = await analyzeChartXml({
    chartXml: context.chartXml,
    chartXmlPath: context.chartXmlPath,
    chartId: context.chartId,
    slotId: context.slotId ?? context.chartId,
    relationshipId: context.relationshipId,
    documentOrder: context.documentOrder ?? 0,
    marker: context.marker ?? "",
    widthPx: context.widthPx,
    heightPx: context.heightPx,
    widthEmu: context.widthEmu ?? null,
    heightEmu: context.heightEmu ?? null,
    zip: context.zip,
  });

  return toWordChartModel(parsed);
}
