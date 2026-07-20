import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordLineChartModel } from "../types";
import { parseAllSeries } from "./chartSeriesParser";
import { parseCategories } from "./chartCategoryParser";
import { parseMultiLevelCategories } from "./multiLevelCategoryParser";
import { parseBarChartAxes } from "./chartAxisParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartDataLabels } from "./chartDataLabelParser";
import { extractChartTitle } from "./barChartParser";

export async function parseLineChart(context: ChartParseContext): Promise<WordLineChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const lineChartEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "lineChart")[0];
  if (!lineChartEl) throw new Error("lineChart element not found");

  // Grouping
  const groupingEl = lineChartEl.getElementsByTagNameNS(OOXML_NS.c, "grouping")[0];
  const grouping = (groupingEl?.getAttribute("val") ?? "standard") as WordLineChartModel["grouping"];

  // Series
  const series = parseAllSeries(lineChartEl);

  // Categories
  let categories = parseMultiLevelCategories(lineChartEl);
  if (!categories || categories.length === 0) {
    categories = parseCategories(lineChartEl);
  }

  // Axes
  const { catAxes, valAxes } = parseBarChartAxes(lineChartEl, plotArea!);
  const catAxis = catAxes[0];
  const valAxis = valAxes[0];

  // Legend & title
  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  // Data labels
  const chartLabels = extractChartDataLabels(lineChartEl);
  if (chartLabels) {
    for (const s of series) {
      if (s.showValueLabel === undefined) s.showValueLabel = chartLabels.showValueLabel;
      if (!s.dataLabelPosition) s.dataLabelPosition = chartLabels.dataLabelPosition;
    }
  }

  // Show marker
  const markerEl = lineChartEl.getElementsByTagNameNS(OOXML_NS.c, "marker")[0];
  const showMarker = markerEl ? markerEl.getElementsByTagNameNS(OOXML_NS.c, "symbol")[0]?.getAttribute("val") !== "none" : true;

  // Smooth
  const smoothEl = lineChartEl.getElementsByTagNameNS(OOXML_NS.c, "smooth")[0];
  const smooth = smoothEl?.getAttribute("val") === "1";

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "line", grouping, showMarker, smooth,
    categories, series, legend, widthPx, heightPx,
    title,
    xAxis: { title: catAxis?.title, numberFormat: catAxis?.numberFormat, reversed: catAxis?.reversed },
    yAxis: { title: valAxis?.title, min: valAxis?.min, max: valAxis?.max, majorUnit: valAxis?.majorUnit, numberFormat: valAxis?.numberFormat },
  };
}
