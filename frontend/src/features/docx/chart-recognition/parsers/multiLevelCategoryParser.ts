import { OOXML_NS } from "../../ooxml/namespaces";
import type { WordChartCategory } from "../types";

/**
 * Parse multi-level category axis data from a barChart element.
 *
 * Target documents (chart2, chart6) contain:
 *  <c:multiLvlStrRef>
 *    <c:multiLvlStrCache>
 *      <c:ptCount val="4"/>
 *      <c:lvl>...</c:lvl>
 *      <c:lvl>...</c:lvl>
 *
 * Each <c:lvl> represents one category level.
 * Within each level, <c:pt> elements mark where a new group label starts.
 * Labels from higher levels are forward-filled to fill gaps.
 *
 * Example:
 *   Level 0 (outer):  0→全省, 1→你县, 2→全省, 3→你县
 *   Level 1 (inner):  0→八年级, 2→四年级
 *
 * Results in:
 *   [全省/八年级, 你县/八年级, 全省/四年级, 你县/四年级]
 */
export function parseMultiLevelCategories(
  barChartEl: Element
): WordChartCategory[] | null {
  // Check if any series has multiLvlStrRef
  const firstSer = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
  if (!firstSer) return null;

  const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];
  if (!cat) return null;

  const multiRef = cat.getElementsByTagNameNS(OOXML_NS.c, "multiLvlStrRef")[0];
  if (!multiRef) return null;

  const multiCache = multiRef.getElementsByTagNameNS(
    OOXML_NS.c,
    "multiLvlStrCache"
  )[0];
  if (!multiCache) return null;

  return parseMultiLvlStrCache(multiCache);
}

/**
 * Parse a <c:multiLvlStrCache> element.
 *
 * Algorithm:
 *  1. Determine ptCount (number of final data points).
 *  2. Read each <c:lvl> (category level).
 *  3. Within each level, <c:pt idx="..."> gives a label starting at that index.
 *  4. Forward-fill labels from each level across all data points.
 *  5. Combine levels into displayValue (innermost level is the primary value).
 */
export function parseMultiLvlStrCache(
  multiCache: Element
): WordChartCategory[] {
  const ptCountEl = multiCache.getElementsByTagNameNS(OOXML_NS.c, "ptCount")[0];
  const ptCount = ptCountEl
    ? parseInt(ptCountEl.getAttribute("val") ?? "0", 10)
    : 0;

  if (ptCount === 0) return [];

  // Read all levels
  const lvls = multiCache.getElementsByTagNameNS(OOXML_NS.c, "lvl");
  const levelsData: Array<Map<number, string>> = [];

  for (const lvl of Array.from(lvls)) {
    const pts = lvl.getElementsByTagNameNS(OOXML_NS.c, "pt");
    const levelMap = new Map<number, string>();

    for (const pt of Array.from(pts)) {
      const idx = parseInt(pt.getAttribute("idx") ?? "0", 10);
      const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
      const val = v?.textContent?.trim() ?? "";
      levelMap.set(idx, val);
    }

    levelsData.push(levelMap);
  }

  // Build categories by forward-filling each level
  const categories: WordChartCategory[] = [];

  for (let i = 0; i < ptCount; i++) {
    const levelValues: string[] = [];

    // Collect the label for each level at index i
    // Forward-fill: find the most recent pt with idx <= i
    for (let li = 0; li < levelsData.length; li++) {
      const levelMap = levelsData[li];
      let value = "";

      // Find the most recent label (max idx <= i)
      // Walk backwards from i to find the most recent label
      for (let j = i; j >= 0; j--) {
        if (levelMap.has(j)) {
          value = levelMap.get(j)!;
          break;
        }
      }

      levelValues.push(value);
    }

    // levelValues is [outer, ..., inner] from the XML lvl order.
    // We need [inner, ..., outer] for the model (spec convention).
    const reversedLevels = [...levelValues].reverse();
    // value: outermost label (last element after reversal)
    const value = reversedLevels[reversedLevels.length - 1] || "";
    // displayValue: all levels joined with newline
    const displayValue = reversedLevels.join("\n");

    // isGroupStart: true when the innermost level (reversedLevels[0]) changes
    const isGroupStart =
      i === 0 ||
      reversedLevels[0] !== categories[i - 1]?.levels?.[0];

    categories.push({
      value,
      levels: reversedLevels,
      displayValue,
      isGroupStart,
    });
  }

  return categories;
}
