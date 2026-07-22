import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartStyleDefinition } from "../models/types";
import { resolveFillColor } from "./colorAnalyzer";

/**
 * Parses chart-area/plot-area level styling. chartStyleId/colorStyleId come
 * from the sibling chart-style part relationship — Word does not embed
 * them in chartN.xml itself, so both fields stay null unless a caller
 * supplies them (kept here as an explicit, always-present field so the UI
 * can show "not detected" rather than omit the row).
 */
export function parseChartStyle(
  chartXml: Document,
  themeColors: Record<string, string>
): ChartStyleDefinition {
  const chartSpace = chartXml.documentElement;

  const chartAreaSpPr = getDirectChild(chartSpace, OOXML_NS.c, "spPr");
  const chartAreaFill = resolveFillColor(chartAreaSpPr, themeColors);

  const chartEl = chartXml.getElementsByTagNameNS(OOXML_NS.c, "chart")[0];
  const plotArea = chartEl?.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const plotAreaSpPr = plotArea ? getDirectChild(plotArea, OOXML_NS.c, "spPr") : null;
  const plotAreaFill = resolveFillColor(plotAreaSpPr, themeColors);

  const roundedCornersEl = chartSpace
    ? Array.from(chartSpace.children).find(
        (c) => c.namespaceURI === OOXML_NS.c && c.localName === "roundedCorners"
      )
    : undefined;
  const roundedCorners = roundedCornersEl ? roundedCornersEl.getAttribute("val") === "1" : null;

  return {
    chartStyleId: null,
    colorStyleId: null,
    chartAreaFill,
    plotAreaFill,
    roundedCorners,
  };
}

function getDirectChild(parent: Element | null, ns: string, localName: string): Element | null {
  if (!parent) return null;
  return (
    Array.from(parent.children).find((c) => c.namespaceURI === ns && c.localName === localName) ??
    null
  );
}
