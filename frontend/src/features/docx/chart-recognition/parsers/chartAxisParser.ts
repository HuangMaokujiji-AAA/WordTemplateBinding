import { OOXML_NS } from "../../ooxml/namespaces";

export interface ChartAxisInfo {
  axId: string;
  axisType: "cat" | "val" | "date" | "ser";
  title?: string;
  min?: number;
  max?: number;
  majorUnit?: number;
  numberFormat?: string;
  reversed?: boolean;
  hidden?: boolean;
}

/**
 * Parse <c:catAx> or <c:valAx> from the chart's plotArea.
 *
 * @param plotArea - The <c:plotArea> element.
 * @param axId - The axis ID to match (from <c:axId> in barChart).
 * @param ns - The namespace for the axis element ("catAx" or "valAx").
 */
export function parseAxis(
  plotArea: Element,
  axIdVal: string,
  nsLocalName: string
): ChartAxisInfo | null {
  const axes = plotArea.getElementsByTagNameNS(OOXML_NS.c, nsLocalName);
  for (const axis of Array.from(axes)) {
    const axId = axis.getElementsByTagNameNS(OOXML_NS.c, "axId")[0];
    if (!axId) continue;

    const val = axId.getAttribute("val");
    if (val !== axIdVal) continue;

    const info: ChartAxisInfo = {
      axId: axIdVal,
      axisType: nsLocalName === "catAx" ? "cat" : nsLocalName === "valAx" ? "val" : "date",
    };

    // Axis title
    const title = axis.getElementsByTagNameNS(OOXML_NS.c, "title")[0];
    if (title) {
      const tx = title.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
      if (tx) {
        const rich = tx.getElementsByTagNameNS(OOXML_NS.c, "rich")[0];
        if (rich) {
          const p = rich.getElementsByTagNameNS(OOXML_NS.a, "p")[0];
          if (p) {
            const tElements = p.getElementsByTagNameNS(OOXML_NS.a, "t");
            info.title = Array.from(tElements)
              .map((t) => t.textContent ?? "")
              .join("");
          }
        }
      }
    }

    // Scaling (min, max)
    const scaling = axis.getElementsByTagNameNS(OOXML_NS.c, "scaling")[0];
    if (scaling) {
      const orientation = scaling.getElementsByTagNameNS(OOXML_NS.c, "orientation")[0];
      if (orientation) {
        const orientVal = orientation.getAttribute("val");
        info.reversed = orientVal === "maxMin";
      }
    }

    // Min/max values — check explicit min/max before autoMin/autoMax
    const explicitMin = axis.getElementsByTagNameNS(OOXML_NS.c, "min")[0];
    if (explicitMin) {
      info.min = parseFloat(explicitMin.getAttribute("val") ?? "");
    }
    const explicitMax = axis.getElementsByTagNameNS(OOXML_NS.c, "max")[0];
    if (explicitMax) {
      info.max = parseFloat(explicitMax.getAttribute("val") ?? "");
    }

    // Major unit
    const majorUnit = axis.getElementsByTagNameNS(OOXML_NS.c, "majorUnit")[0];
    if (majorUnit) {
      info.majorUnit = parseFloat(majorUnit.getAttribute("val") ?? "");
    }

    // Number format
    const numFmt = axis.getElementsByTagNameNS(OOXML_NS.c, "numFmt")[0];
    if (numFmt) {
      info.numberFormat = numFmt.getAttribute("formatCode") ?? undefined;
    }

    // Hidden / deleted
    const deleteEl = axis.getElementsByTagNameNS(OOXML_NS.c, "delete")[0];
    if (deleteEl) {
      info.hidden = deleteEl.getAttribute("val") === "1";
    }

    return info;
  }

  return null;
}

/**
 * Parse all axes referenced by a barChart element.
 */
export function parseBarChartAxes(
  barChartEl: Element,
  plotArea: Element
): { catAxes: ChartAxisInfo[]; valAxes: ChartAxisInfo[] } {
  const axIds = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "axId");
  const catAxes: ChartAxisInfo[] = [];
  const valAxes: ChartAxisInfo[] = [];

  for (const axIdEl of Array.from(axIds)) {
    const axIdVal = axIdEl.getAttribute("val");
    if (!axIdVal) continue;

    const catAxis = parseAxis(plotArea, axIdVal, "catAx");
    if (catAxis) {
      catAxes.push(catAxis);
      continue;
    }

    const valAxis = parseAxis(plotArea, axIdVal, "valAx");
    if (valAxis) {
      valAxes.push(valAxis);
    }
  }

  return { catAxes, valAxes };
}
