import { OOXML_NS } from "../../ooxml/namespaces";
import { normalizeChartNumber } from "../utils/numberUtils";
import type { WordChartSeries } from "../types";

interface SeriesColorInfo {
  solidFill?: string;
  schemeClr?: string;
}

/**
 * Extract series color from <c:spPr> inside a <c:ser> element.
 *
 * Supports:
 *  <a:solidFill><a:srgbClr val="4472C4"/>
 *  <a:solidFill><a:schemeClr val="accent1"/>
 *
 * Returns the sRGB hex string (without #) or the scheme color name.
 */
export function extractSeriesColorInfo(serEl: Element): SeriesColorInfo {
  const spPr = serEl.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0];
  if (!spPr) return {};

  const solidFill = spPr.getElementsByTagNameNS(OOXML_NS.a, "solidFill")[0];
  if (!solidFill) return {};

  const srgbClr = solidFill.getElementsByTagNameNS(OOXML_NS.a, "srgbClr")[0];
  if (srgbClr) {
    const val = srgbClr.getAttribute("val");
    if (val) return { solidFill: val };
  }

  const schemeClr = solidFill.getElementsByTagNameNS(OOXML_NS.a, "schemeClr")[0];
  if (schemeClr) {
    const val = schemeClr.getAttribute("val");
    if (val) return { schemeClr: val };
  }

  return {};
}

/**
 * Extract the number format code from a series element.
 */
export function extractNumberFormat(serEl: Element): string | undefined {
  const numFmt = serEl.getElementsByTagNameNS(OOXML_NS.c, "numFmt")[0];
  if (numFmt) {
    return numFmt.getAttribute("formatCode") ?? undefined;
  }
  return undefined;
}

/**
 * Parse data label settings from <c:dLbls> in a series or chart-level.
 */
export function extractDataLabels(
  parent: Element
): { showValueLabel: boolean; dataLabelPosition?: string } | undefined {
  const dLbls = parent.getElementsByTagNameNS(OOXML_NS.c, "dLbls")[0];
  if (!dLbls) return undefined;

  const showVal = dLbls.getElementsByTagNameNS(OOXML_NS.c, "showVal")[0];
  const showValAttr = showVal?.getAttribute("val");

  const dLblPos = dLbls.getElementsByTagNameNS(OOXML_NS.c, "dLblPos")[0];
  const dLblPosVal = dLblPos?.getAttribute("val");

  return {
    showValueLabel: showValAttr === "1",
    dataLabelPosition: dLblPosVal ?? undefined,
  };
}

/**
 * Parse a single <c:ser> element into a WordChartSeries.
 */
export function parseChartSeries(serEl: Element): WordChartSeries {
  const order = serEl.getElementsByTagNameNS(OOXML_NS.c, "order")[0];

  // Parse series name
  let seriesName = "";
  const tx = serEl.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
  if (tx) {
    // Try <c:strRef><c:strCache><c:pt><c:v>
    const strRef = tx.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
    if (strRef) {
      const strCache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
      if (strCache) {
        const pt = strCache.getElementsByTagNameNS(OOXML_NS.c, "pt")[0];
        if (pt) {
          const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
          seriesName = v?.textContent?.trim() ?? "";
        }
      }
    }

    // Fallback: <c:v> directly inside <c:tx>
    if (!seriesName) {
      const v = tx.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
      seriesName = v?.textContent?.trim() ?? "";
    }
  }

  // If still no name, try the idx + order
  if (!seriesName) {
    const orderVal = order?.getAttribute("val") ?? "0";
    seriesName = `系列 ${parseInt(orderVal, 10) + 1}`;
  }

  // Parse values from <c:val><c:numRef><c:numCache>
  const values: Array<number | null> = [];
  const val = serEl.getElementsByTagNameNS(OOXML_NS.c, "val")[0];
  if (val) {
    const numRef = val.getElementsByTagNameNS(OOXML_NS.c, "numRef")[0];
    if (numRef) {
      const numCache = numRef.getElementsByTagNameNS(OOXML_NS.c, "numCache")[0];
      if (numCache) {
        const pts = numCache.getElementsByTagNameNS(OOXML_NS.c, "pt");
        const ptList = Array.from(pts);
        ptList.sort((a, b) => {
          const idxA = parseInt(a.getAttribute("idx") ?? "0", 10);
          const idxB = parseInt(b.getAttribute("idx") ?? "0", 10);
          return idxA - idxB;
        });

        for (const pt of ptList) {
          const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
          if (v?.textContent) {
            values.push(normalizeChartNumber(v.textContent));
          } else {
            values.push(null);
          }
        }
      }
    }

    // Fallback: <c:numLit>
    if (values.length === 0) {
      const numLit = val.getElementsByTagNameNS(OOXML_NS.c, "numLit")[0];
      if (numLit) {
        const pts = numLit.getElementsByTagNameNS(OOXML_NS.c, "pt");
        const ptList = Array.from(pts);
        ptList.sort((a, b) => {
          const idxA = parseInt(a.getAttribute("idx") ?? "0", 10);
          const idxB = parseInt(b.getAttribute("idx") ?? "0", 10);
          return idxA - idxB;
        });

        for (const pt of ptList) {
          const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
          if (v?.textContent) {
            values.push(normalizeChartNumber(v.textContent));
          } else {
            values.push(null);
          }
        }
      }
    }
  }

  // Parse source formula
  let sourceFormula: string | undefined;
  if (val) {
    const numRefF = val.getElementsByTagNameNS(OOXML_NS.c, "numRef")[0];
    if (numRefF) {
      const f = numRefF.getElementsByTagNameNS(OOXML_NS.c, "f")[0];
      sourceFormula = f?.textContent?.trim() || undefined;
    }
  }
  if (!sourceFormula && tx) {
    const strRefF = tx.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
    if (strRefF) {
      const f = strRefF.getElementsByTagNameNS(OOXML_NS.c, "f")[0];
      sourceFormula = f?.textContent?.trim() || undefined;
    }
  }

  // Color
  const colorInfo = extractSeriesColorInfo(serEl);
  let color: string | undefined;
  if (colorInfo.solidFill) {
    color = `#${colorInfo.solidFill}`;
  }
  // For scheme colors, we resolve later in the mapper when theme is available

  // Data labels
  const dLblsInfo = extractDataLabels(serEl);

  // Number format
  const numberFormat = extractNumberFormat(serEl);

  return {
    name: seriesName,
    values,
    color,
    showValueLabel: dLblsInfo?.showValueLabel,
    dataLabelPosition: dLblsInfo?.dataLabelPosition,
    sourceFormula,
    numberFormat,
  };
}

/**
 * Parse all series from a barChart element.
 * Series are sorted by their order attribute.
 */
export function parseAllSeries(barChartEl: Element): WordChartSeries[] {
  const serElements = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "ser");
  const seriesList = Array.from(serElements).map(parseChartSeries);

  // Series are already in document order; preserve it

  return seriesList;
}
