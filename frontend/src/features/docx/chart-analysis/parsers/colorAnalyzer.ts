import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartColorValue, ChartFill } from "../models/types";

/**
 * Resolve an <a:solidFill> (or the noFill sibling) into a ChartColorValue.
 * Supports srgbClr, schemeClr (resolved against the document theme when
 * available), sysClr, and prstClr. Unrecognized fill kinds return an
 * "unresolved" color that preserves the raw XML fragment name instead of
 * silently dropping the reference.
 */
export function resolveFillColor(
  spPrEl: Element | null,
  themeColors: Record<string, string>
): ChartFill | null {
  if (!spPrEl) return null;

  const noFill = spPrEl.getElementsByTagNameNS(OOXML_NS.a, "noFill")[0];
  if (noFill) {
    return { color: null, noFill: true };
  }

  const solidFill = spPrEl.getElementsByTagNameNS(OOXML_NS.a, "solidFill")[0];
  if (!solidFill) return null;

  const color = resolveColorElement(solidFill, themeColors);
  if (!color) return null;

  return { color, noFill: false };
}

/**
 * Resolve the first recognized color child (srgbClr/schemeClr/sysClr/prstClr)
 * directly under `container` (e.g. <a:solidFill>, <a:ln><a:solidFill>, marker spPr).
 */
export function resolveColorElement(
  container: Element,
  themeColors: Record<string, string>
): ChartColorValue | null {
  const srgbClr = container.getElementsByTagNameNS(OOXML_NS.a, "srgbClr")[0];
  if (srgbClr) {
    const val = srgbClr.getAttribute("val") ?? "";
    return {
      sourceKind: "srgb",
      raw: val,
      resolvedHex: /^[0-9A-Fa-f]{6}$/.test(val) ? `#${val.toUpperCase()}` : null,
      alphaPercent: extractAlphaPercent(srgbClr),
    };
  }

  const schemeClr = container.getElementsByTagNameNS(OOXML_NS.a, "schemeClr")[0];
  if (schemeClr) {
    const val = schemeClr.getAttribute("val") ?? "";
    const resolved = resolveSchemeColorName(val, themeColors);
    return {
      sourceKind: "scheme",
      raw: val,
      resolvedHex: resolved,
      alphaPercent: extractAlphaPercent(schemeClr),
    };
  }

  const sysClr = container.getElementsByTagNameNS(OOXML_NS.a, "sysClr")[0];
  if (sysClr) {
    const val = sysClr.getAttribute("val") ?? "";
    const lastClr = sysClr.getAttribute("lastClr");
    return {
      sourceKind: "sys",
      raw: val,
      resolvedHex: lastClr ? `#${lastClr.toUpperCase()}` : null,
      alphaPercent: extractAlphaPercent(sysClr),
    };
  }

  const prstClr = container.getElementsByTagNameNS(OOXML_NS.a, "prstClr")[0];
  if (prstClr) {
    const val = prstClr.getAttribute("val") ?? "";
    return {
      sourceKind: "preset",
      raw: val,
      resolvedHex: resolvePresetColorName(val),
      alphaPercent: extractAlphaPercent(prstClr),
    };
  }

  // Unrecognized color kind (e.g. gradient/pattern fill) — preserve the tag
  // name so diagnostics can point at exactly what was skipped.
  const firstChild = container.firstElementChild;
  if (firstChild) {
    return {
      sourceKind: "unresolved",
      raw: firstChild.localName,
      resolvedHex: null,
      alphaPercent: null,
    };
  }

  return null;
}

function extractAlphaPercent(colorEl: Element): number | null {
  const alpha = colorEl.getElementsByTagNameNS(OOXML_NS.a, "alpha")[0];
  if (!alpha) return null;
  const val = alpha.getAttribute("val");
  if (!val) return null;
  // OOXML alpha is expressed in thousandths of a percent (100000 = 100%).
  const parsed = parseInt(val, 10);
  return Number.isFinite(parsed) ? Math.round(parsed / 1000) : null;
}

const SCHEME_ALIASES: Record<string, string> = {
  tx1: "dk1",
  tx2: "dk2",
  bg1: "lt1",
  bg2: "lt2",
};

function resolveSchemeColorName(
  schemeVal: string,
  themeColors: Record<string, string>
): string | null {
  const key = schemeVal.trim();
  const normalized = SCHEME_ALIASES[key] ?? key;
  return themeColors[normalized] ?? null;
}

const PRESET_COLOR_NAMES: Record<string, string> = {
  black: "#000000",
  white: "#FFFFFF",
  red: "#FF0000",
  green: "#008000",
  blue: "#0000FF",
  yellow: "#FFFF00",
  gray: "#808080",
  grey: "#808080",
};

function resolvePresetColorName(name: string): string | null {
  return PRESET_COLOR_NAMES[name.trim().toLowerCase()] ?? null;
}
