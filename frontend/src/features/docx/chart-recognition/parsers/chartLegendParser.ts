import { OOXML_NS } from "../../ooxml/namespaces";

export interface ChartLegendInfo {
  visible: boolean;
  position?: "top" | "bottom" | "left" | "right";
}

/**
 * Parse the chart legend from <c:chart><c:legend>.
 *
 * Position mapping (from <c:legendPos val="..."/>):
 *   t  → top
 *   b  → bottom
 *   l  → left
 *   r  → right
 *   tr → right
 *
 * If <c:legend> is absent, visible = false.
 */
export function parseChartLegend(chartXml: Document): ChartLegendInfo {
  const chartEl = chartXml.getElementsByTagNameNS(OOXML_NS.c, "chart")[0];
  if (!chartEl) return { visible: false };

  const legend = chartEl.getElementsByTagNameNS(OOXML_NS.c, "legend")[0];
  if (!legend) return { visible: false };

  // Check if legend is explicitly deleted
  const deleteEl = legend.getElementsByTagNameNS(OOXML_NS.c, "delete")[0];
  if (deleteEl?.getAttribute("val") === "1") {
    return { visible: false };
  }

  const legendPos = legend.getElementsByTagNameNS(OOXML_NS.c, "legendPos")[0];
  let position: ChartLegendInfo["position"] = "bottom";

  if (legendPos) {
    const val = legendPos.getAttribute("val");
    switch (val) {
      case "t":
        position = "top";
        break;
      case "b":
        position = "bottom";
        break;
      case "l":
        position = "left";
        break;
      case "r":
      case "tr":
        position = "right";
        break;
    }
  }

  return { visible: true, position };
}
