import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseChartViaAnalyzer } from "./analyzerBridge";
import { isChartTypeElement } from "./lineChartHandler";

export const ComboChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    if (!plotArea) return false;

    // Count direct chart-type children of plotArea
    const types: string[] = [];
    for (const child of Array.from(plotArea.children)) {
      if (child.namespaceURI === OOXML_NS.c && isChartTypeElement(child.localName)) {
        types.push(child.localName);
      }
    }

    // Combo = at least 2 different chart type elements, none is 3D
    if (types.length < 2) return false;

    // Currently only support simple bar+line(+area-as-line) combos (no 3D)
    return types.every((t) => t === "barChart" || t === "lineChart" || t === "areaChart");
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseChartViaAnalyzer(context);
  },
};
