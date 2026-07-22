import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartDataLabelDefinition } from "../models/types";

/**
 * Parses <c:dLbls> (chart-level or series-level) into a full
 * ChartDataLabelDefinition. Returns null when no <c:dLbls> element exists
 * for `parent`, letting callers merge chart-level and series-level
 * definitions (series overrides chart, matching Word's own precedence).
 */
export function parseDataLabels(parent: Element): ChartDataLabelDefinition | null {
  const dLbls = Array.from(parent.children).find(
    (c) => c.namespaceURI === OOXML_NS.c && c.localName === "dLbls"
  );
  if (!dLbls) return null;

  return {
    showValue: readBool(dLbls, "showVal", false),
    showCategoryName: readBool(dLbls, "showCatName", false),
    showSeriesName: readBool(dLbls, "showSerName", false),
    showPercent: readBool(dLbls, "showPercent", false),
    showLegendKey: readBool(dLbls, "showLegendKey", false),
    showLeaderLines: readBool(dLbls, "showLeaderLines", true),
    separator: readText(dLbls, "separator"),
    position: readAttr(dLbls, "dLblPos", "val"),
    numberFormat: readNumFmt(dLbls),
  };
}

function readBool(parent: Element, localName: string, fallback: boolean): boolean {
  const el = parent.getElementsByTagNameNS(OOXML_NS.c, localName)[0];
  if (!el) return fallback;
  const val = el.getAttribute("val");
  if (val == null) return fallback;
  return val === "1" || val.toLowerCase() === "true";
}

function readText(parent: Element, localName: string): string | null {
  const el = parent.getElementsByTagNameNS(OOXML_NS.c, localName)[0];
  return el?.textContent?.trim() || null;
}

function readAttr(parent: Element, localName: string, attr: string): string | null {
  const el = parent.getElementsByTagNameNS(OOXML_NS.c, localName)[0];
  return el?.getAttribute(attr) ?? null;
}

function readNumFmt(parent: Element): string | null {
  const el = parent.getElementsByTagNameNS(OOXML_NS.c, "numFmt")[0];
  return el?.getAttribute("formatCode") ?? null;
}
