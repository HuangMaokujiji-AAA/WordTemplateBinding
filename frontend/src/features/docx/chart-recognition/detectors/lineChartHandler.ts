import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseLineChart } from "../parsers/lineChartParser";

export const LineChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    if (!plotArea) return false;

    const chartTypes = getDirectChartChildren(plotArea);
    return chartTypes.length === 1 && chartTypes[0] === "lineChart";
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseLineChart(context);
  },
};

export function getDirectChartChildren(plotArea: Element): string[] {
  const types: string[] = [];
  for (const child of Array.from(plotArea.children)) {
    if (child.namespaceURI === OOXML_NS.c) {
      const ln = child.localName;
      if (isChartTypeElement(ln)) types.push(ln);
    }
  }
  return types;
}

export function isChartTypeElement(localName: string): boolean {
  return [
    "barChart", "bar3DChart", "lineChart", "line3DChart",
    "pieChart", "pie3DChart", "doughnutChart", "areaChart",
    "area3DChart", "scatterChart", "radarChart",
    "surfaceChart", "surface3DChart", "stockChart",
  ].includes(localName);
}
