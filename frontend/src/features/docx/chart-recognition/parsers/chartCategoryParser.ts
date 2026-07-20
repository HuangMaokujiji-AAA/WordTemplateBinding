import { OOXML_NS } from "../../ooxml/namespaces";
import type { WordChartCategory } from "../types";

/**
 * Parse a simple (single-level) category axis from a barChart element.
 *
 * Categories can come from:
 *  <c:cat><c:strRef><c:strCache> (most common)
 *  <c:cat><c:strLit>
 *  <c:cat><c:numRef><c:numCache>
 */
export function parseCategories(barChartEl: Element): WordChartCategory[] {
  // Category references are inside <c:ser> elements.
  // For bar charts, all series share the same categories,
  // so we read from the first series.
  // For bar charts, all series share the same categories,
  // so we can read from the first series.
  const firstSer = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
  if (!firstSer) return [];

  const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];
  if (!cat) return [];

  return parseCategoryElement(cat);
}

/**
 * Parse a <c:cat> element into WordChartCategory[].
 */
export function parseCategoryElement(cat: Element): WordChartCategory[] {
  // Try strRef → strCache
  const strRef = cat.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
  if (strRef) {
    const strCache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
    if (strCache) {
      return parseStrCache(strCache);
    }
  }

  // Try strLit
  const strLit = cat.getElementsByTagNameNS(OOXML_NS.c, "strLit")[0];
  if (strLit) {
    return parseStrCache(strLit);
  }

  // Try numRef → numCache
  const numRef = cat.getElementsByTagNameNS(OOXML_NS.c, "numRef")[0];
  if (numRef) {
    const numCache = numRef.getElementsByTagNameNS(OOXML_NS.c, "numCache")[0];
    if (numCache) {
      const pts = numCache.getElementsByTagNameNS(OOXML_NS.c, "pt");
      const ptList = Array.from(pts).sort((a, b) => {
        const idxA = parseInt(a.getAttribute("idx") ?? "0", 10);
        const idxB = parseInt(b.getAttribute("idx") ?? "0", 10);
        return idxA - idxB;
      });

      return ptList.map((pt) => {
        const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
        const val = v?.textContent?.trim() ?? "";
        return { value: val, displayValue: val };
      });
    }
  }

  return [];
}

/**
 * Parse a c:strCache (or c:strLit) element.
 * Returns categories sorted by pt idx, with nulls for missing indices.
 */
function parseStrCache(cache: Element): WordChartCategory[] {
  const pts = cache.getElementsByTagNameNS(OOXML_NS.c, "pt");
  const ptList = Array.from(pts).sort((a, b) => {
    const idxA = parseInt(a.getAttribute("idx") ?? "0", 10);
    const idxB = parseInt(b.getAttribute("idx") ?? "0", 10);
    return idxA - idxB;
  });

  if (ptList.length === 0) return [];

  const maxIdx = Math.max(
    ...ptList.map((p) => parseInt(p.getAttribute("idx") ?? "0", 10))
  );
  const result: WordChartCategory[] = new Array(maxIdx + 1);

  // Initialize with placeholder for missing indices
  for (let i = 0; i <= maxIdx; i++) {
    result[i] = { value: "", displayValue: "" };
  }

  for (const pt of ptList) {
    const idx = parseInt(pt.getAttribute("idx") ?? "0", 10);
    const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
    const val = v?.textContent?.trim() ?? "";
    result[idx] = { value: val, displayValue: val };
  }

  return result;
}
