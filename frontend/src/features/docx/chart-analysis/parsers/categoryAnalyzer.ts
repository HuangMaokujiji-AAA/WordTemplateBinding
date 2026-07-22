import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartCategoryDefinition, ChartCategoryValueType } from "../models/types";
import { buildDataPoints, readCacheElement, readFormula } from "./cacheParser";

/**
 * Parses a <c:cat> (or <c:xVal> when scatter category-like use is needed)
 * element into ChartCategoryDefinition[], covering:
 *   strRef+strCache, strLit, numRef+numCache, numLit, multiLvlStrRef+multiLvlStrCache
 *
 * Unlike the legacy chartCategoryParser/multiLevelCategoryParser, this keeps
 * a single gap-filling code path (via cacheParser.buildDataPoints) shared
 * with series/scatter value parsing, so sparse idx behaves identically
 * everywhere in the model.
 */
export function parseCategoryContainer(catEl: Element | null): ChartCategoryDefinition[] {
  if (!catEl) return [];

  const multiRef = catEl.getElementsByTagNameNS(OOXML_NS.c, "multiLvlStrRef")[0];
  if (multiRef) {
    const formula = readFormula(multiRef);
    const cache = multiRef.getElementsByTagNameNS(OOXML_NS.c, "multiLvlStrCache")[0];
    if (cache) return parseMultiLevelCache(cache, formula);
  }

  const strRef = catEl.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
  if (strRef) {
    const formula = readFormula(strRef);
    const cache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
    if (cache) return parseSingleLevelCache(cache, formula, "string", null);
    return [];
  }

  const strLit = catEl.getElementsByTagNameNS(OOXML_NS.c, "strLit")[0];
  if (strLit) {
    return parseSingleLevelCache(strLit, null, "string", null);
  }

  const numRef = catEl.getElementsByTagNameNS(OOXML_NS.c, "numRef")[0];
  if (numRef) {
    const formula = readFormula(numRef);
    const cache = numRef.getElementsByTagNameNS(OOXML_NS.c, "numCache")[0];
    if (cache) {
      const formatCode = readFormatCode(cache);
      const valueType = inferNumericCategoryType(formatCode);
      return parseSingleLevelCache(cache, formula, valueType, formatCode);
    }
    return [];
  }

  const numLit = catEl.getElementsByTagNameNS(OOXML_NS.c, "numLit")[0];
  if (numLit) {
    const formatCode = readFormatCode(numLit);
    const valueType = inferNumericCategoryType(formatCode);
    return parseSingleLevelCache(numLit, null, valueType, formatCode);
  }

  return [];
}

function readFormatCode(cacheEl: Element): string | null {
  const el = cacheEl.getElementsByTagNameNS(OOXML_NS.c, "formatCode")[0];
  return el?.textContent?.trim() || null;
}

const DATE_FORMAT_PATTERN = /[ymdhs]/i;
const GENERAL_FORMAT_PATTERN = /^general$/i;

function inferNumericCategoryType(formatCode: string | null): "number" | "date" {
  if (!formatCode || GENERAL_FORMAT_PATTERN.test(formatCode)) return "number";
  // Strip quoted literal sections before checking for date tokens so
  // formats like "0\"kg\"" aren't misread as containing a "d".
  const withoutLiterals = formatCode.replace(/"[^"]*"/g, "");
  return DATE_FORMAT_PATTERN.test(withoutLiterals) ? "date" : "number";
}

function parseSingleLevelCache(
  cacheEl: Element,
  formula: string | null,
  valueType: ChartCategoryValueType,
  formatCode: string | null
): ChartCategoryDefinition[] {
  const { pointCount, rawPoints } = readCacheElement(cacheEl);
  const convert = valueType === "string" ? (raw: string) => raw : (raw: string) => {
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
  };

  const points = buildDataPoints<string | number>(pointCount, rawPoints, convert, formatCode);

  return points.map((pt) => ({
    index: pt.index,
    value: pt.value,
    displayValue: pt.value == null ? "" : String(pt.value),
    valueType,
    levels: [],
    sourceFormula: formula,
    numberFormat: formatCode,
    isMissing: pt.isMissing,
  }));
}

/**
 * Multi-level category convention used by this model. Verified against a
 * real Word-generated <c:multiLvlStrCache> (values are forward-filled
 * sparse entries, per OOXML §21.2.2.152): the FIRST <c:lvl> in document
 * order is the one that changes on nearly every point (the finest-grained,
 * "innermost" level — e.g. region alternating every row), and each
 * subsequent <c:lvl> groups more coarsely (sparser forward-fill — e.g.
 * grade spanning several rows, the "outermost" level). So XML document
 * order is [innermost, ..., outermost].
 *
 * `levels` on the resulting ChartCategoryDefinition preserves that same
 * [innermost, ..., outermost] order; `value`/`displayValue` present the
 * outermost (broadest grouping) label first, matching how Word itself
 * stacks multi-level axis labels with the coarsest group at the bottom.
 */
function parseMultiLevelCache(
  multiCacheEl: Element,
  formula: string | null
): ChartCategoryDefinition[] {
  const ptCountEl = multiCacheEl.getElementsByTagNameNS(OOXML_NS.c, "ptCount")[0];
  const ptCount = ptCountEl ? parseInt(ptCountEl.getAttribute("val") ?? "0", 10) : 0;
  if (!ptCount) return [];

  const lvlEls = Array.from(multiCacheEl.children).filter(
    (c) => c.namespaceURI === OOXML_NS.c && c.localName === "lvl"
  );

  // Document order: [innermost, ..., outermost].
  const levelsInnerToOuter: Array<Map<number, string>> = lvlEls.map((lvl) => {
    const map = new Map<number, string>();
    for (const pt of Array.from(lvl.children)) {
      if (pt.namespaceURI !== OOXML_NS.c || pt.localName !== "pt") continue;
      const idx = parseInt(pt.getAttribute("idx") ?? "0", 10);
      const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
      map.set(idx, v?.textContent?.trim() ?? "");
    }
    return map;
  });

  const categories: ChartCategoryDefinition[] = [];

  for (let i = 0; i < ptCount; i++) {
    const innerToOuter: string[] = levelsInnerToOuter.map((levelMap) => {
      for (let j = i; j >= 0; j--) {
        const v = levelMap.get(j);
        if (v !== undefined) return v;
      }
      return "";
    });

    const outerToInner = [...innerToOuter].reverse();
    const value = outerToInner[0] ?? "";
    const displayValue = outerToInner.filter((s) => s !== "").join(" / ");

    categories.push({
      index: i,
      value,
      displayValue,
      valueType: "string",
      levels: innerToOuter,
      sourceFormula: formula,
      numberFormat: null,
      isMissing: innerToOuter.every((s) => s === ""),
    });
  }

  return categories;
}
