import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext, ChartTypeHandler } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseChartViaAnalyzer } from "./analyzerBridge";

export const RadarChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    if (!plotArea) return false;
    return plotArea.getElementsByTagNameNS(OOXML_NS.c, "radarChart").length === 1;
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseChartViaAnalyzer(context);
  },
};
