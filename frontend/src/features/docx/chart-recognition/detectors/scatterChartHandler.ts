import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseChartViaAnalyzer } from "./analyzerBridge";

export const ScatterChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    if (!plotArea) return false;
    const children = plotArea.getElementsByTagNameNS(OOXML_NS.c, "scatterChart");
    return children.length === 1;
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseChartViaAnalyzer(context);
  },
};
