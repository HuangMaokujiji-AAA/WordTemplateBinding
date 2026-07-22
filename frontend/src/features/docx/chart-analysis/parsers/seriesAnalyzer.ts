import { OOXML_NS } from "../../ooxml/namespaces";
import type {
  ChartDataLabelDefinition,
  ChartLineDefinition,
  ChartMarkerDefinition,
  ChartPointStyleOverride,
  ChartSeriesStyle,
  ParsedChartSeries,
  WordChartType,
} from "../models/types";
import { convertToNumber, convertToString, parseValueSource } from "./cacheParser";
import { resolveColorElement, resolveFillColor } from "./colorAnalyzer";
import { parseDataLabels } from "./dataLabelAnalyzer";

/**
 * Parses a single <c:ser> element into a ParsedChartSeries. `plotGroupId`
 * and `chartType` are supplied by the plot-group walker in chartXmlAnalyzer
 * (a single <c:ser> parser cannot know its own chart type in isolation —
 * that information lives on the enclosing <c:barChart>/<c:lineChart>/etc.).
 * `axisIds` are the raw <c:axId> values of the *enclosing* plot-group
 * element; axisRole is resolved later once all axes are known.
 */
export function parseSeries(
  serEl: Element,
  index: number,
  plotGroupId: string,
  chartType: WordChartType,
  axisIds: string[],
  themeColors: Record<string, string>,
  seriesKeyPrefix: string
): ParsedChartSeries {
  const orderEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "order")[0];
  const idxEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "idx")[0];
  const order = orderEl ? parseInt(orderEl.getAttribute("val") ?? String(index), 10) : index;
  const seriesIdx = idxEl ? parseInt(idxEl.getAttribute("val") ?? String(index), 10) : index;

  const tx = serEl.getElementsByTagNameNS(OOXML_NS.c, "tx")[0] ?? null;
  const nameSource = parseValueSource<string>(tx, "strRef", "strCache", "v", convertToString);
  // <c:tx><c:v>literal</c:v></c:tx> (no strRef/strLit wrapper) is common for series names.
  let name = nameSource.points[0]?.value ?? "";
  if (!name && tx) {
    const directV = Array.from(tx.children).find(
      (c) => c.namespaceURI === OOXML_NS.c && c.localName === "v"
    );
    name = directV?.textContent?.trim() ?? "";
  }
  if (!name) name = `系列 ${order + 1}`;

  const xValEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "xVal")[0] ?? null;
  const yValEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "yVal")[0] ?? null;
  const bubbleSizeEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "bubbleSize")[0] ?? null;
  const hasScatterShape = Boolean(xValEl || yValEl);

  // Scatter/bubble series carry no <c:val> — yVal doubles as the "values"
  // slot so the data table and binding schema still have a single primary
  // value source to point at, in addition to the dedicated x/y slots below.
  const valEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "val")[0] ?? null;
  const values = hasScatterShape
    ? parseValueSource<number>(yValEl, "numRef", "numCache", "numLit", convertToNumber)
    : parseValueSource<number>(valEl, "numRef", "numCache", "numLit", convertToNumber);

  const xValues = hasScatterShape
    ? parseValueSource<number>(xValEl, "numRef", "numCache", "numLit", convertToNumber)
    : undefined;
  const yValues = hasScatterShape
    ? parseValueSource<number>(yValEl, "numRef", "numCache", "numLit", convertToNumber)
    : undefined;
  const bubbleSizes = bubbleSizeEl
    ? parseValueSource<number>(bubbleSizeEl, "numRef", "numCache", "numLit", convertToNumber)
    : undefined;

  const style = parseSeriesStyle(serEl, themeColors);
  const marker = parseMarker(serEl, themeColors);
  const line = parseLine(serEl, themeColors, chartType);
  const dataLabels = parseDataLabels(serEl);

  const hiddenEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "hidden")[0];
  const hidden = hiddenEl?.getAttribute("val") === "1";

  return {
    key: `${seriesKeyPrefix}-s${seriesIdx}`,
    index: seriesIdx,
    order,
    name,
    nameSource,
    chartType,
    plotGroupId,
    axisRole: "none",
    axisIds,
    values,
    xValues,
    yValues,
    bubbleSizes,
    style,
    marker,
    line,
    dataLabels,
    hidden,
  };
}

function parseSeriesStyle(serEl: Element, themeColors: Record<string, string>): ChartSeriesStyle {
  const spPr = serEl.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0] ?? null;
  const fill = resolveFillColor(spPr, themeColors);
  const line = spPr ? parseLineFromSpPr(spPr, themeColors) : null;

  const explosionEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "explosion")[0];
  const explosion = explosionEl ? parseInt(explosionEl.getAttribute("val") ?? "0", 10) : null;

  const invertEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "invertIfNegative")[0];
  const invertIfNegative = invertEl ? invertEl.getAttribute("val") !== "0" : null;

  const pointOverrides = parsePointOverrides(serEl, themeColors);

  return { fill, line, explosion, invertIfNegative, pointOverrides };
}

function parsePointOverrides(
  serEl: Element,
  themeColors: Record<string, string>
): ChartPointStyleOverride[] {
  const dPts = Array.from(serEl.children).filter(
    (c) => c.namespaceURI === OOXML_NS.c && c.localName === "dPt"
  );
  return dPts.map((dPt) => {
    const idxEl = dPt.getElementsByTagNameNS(OOXML_NS.c, "idx")[0];
    const pointIndex = idxEl ? parseInt(idxEl.getAttribute("val") ?? "0", 10) : 0;
    const spPr = dPt.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0] ?? null;
    return { pointIndex, fill: resolveFillColor(spPr, themeColors) };
  });
}

function parseLineFromSpPr(
  spPr: Element,
  themeColors: Record<string, string>
): ChartLineDefinition | null {
  const ln = spPr.getElementsByTagNameNS(OOXML_NS.a, "ln")[0];
  if (!ln) return null;

  const noFill = ln.getElementsByTagNameNS(OOXML_NS.a, "noFill")[0];
  const solidFill = ln.getElementsByTagNameNS(OOXML_NS.a, "solidFill")[0];
  const color = solidFill ? resolveColorElement(solidFill, themeColors) : null;

  const widthAttr = ln.getAttribute("w");
  const widthPt = widthAttr ? parseInt(widthAttr, 10) / 12700 : null; // EMU -> points

  const dashEl = ln.getElementsByTagNameNS(OOXML_NS.a, "prstDash")[0];

  return {
    color,
    widthPt: widthPt != null && Number.isFinite(widthPt) ? widthPt : null,
    dashStyle: dashEl?.getAttribute("val") ?? null,
    smooth: false,
    noFill: Boolean(noFill),
  };
}

function parseMarker(
  serEl: Element,
  themeColors: Record<string, string>
): ChartMarkerDefinition | null {
  const marker = serEl.getElementsByTagNameNS(OOXML_NS.c, "marker")[0];
  if (!marker) return null;

  const symbolEl = marker.getElementsByTagNameNS(OOXML_NS.c, "symbol")[0];
  const sizeEl = marker.getElementsByTagNameNS(OOXML_NS.c, "size")[0];
  const spPr = marker.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0] ?? null;
  const fill = resolveFillColor(spPr, themeColors);

  return {
    symbol: symbolEl?.getAttribute("val") ?? null,
    size: sizeEl ? parseInt(sizeEl.getAttribute("val") ?? "0", 10) : null,
    color: fill?.color ?? null,
  };
}

function parseLine(
  serEl: Element,
  themeColors: Record<string, string>,
  chartType: WordChartType
): ChartLineDefinition | null {
  if (chartType !== "line" && chartType !== "scatter" && chartType !== "area") return null;
  const spPr = serEl.getElementsByTagNameNS(OOXML_NS.c, "spPr")[0];
  const base = spPr ? parseLineFromSpPr(spPr, themeColors) : null;

  const smoothEl = serEl.getElementsByTagNameNS(OOXML_NS.c, "smooth")[0];
  const smooth = smoothEl?.getAttribute("val") === "1";

  if (base) return { ...base, smooth };
  return smoothEl ? { color: null, widthPt: null, dashStyle: null, smooth, noFill: false } : null;
}

export type { ChartDataLabelDefinition };
