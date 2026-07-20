import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordAreaChartModel } from "../types";
import { parseAllSeries } from "./chartSeriesParser";
import { parseCategories } from "./chartCategoryParser";
import { parseMultiLevelCategories } from "./multiLevelCategoryParser";
import { parseBarChartAxes } from "./chartAxisParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartDataLabels } from "./chartDataLabelParser";
import { extractChartTitle } from "./barChartParser";

export async function parseAreaChart(context: ChartParseContext): Promise<WordAreaChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const areaChartEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "areaChart")[0];
  if (!areaChartEl) throw new Error("areaChart element not found");

  const groupingEl = areaChartEl.getElementsByTagNameNS(OOXML_NS.c, "grouping")[0];
  const grouping = (groupingEl?.getAttribute("val") ?? "standard") as WordAreaChartModel["grouping"];

  const series = parseAllSeries(areaChartEl);

  let categories = parseMultiLevelCategories(areaChartEl);
  if (!categories || categories.length === 0) {
    categories = parseCategories(areaChartEl);
  }

  const { catAxes, valAxes } = parseBarChartAxes(areaChartEl, plotArea!);
  const catAxis = catAxes[0];
  const valAxis = valAxes[0];

  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  const chartLabels = extractChartDataLabels(areaChartEl);
  if (chartLabels) {
    for (const s of series) {
      if (s.showValueLabel === undefined) s.showValueLabel = chartLabels.showValueLabel;
      if (!s.dataLabelPosition) s.dataLabelPosition = chartLabels.dataLabelPosition;
    }
  }

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "area", grouping,
    categories, series, legend, widthPx, heightPx,
    title,
    xAxis: { title: catAxis?.title, numberFormat: catAxis?.numberFormat, reversed: catAxis?.reversed },
    yAxis: { title: valAxis?.title, min: valAxis?.min, max: valAxis?.max, majorUnit: valAxis?.majorUnit, numberFormat: valAxis?.numberFormat },
  };
}
