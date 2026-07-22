import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartLegendDefinition, ChartLegendPosition } from "../models/types";

export function parseLegend(chartXml: Document): ChartLegendDefinition | null {
  const chartEl = chartXml.getElementsByTagNameNS(OOXML_NS.c, "chart")[0];
  if (!chartEl) return null;

  const legend = chartEl.getElementsByTagNameNS(OOXML_NS.c, "legend")[0];
  if (!legend) return { visible: false, position: null, overlay: false };

  const deleteEl = legend.getElementsByTagNameNS(OOXML_NS.c, "delete")[0];
  if (deleteEl?.getAttribute("val") === "1") {
    return { visible: false, position: null, overlay: false };
  }

  const legendPosEl = legend.getElementsByTagNameNS(OOXML_NS.c, "legendPos")[0];
  const position = mapLegendPos(legendPosEl?.getAttribute("val") ?? null);

  const overlayEl = legend.getElementsByTagNameNS(OOXML_NS.c, "overlay")[0];
  const overlay = overlayEl?.getAttribute("val") === "1";

  return { visible: true, position, overlay };
}

function mapLegendPos(val: string | null): ChartLegendPosition {
  switch (val) {
    case "t": return "top";
    case "b": return "bottom";
    case "l": return "left";
    case "r": return "right";
    case "tr": return "topRight";
    default: return "bottom";
  }
}
