import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordComboChartModel } from "../types";
import { parseAllSeries } from "./chartSeriesParser";
import { parseCategories } from "./chartCategoryParser";
import { parseMultiLevelCategories } from "./multiLevelCategoryParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartDataLabels } from "./chartDataLabelParser";
import { extractChartTitle } from "./barChartParser";

export async function parseComboChart(context: ChartParseContext): Promise<WordComboChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  if (!plotArea) throw new Error("plotArea not found");

  // Find all chart type elements
  const chartTypes: Array<{ localName: string; element: Element }> = [];
  for (const child of Array.from(plotArea.children)) {
    if (child.namespaceURI === OOXML_NS.c) {
      const ln = child.localName;
      if (ln === "barChart" || ln === "lineChart" || ln === "areaChart") {
        chartTypes.push({ localName: ln, element: child });
      }
    }
  }

  // Map localName → series type
  const typeMap: Record<string, string> = {
    barChart: "bar",
    lineChart: "line",
    areaChart: "line", // area rendered as line in combo for simplicity
  };

  // Parse all series across all chart type elements, tagging their chartType
  const allSeries: ReturnType<typeof parseAllSeries> = [];
  let categories: ReturnType<typeof parseCategories> = [];
  let useSecondaryAxis = false;

  for (const ct of chartTypes) {
    const ctSeries = parseAllSeries(ct.element);
    for (const s of ctSeries) {
      s.chartType = typeMap[ct.localName] ?? "bar";
      // Check if any series uses secondary axis
      // In OOXML, secondary axis is indicated by a separate axId
      // For combo charts, check if line series reference a different valAx
    }
    allSeries.push(...ctSeries);

    // Get categories from first chart type element that has them
    if (categories.length === 0) {
      const ml = parseMultiLevelCategories(ct.element);
      if (ml && ml.length > 0) {
        categories = ml;
      } else {
        categories = parseCategories(ct.element);
      }
    }
  }

  // Check for secondary axis (simplified: count distinct valAx ids)
  const allAxIds = new Set<string>();
  for (const ct of chartTypes) {
    const axIdEls = ct.element.getElementsByTagNameNS(OOXML_NS.c, "axId");
    for (const axIdEl of Array.from(axIdEls)) {
      const val = axIdEl.getAttribute("val");
      if (val) allAxIds.add(val);
    }
  }
  // If more than 2 axIds per chart type, might have secondary axis
  useSecondaryAxis = allAxIds.size > 4;

  // Parse axes directly from plotArea
  const allCatAxes = plotArea.getElementsByTagNameNS(OOXML_NS.c, "catAx");
  const allValAxes = plotArea.getElementsByTagNameNS(OOXML_NS.c, "valAx");

  const firstCatAx = allCatAxes[0];
  const firstValAx = allValAxes[0];
  const secondValAx = allValAxes[1];

  function parseAxisInfo(axisEl: Element | undefined) {
    if (!axisEl) return undefined;
    const title = axisEl.getElementsByTagNameNS(OOXML_NS.c, "title")[0];
    let titleText: string | undefined;
    if (title) {
      const tEls = title.getElementsByTagNameNS(OOXML_NS.a, "t");
      titleText = Array.from(tEls).map((t) => t.textContent ?? "").join("") || undefined;
    }
    const minEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "min")[0];
    const maxEl = axisEl.getElementsByTagNameNS(OOXML_NS.c, "max")[0];
    return {
      title: titleText,
      min: minEl ? parseFloat(minEl.getAttribute("val") ?? "") : undefined,
      max: maxEl ? parseFloat(maxEl.getAttribute("val") ?? "") : undefined,
    };
  }

  const xAxisInfo = parseAxisInfo(firstCatAx);
  const yAxisInfo = parseAxisInfo(firstValAx);
  const secondaryYAxisInfo = parseAxisInfo(secondValAx);

  // For combo, detect which series use secondary axis
  // Simplified: line series use secondary, bar series use primary
  for (const s of allSeries) {
    if (s.chartType === "line") {
      s.axis = useSecondaryAxis ? "secondary" : "primary";
    } else {
      s.axis = "primary";
    }
  }

  // Legend and title
  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  // Data labels from first chart type element
  const firstChartTypeEl = chartTypes[0]?.element;
  if (firstChartTypeEl) {
    const chartLabels = extractChartDataLabels(firstChartTypeEl);
    if (chartLabels) {
      for (const s of allSeries) {
        if (s.showValueLabel === undefined) s.showValueLabel = chartLabels.showValueLabel;
      }
    }
  }

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "combo", useSecondaryAxis,
    categories, series: allSeries, legend, widthPx, heightPx,
    title,
    xAxis: { title: xAxisInfo?.title, reversed: false },
    yAxis: { title: yAxisInfo?.title, min: yAxisInfo?.min, max: yAxisInfo?.max },
    secondaryYAxis: secondaryYAxisInfo ? {
      title: secondaryYAxisInfo.title,
      min: secondaryYAxisInfo.min,
      max: secondaryYAxisInfo.max,
    } : undefined,
  };
}
