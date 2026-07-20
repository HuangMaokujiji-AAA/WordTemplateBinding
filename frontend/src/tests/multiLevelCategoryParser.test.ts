import { describe, it, expect } from "vitest";
import { parseMultiLvlStrCache } from "../features/docx/chart-recognition/parsers/multiLevelCategoryParser";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";

/**
 * Build a test multiLvlStrCache XML.
 *
 * Simulates chart2 / chart6 structure:
 *   Level 0 (outer label): 全省 / 你县 (repeats)
 *   Level 1 (inner label): 八年级 / 四年级
 */
function makeMultiCacheXml(
  ptCount: number,
  levels: Array<Array<[number, string]>>
): string {
  const lvlXml = levels
    .map(
      (pts) =>
        `<c:lvl>${pts
          .map(
            ([idx, val]) =>
              `<c:pt idx="${idx}"><c:v>${val}</c:v></c:pt>`
          )
          .join("")}</c:lvl>`
    )
    .join("\n");

  return `<?xml version="1.0"?>
<c:multiLvlStrCache xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
  <c:ptCount val="${ptCount}"/>
  ${lvlXml}
</c:multiLvlStrCache>`;
}

describe("parseMultiLevelCategories", () => {
  it("parses 八年级/全省 → 八年级/你县 → 四年级/全省 → 四年级/你县", () => {
    const xml = makeMultiCacheXml(4, [
      // Level 0: outer labels (全省/你县)
      [
        [0, "全省"],
        [1, "你县"],
        [2, "全省"],
        [3, "你县"],
      ],
      // Level 1: inner labels (八年级/四年级)
      [
        [0, "八年级"],
        [2, "四年级"],
      ],
    ]);

    const doc = parseXmlString(xml);
    const cacheEl = doc.documentElement;
    const categories = parseMultiLvlStrCache(cacheEl);

    expect(categories).toHaveLength(4);

    // Index 0: 全省 + 八年级; levels = [inner, outer]; value = outermost
    expect(categories[0].value).toBe("全省");
    expect(categories[0].levels).toEqual(["八年级", "全省"]);
    expect(categories[0].displayValue).toBe("八年级\n全省");
    expect(categories[0].isGroupStart).toBe(true);

    // Index 1: 你县 + 八年级 (forward-filled outer)
    expect(categories[1].value).toBe("你县");
    expect(categories[1].levels).toEqual(["八年级", "你县"]);
    expect(categories[1].displayValue).toBe("八年级\n你县");
    expect(categories[1].isGroupStart).toBe(false);

    // Index 2: 全省 + 四年级; inner level changes → isGroupStart
    expect(categories[2].value).toBe("全省");
    expect(categories[2].levels).toEqual(["四年级", "全省"]);
    expect(categories[2].displayValue).toBe("四年级\n全省");
    expect(categories[2].isGroupStart).toBe(true);

    // Index 3: 你县 + 四年级
    expect(categories[3].value).toBe("你县");
    expect(categories[3].levels).toEqual(["四年级", "你县"]);
    expect(categories[3].displayValue).toBe("四年级\n你县");
    expect(categories[3].isGroupStart).toBe(false);
  });

  it("handles single level categories", () => {
    const xml = makeMultiCacheXml(3, [
      [[0, "A"], [1, "B"], [2, "C"]],
    ]);

    const doc = parseXmlString(xml);
    const categories = parseMultiLvlStrCache(doc.documentElement);

    expect(categories).toHaveLength(3);
    expect(categories[0].value).toBe("A");
    expect(categories[1].value).toBe("B");
    expect(categories[2].value).toBe("C");
  });

  it("returns empty array for empty ptCount", () => {
    const xml = `<?xml version="1.0"?>
<c:multiLvlStrCache xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
  <c:ptCount val="0"/>
</c:multiLvlStrCache>`;

    const doc = parseXmlString(xml);
    const categories = parseMultiLvlStrCache(doc.documentElement);
    expect(categories).toHaveLength(0);
  });

  it("forward-fills outer labels correctly with sparse data", () => {
    const xml = makeMultiCacheXml(5, [
      // Level 0: geographical regions (sparse)
      [[0, "东部"], [3, "西部"]],
      // Level 1: subjects (dense)
      [[0, "语文"], [1, "数学"], [2, "英语"], [3, "物理"], [4, "化学"]],
    ]);

    const doc = parseXmlString(xml);
    const categories = parseMultiLvlStrCache(doc.documentElement);

    expect(categories).toHaveLength(5);

    // 东部 should forward-fill through indices 0, 1, 2
    // levels = [inner, outer] after reversal
    expect(categories[0].levels).toEqual(["语文", "东部"]);
    expect(categories[1].levels).toEqual(["数学", "东部"]);
    expect(categories[2].levels).toEqual(["英语", "东部"]);

    // 西部 should apply from index 3 onward
    expect(categories[3].levels).toEqual(["物理", "西部"]);
    expect(categories[4].levels).toEqual(["化学", "西部"]);
  });
});
