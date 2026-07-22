import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartAxisDefinition, ChartAxisType, ChartTextContent } from "../models/types";
import { parseRichText } from "./textAnalyzer";

const AXIS_LOCAL_NAMES: Record<string, ChartAxisType> = {
  catAx: "category",
  valAx: "value",
  dateAx: "date",
  serAx: "series",
};

/**
 * Parses every <c:catAx>/<c:valAx>/<c:dateAx>/<c:serAx> that is a direct
 * child of `plotArea`, regardless of which plot-group references it. The
 * plot-group walker in chartXmlAnalyzer cross-references these by axId
 * (and crossAx) to assign each series' axisRole — this function only
 * describes the axis definitions themselves.
 */
export function parseAllAxes(plotArea: Element): ChartAxisDefinition[] {
  const axes: ChartAxisDefinition[] = [];
  for (const child of Array.from(plotArea.children)) {
    if (child.namespaceURI !== OOXML_NS.c) continue;
    const axisType = AXIS_LOCAL_NAMES[child.localName];
    if (!axisType) continue;
    axes.push(parseAxis(child, axisType));
  }
  return axes;
}

function parseAxis(axisEl: Element, axisType: ChartAxisType): ChartAxisDefinition {
  const axIdEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "axId")[0];
  const id = axIdEl?.getAttribute("val") ?? "";

  const axPosEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "axPos")[0];
  const position = mapAxPos(axPosEl?.getAttribute("val") ?? null);

  const title = parseAxisTitle(axisEl);

  const scaling = axisEl.getElementsByTagNameNS(OOXML_NS.c, "scaling")[0];
  const orientationEl = scaling?.getElementsByTagNameNS(OOXML_NS.c, "orientation")[0];
  const reversed = orientationEl?.getAttribute("val") === "maxMin";
  const logBaseEl = scaling?.getElementsByTagNameNS(OOXML_NS.c, "logBase")[0];
  const logarithmicBase = logBaseEl ? parseFloat(logBaseEl.getAttribute("val") ?? "") : null;
  const minEl = scaling?.getElementsByTagNameNS(OOXML_NS.c, "min")[0];
  const maxEl = scaling?.getElementsByTagNameNS(OOXML_NS.c, "max")[0];

  const majorUnitEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "majorUnit")[0];
  const minorUnitEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "minorUnit")[0];

  const numFmtEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "numFmt")[0];

  const deleteEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "delete")[0];
  const deleted = deleteEl?.getAttribute("val") === "1";

  const crossesEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "crosses")[0];
  const crossesAtEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "crossesAt")[0];
  const crossAxEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "crossAx")[0];

  const lblAlgnEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "lblAlgn")[0];
  const tickLblPosEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "tickLblPos")[0];
  const majorTickEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "majorTickMark")[0];
  const minorTickEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "minorTickMark")[0];

  return {
    id,
    type: axisType,
    role: "unknown",
    position,
    title,
    min: minEl ? parseFloat(minEl.getAttribute("val") ?? "") : null,
    max: maxEl ? parseFloat(maxEl.getAttribute("val") ?? "") : null,
    majorUnit: majorUnitEl ? parseFloat(majorUnitEl.getAttribute("val") ?? "") : null,
    minorUnit: minorUnitEl ? parseFloat(minorUnitEl.getAttribute("val") ?? "") : null,
    logarithmicBase: Number.isFinite(logarithmicBase) ? logarithmicBase : null,
    reversed,
    visible: !deleted,
    numberFormat: numFmtEl?.getAttribute("formatCode") ?? null,
    sourceLinked: numFmtEl?.getAttribute("sourceLinked") === "1" ? true
      : numFmtEl?.getAttribute("sourceLinked") === "0" ? false : null,
    crosses: crossesEl?.getAttribute("val") ?? null,
    crossesAt: crossesAtEl ? parseFloat(crossesAtEl.getAttribute("val") ?? "") : null,
    crossAxisId: crossAxEl?.getAttribute("val") ?? null,
    labelPosition: lblAlgnEl?.getAttribute("val") ?? null,
    tickLabelPosition: tickLblPosEl?.getAttribute("val") ?? null,
    majorTickMark: majorTickEl?.getAttribute("val") ?? null,
    minorTickMark: minorTickEl?.getAttribute("val") ?? null,
    delete: deleted,
  };
}

function mapAxPos(val: string | null): ChartAxisDefinition["position"] {
  switch (val) {
    case "l": return "left";
    case "r": return "right";
    case "t": return "top";
    case "b": return "bottom";
    default: return null;
  }
}

function parseAxisTitle(axisEl: Element): ChartTextContent | null {
  const title = axisEl.getElementsByTagNameNS(OOXML_NS.c, "title")[0];
  if (!title) return null;
  const tx = title.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
  const rich = tx?.getElementsByTagNameNS(OOXML_NS.c, "rich")[0];
  if (!rich) return null;
  return parseRichText(rich);
}

/**
 * Cross-reference each plot-group's <c:axId> list against the parsed axes
 * to assign a primary/secondary x/y role. A group's first axId pair
 * (category-like axis + value-like axis) is "primary". Any additional
 * axId pair whose value axis does NOT share crossAx with the primary
 * value axis is "secondary" — this replaces the legacy heuristic that
 * counted total distinct axId values and guessed bar=primary/line=secondary.
 */
export function assignAxisRoles(
  axes: ChartAxisDefinition[],
  plotGroupAxisIdLists: string[][]
): void {
  if (axes.length === 0) return;

  const byId = new Map(axes.map((a) => [a.id, a]));

  // The first plot group's axes are always primary.
  const primaryIds = new Set(plotGroupAxisIdLists[0] ?? []);
  for (const id of primaryIds) {
    const axis = byId.get(id);
    if (!axis) continue;
    axis.role = axis.type === "value" ? "y" : "x";
  }

  for (let g = 1; g < plotGroupAxisIdLists.length; g++) {
    for (const id of plotGroupAxisIdLists[g]) {
      if (primaryIds.has(id)) continue;
      const axis = byId.get(id);
      if (!axis || axis.role !== "unknown") continue;
      axis.role = axis.type === "value" ? "secondary-y" : "secondary-x";
    }
  }

  // Anything left unresolved (axis not referenced by any plot group's axId
  // list — can happen with malformed charts) falls back to type-based guess.
  for (const axis of axes) {
    if (axis.role === "unknown") {
      axis.role = axis.type === "value" ? "y" : "x";
    }
  }
}
