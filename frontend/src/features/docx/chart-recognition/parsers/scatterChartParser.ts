import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordScatterChartModel, WordChartSeries } from "../types";
import { parseBarChartAxes } from "./chartAxisParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartTitle } from "./barChartParser";
import { normalizeChartNumber } from "../utils/numberUtils";

export async function parseScatterChart(context: ChartParseContext): Promise<WordScatterChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const scatterEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "scatterChart")[0];
  if (!scatterEl) throw new Error("scatterChart element not found");

  // Scatter style: "lineMarker" | "line" | "marker" | "smooth" | "none"
  const scatterStyleEl = scatterEl.getElementsByTagNameNS(OOXML_NS.c, "scatterStyle")[0];
  const scatterStyle = scatterStyleEl?.getAttribute("val") ?? "lineMarker";

  // Check varyColors (if false, all series use same color)
  const varyColorsEl = scatterEl.getElementsByTagNameNS(OOXML_NS.c, "varyColors")[0];
  const varyColors = varyColorsEl?.getAttribute("val") !== "0";

  // Parse scatter-specific series (uses <c:xVal> and <c:yVal>)
  const series = parseScatterSeries(scatterEl, varyColors);

  const { catAxes, valAxes } = parseBarChartAxes(scatterEl, plotArea!);
  const catAxis = catAxes[0];
  const valAxis = valAxes[0];

  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  // Smooth
  const smoothEl = scatterEl.getElementsByTagNameNS(OOXML_NS.c, "smooth")[0];
  const isSmooth = smoothEl?.getAttribute("val") === "1";

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "scatter", scatterStyle: isSmooth ? "smooth" : scatterStyle,
    categories: [], series, legend, widthPx, heightPx,
    title,
    xAxis: {
      title: catAxis?.title,
      min: catAxis?.min,
      max: catAxis?.max,
      numberFormat: catAxis?.numberFormat,
    },
    yAxis: {
      title: valAxis?.title,
      min: valAxis?.min,
      max: valAxis?.max,
      numberFormat: valAxis?.numberFormat,
    },
  };
}

/**
 * Parse scatter series.
 *
 * Scatter chart series use:
 *   <c:xVal> for X coordinate values
 *   <c:yVal> for Y coordinate values
 *   (NOT <c:val> like bar/line charts)
 *
 * Both xVal and yVal contain <c:numRef><c:numCache><c:pt> elements
 * with idx-sorted data points.
 */
function parseScatterSeries(
  parent: Element,
  varyColors: boolean
): WordChartSeries[] {
  const serElements = parent.getElementsByTagNameNS(OOXML_NS.c, "ser");
  const defaultPalette = ["#FFC000", "#2E74B5", "#4472C4", "#ED7D31", "#A5A5A5", "#5B9BD5"];

  return Array.from(serElements).map((serEl, seriesIdx) => {
    // Parse series name
    const name = extractScatterSeriesName(serEl, seriesIdx);

    // Parse Y values from <c:yVal>
    const yVal = serEl.getElementsByTagNameNS(OOXML_NS.c, "yVal")[0];
    const yValues = yVal ? extractValuesFromVal(yVal) : [];

    // Parse X values from <c:xVal>
    const xVal = serEl.getElementsByTagNameNS(OOXML_NS.c, "xVal")[0];
    const xValues = xVal ? extractValuesFromVal(xVal) : [];

    // Parse color from <c:marker><c:spPr><a:solidFill>
    let color = extractMarkerColor(serEl);

    // If varyColors is false, use palette by index
    if (!varyColors || !color) {
      color = defaultPalette[seriesIdx % defaultPalette.length];
    }

    return {
      name,
      values: yValues,
      xValues,
      color,
      chartType: "scatter",
    };
  });
}

/**
 * Extract series name for scatter charts.
 *
 * Order of preference:
 *   1. <c:tx><c:strRef><c:strCache><c:pt><c:v>
 *   2. Extension: <c15:filteredSeriesTitle> → <c15:tx> → <c:strRef>
 *   3. Fallback: "系列 N"
 */
function extractScatterSeriesName(serEl: Element, idx: number): string {
  // Standard path
  const tx = serEl.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
  if (tx) {
    const strRef = tx.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
    if (strRef) {
      const strCache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
      if (strCache) {
        const pt = strCache.getElementsByTagNameNS(OOXML_NS.c, "pt")[0];
        if (pt) {
          const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
          if (v?.textContent?.trim()) return v.textContent.trim();
        }
      }
    }
  }

  // Extension path: c:extLst → c:ext → c15:filteredSeriesTitle → c15:tx → c:strRef
  const extLst = serEl.getElementsByTagNameNS(OOXML_NS.c, "extLst")[0];
  if (extLst) {
    const exts = extLst.getElementsByTagNameNS(OOXML_NS.c, "ext");
    for (const ext of Array.from(exts)) {
      // Look for filteredSeriesTitle (namespace varies)
      const filteredTitle = ext.firstElementChild;
      if (filteredTitle && filteredTitle.localName === "filteredSeriesTitle") {
        const extTx = filteredTitle.getElementsByTagNameNS(OOXML_NS.c, "tx")[0]
          ?? (filteredTitle.firstElementChild?.localName === "tx"
            ? filteredTitle.firstElementChild
            : null);
        if (extTx) {
          const strRef = extTx.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
          if (strRef) {
            const strCache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
            if (strCache) {
              const pt = strCache.getElementsByTagNameNS(OOXML_NS.c, "pt")[0];
              if (pt) {
                const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
                if (v?.textContent?.trim()) return v.textContent.trim();
              }
            }
          }
        }
      }
    }
  }

  return `系列 ${idx + 1}`;
}

/**
 * Extract numeric values from <c:xVal> or <c:yVal>.
 *
 * Both use the structure: <c:numRef><c:numCache><c:pt idx="N"><c:v>
 */
function extractValuesFromVal(valEl: Element): Array<number | null> {
  const numRef = valEl.getElementsByTagNameNS(OOXML_NS.c, "numRef")[0];
  if (numRef) {
    const numCache = numRef.getElementsByTagNameNS(OOXML_NS.c, "numCache")[0];
    if (numCache) {
      return extractPtValues(numCache);
    }
  }

  const numLit = valEl.getElementsByTagNameNS(OOXML_NS.c, "numLit")[0];
  if (numLit) {
    return extractPtValues(numLit);
  }

  return [];
}

/**
 * Extract sorted <c:pt idx="N"><c:v>value</c:v></c:pt> values.
 */
function extractPtValues(cache: Element): Array<number | null> {
  const pts = Array.from(cache.getElementsByTagNameNS(OOXML_NS.c, "pt"))
    .sort((a, b) =>
      parseInt(a.getAttribute("idx") ?? "0", 10) -
      parseInt(b.getAttribute("idx") ?? "0", 10)
    );

  if (pts.length === 0) return [];

  const maxIdx = Math.max(
    ...pts.map((p) => parseInt(p.getAttribute("idx") ?? "0", 10))
  );
  const result: Array<number | null> = new Array(maxIdx + 1).fill(null);

  for (const pt of pts) {
    const idx = parseInt(pt.getAttribute("idx") ?? "0", 10);
    const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0];
    result[idx] = v?.textContent ? normalizeChartNumber(v.textContent) : null;
  }

  return result;
}

/**
 * Extract marker color from <c:marker><c:spPr><a:solidFill>.
 */
function extractMarkerColor(serEl: Element): string | undefined {
  const marker = serEl.getElementsByTagNameNS(OOXML_NS.c, "marker")[0];
  if (!marker) return undefined;

  const spPr = marker.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0];
  if (!spPr) return undefined;

  const solidFill = spPr.getElementsByTagNameNS(OOXML_NS.a, "solidFill")[0];
  if (!solidFill) return undefined;

  const srgbClr = solidFill.getElementsByTagNameNS(OOXML_NS.a, "srgbClr")[0];
  if (srgbClr) {
    const val = srgbClr.getAttribute("val");
    if (val) return `#${val}`;
  }

  return undefined;
}
