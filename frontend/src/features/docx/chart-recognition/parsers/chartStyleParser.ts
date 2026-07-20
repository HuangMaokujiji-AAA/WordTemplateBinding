import { OOXML_NS } from "../../ooxml/namespaces";

export interface ChartStyleInfo {
  gapWidth?: number;
  overlap?: number;
}

/**
 * Parse chart style properties like gapWidth and overlap.
 *
 * Found at: <c:chart><c:plotArea><c:barChart><c:gapWidth>
 *           <c:chart><c:plotArea><c:barChart><c:overlap>
 */
export function parseChartStyle(barChartEl: Element): ChartStyleInfo {
  const gapWidth = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "gapWidth")[0];
  const overlap = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "overlap")[0];

  return {
    gapWidth: gapWidth ? parseInt(gapWidth.getAttribute("val") ?? "150", 10) : undefined,
    overlap: overlap ? parseInt(overlap.getAttribute("val") ?? "0", 10) : undefined,
  };
}
