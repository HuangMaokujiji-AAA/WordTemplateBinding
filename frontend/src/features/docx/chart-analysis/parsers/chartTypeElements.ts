import { OOXML_NS } from "../../ooxml/namespaces";
import type { WordChartType } from "../models/types";

/**
 * Maps every OOXML plotArea chart-type element local name to a
 * WordChartType and whether the web preview pipeline (ECharts mappers)
 * currently knows how to render it. Structural parsing (title, series,
 * categories, formulas, cache) is attempted for ALL of these — only
 * `previewable` gates whether an ECharts option is produced.
 */
export interface ChartTypeElementInfo {
  type: WordChartType;
  previewable: boolean;
}

export const CHART_TYPE_ELEMENTS: Record<string, ChartTypeElementInfo> = {
  barChart: { type: "bar", previewable: true }, // resolved to "bar"|"column" by barDir
  bar3DChart: { type: "bar", previewable: false },
  lineChart: { type: "line", previewable: true },
  line3DChart: { type: "line", previewable: false },
  pieChart: { type: "pie", previewable: true },
  pie3DChart: { type: "pie", previewable: false },
  doughnutChart: { type: "doughnut", previewable: true },
  areaChart: { type: "area", previewable: true },
  area3DChart: { type: "area", previewable: false },
  scatterChart: { type: "scatter", previewable: true },
  bubbleChart: { type: "bubble", previewable: false },
  radarChart: { type: "radar", previewable: false },
  stockChart: { type: "stock", previewable: false },
  surfaceChart: { type: "surface", previewable: false },
  surface3DChart: { type: "surface", previewable: false },
};

export function isChartTypeElementName(localName: string): boolean {
  return localName in CHART_TYPE_ELEMENTS;
}

export function getDirectChartTypeChildren(plotArea: Element): Element[] {
  return Array.from(plotArea.children).filter(
    (c) => c.namespaceURI === OOXML_NS.c && isChartTypeElementName(c.localName)
  );
}
