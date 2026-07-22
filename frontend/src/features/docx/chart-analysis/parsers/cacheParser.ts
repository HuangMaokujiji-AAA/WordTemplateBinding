import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartDataPoint, ChartValueSource } from "../models/types";
import { normalizeChartNumber } from "../../chart-recognition/utils/numberUtils";

/**
 * Single source of truth for reading <c:pt idx="N"> points out of a
 * numCache/strCache/numLit/strLit element.
 *
 * The legacy per-type parsers each re-implemented this and disagreed on
 * sparse-idx handling: chartCategoryParser and scatterChartParser gap-fill
 * missing indices, but chartSeriesParser's main value parser did not — a
 * chart with `idx="0"` and `idx="2"` (idx 1 missing) would silently shift
 * the value at idx 2 into array position 1. This implementation always
 * gap-fills using max(ptCount attribute, max observed idx + 1) so every
 * point keeps its true index and gaps become isMissing:true entries.
 */
export interface RawCacheResult {
  pointCount: number;
  rawPoints: Array<{ idx: number; raw: string | null }>;
  formatCode: string | null;
  formula: string | null;
}

function readPtCount(cacheEl: Element): number | null {
  const ptCountEl = cacheEl.getElementsByTagNameNS(OOXML_NS.c, "ptCount")[0];
  if (!ptCountEl) return null;
  const val = ptCountEl.getAttribute("val");
  if (val == null) return null;
  const parsed = parseInt(val, 10);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * Read a numCache/strCache/numLit/strLit element into raw (idx, string) pairs.
 * Only reads *direct* <c:pt> children — cache elements never nest pt inside pt.
 */
export function readCacheElement(cacheEl: Element): RawCacheResult {
  const ptCount = readPtCount(cacheEl);
  const ptEls = Array.from(cacheEl.children).filter(
    (c) => c.namespaceURI === OOXML_NS.c && c.localName === "pt"
  );

  const rawPoints = ptEls.map((pt) => {
    const idx = parseInt(pt.getAttribute("idx") ?? "0", 10);
    const vEl = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
    return { idx: Number.isFinite(idx) ? idx : 0, raw: vEl?.textContent ?? null };
  });

  const maxObservedIdx = rawPoints.length > 0 ? Math.max(...rawPoints.map((p) => p.idx)) : -1;
  const pointCount = ptCount ?? (maxObservedIdx >= 0 ? maxObservedIdx + 1 : 0);

  const formatCodeEl = cacheEl.getElementsByTagNameNS(OOXML_NS.c, "formatCode")[0];
  const formatCode = formatCodeEl?.textContent?.trim() || null;

  return { pointCount, rawPoints, formatCode, formula: null };
}

/**
 * Build a gap-filled ChartDataPoint<T> array of length `pointCount`, applying
 * `convert` to each present raw string. Missing indices become
 * { value: null, isMissing: true }; indices whose cached value fails to
 * convert (e.g. non-numeric text in a numCache) also become isMissing:true
 * so bad data never silently becomes 0/NaN in the model.
 */
export function buildDataPoints<T>(
  pointCount: number,
  rawPoints: Array<{ idx: number; raw: string | null }>,
  convert: (raw: string) => T | null,
  formatCode: string | null
): Array<ChartDataPoint<T>> {
  const points: Array<ChartDataPoint<T>> = [];
  for (let i = 0; i < pointCount; i++) {
    points.push({ index: i, value: null, displayValue: null, formatCode, isMissing: true });
  }

  for (const { idx, raw } of rawPoints) {
    if (idx < 0) continue;
    while (points.length <= idx) {
      points.push({ index: points.length, value: null, displayValue: null, formatCode, isMissing: true });
    }
    const converted = raw != null ? convert(raw) : null;
    points[idx] = {
      index: idx,
      value: converted,
      displayValue: raw,
      formatCode,
      isMissing: converted === null,
    };
  }

  return points;
}

export function convertToNumber(raw: string): number | null {
  return normalizeChartNumber(raw);
}

export function convertToString(raw: string): string | null {
  return raw;
}

/**
 * Locate a <c:f> formula element that is a direct child of `container`
 * (e.g. <c:numRef>, <c:strRef>) and return its trimmed text, or null.
 */
export function readFormula(container: Element): string | null {
  const f = Array.from(container.children).find(
    (c) => c.namespaceURI === OOXML_NS.c && c.localName === "f"
  );
  return f?.textContent?.trim() || null;
}

/**
 * Parse a <c:val>/<c:cat>/<c:xVal>/<c:yVal>/<c:tx> style container that may
 * hold either a `*Ref` (formula + cache) or a `*Lit` (literal, no formula)
 * child, producing a normalized ChartValueSource<T>.
 *
 * `refLocalName`/`cacheLocalName`/`litLocalName` let one function serve both
 * numeric (numRef/numCache/numLit) and string (strRef/strCache/strLit) shapes.
 */
export function parseValueSource<T>(
  container: Element | null,
  refLocalName: string,
  cacheLocalName: string,
  litLocalName: string,
  convert: (raw: string) => T | null
): ChartValueSource<T> {
  if (!container) {
    return { sourceKind: "missing", formula: null, formatCode: null, pointCount: null, points: [] };
  }

  const ref = container.getElementsByTagNameNS(OOXML_NS.c, refLocalName)[0];
  if (ref) {
    const formula = readFormula(ref);
    const cache = ref.getElementsByTagNameNS(OOXML_NS.c, cacheLocalName)[0];
    if (cache) {
      const { pointCount, rawPoints, formatCode } = readCacheElement(cache);
      return {
        sourceKind: "reference",
        formula,
        formatCode,
        pointCount,
        points: buildDataPoints(pointCount, rawPoints, convert, formatCode),
      };
    }
    return { sourceKind: formula ? "cache-only" : "missing", formula, formatCode: null, pointCount: null, points: [] };
  }

  const lit = container.getElementsByTagNameNS(OOXML_NS.c, litLocalName)[0];
  if (lit) {
    const { pointCount, rawPoints, formatCode } = readCacheElement(lit);
    return {
      sourceKind: "literal",
      formula: null,
      formatCode,
      pointCount,
      points: buildDataPoints(pointCount, rawPoints, convert, formatCode),
    };
  }

  return { sourceKind: "missing", formula: null, formatCode: null, pointCount: null, points: [] };
}
