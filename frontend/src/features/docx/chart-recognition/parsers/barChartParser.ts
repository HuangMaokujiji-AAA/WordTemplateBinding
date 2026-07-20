import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordBarChartModel } from "../types";
import { detectBarChart } from "../detectors/barChartDetector";
import { parseAllSeries } from "./chartSeriesParser";
import { parseCategories } from "./chartCategoryParser";
import { parseMultiLevelCategories } from "./multiLevelCategoryParser";
import { parseBarChartAxes } from "./chartAxisParser";
import { parseChartLegend } from "./chartLegendParser";
import { parseChartStyle } from "./chartStyleParser";
import { extractChartDataLabels } from "./chartDataLabelParser";

/**
 * Parse a bar/column chart XML into a WordBarChartModel.
 *
 * This is the main entry point for parsing 2D bar/column charts.
 * It orchestrates all sub-parsers (series, categories, axes, legend,
 * style, data labels) and assembles the unified model.
 */
export async function parseBarChart(
  context: ChartParseContext
): Promise<WordBarChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const detection = detectBarChart(chartXml);
  const barDir = detection.barDir;
  const grouping = detection.grouping as WordBarChartModel["grouping"];

  // Determine chart type
  const chartType = barDir === "bar" ? "bar" : "column";

  // Get the barChart element
  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const barChartEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "barChart")[0];
  if (!barChartEl) {
    throw new Error("barChart element not found in chart XML");
  }

  // Parse series
  const series = parseAllSeries(barChartEl);

  // Parse categories (try multi-level first, then simple)
  let categories = parseMultiLevelCategories(barChartEl);
  if (!categories || categories.length === 0) {
    categories = parseCategories(barChartEl);
  }

  // Parse axes
  const { catAxes, valAxes } = parseBarChartAxes(barChartEl, plotArea!);

  // For "col" (vertical) charts: cat axis → xAxis, val axis → yAxis
  // For "bar" (horizontal) charts: val axis → xAxis, cat axis → yAxis
  const catAxis = catAxes[0];
  const valAxis = valAxes[0];

  // Parse legend
  const legendInfo = parseChartLegend(chartXml);

  // Parse chart title
  const title = extractChartTitle(chartXml);

  // Parse chart-level data labels (apply to series that don't have their own)
  const chartLabels = extractChartDataLabels(barChartEl);
  if (chartLabels) {
    for (const s of series) {
      if (s.showValueLabel === undefined) {
        s.showValueLabel = chartLabels.showValueLabel;
        if (!s.dataLabelPosition) {
          s.dataLabelPosition = chartLabels.dataLabelPosition;
        }
      }
    }
  }

  // Parse style
  const styleInfo = parseChartStyle(barChartEl);

  // Build the model
  const model: WordBarChartModel = {
    id: chartId,
    relationshipId,
    sourcePath: chartXmlPath,
    type: chartType as "bar" | "column",
    grouping,
    barDirection: barDir,
    categories,
    series,
    legend: legendInfo,
    widthPx,
    heightPx,
    gapWidth: styleInfo.gapWidth,
    overlap: styleInfo.overlap,
  };

  // Set title only if autoTitleDeleted is not set
  if (title) {
    model.title = title;
  }

  // Map axes based on bar direction
  if (barDir === "col") {
    // Vertical: cat → xAxis, val → yAxis
    model.xAxis = {
      title: catAxis?.title,
      numberFormat: catAxis?.numberFormat,
      reversed: catAxis?.reversed,
    };
    model.yAxis = {
      title: valAxis?.title,
      min: valAxis?.min,
      max: valAxis?.max,
      majorUnit: valAxis?.majorUnit,
      numberFormat: valAxis?.numberFormat,
    };
  } else {
    // Horizontal: val → xAxis, cat → yAxis
    model.xAxis = {
      title: valAxis?.title,
      min: valAxis?.min,
      max: valAxis?.max,
      majorUnit: valAxis?.majorUnit,
      numberFormat: valAxis?.numberFormat,
    };
    model.yAxis = {
      title: catAxis?.title,
      numberFormat: catAxis?.numberFormat,
      reversed: catAxis?.reversed,
    };
  }

  return model;
}

/**
 * Extract the chart title from <c:chart><c:title>.
 * Respects <c:autoTitleDeleted val="1"/> — returns undefined when set.
 *
 * Shared by all chart type parsers.
 */
export function extractChartTitle(chartXml: Document): string | undefined {
  const chartEl = chartXml.getElementsByTagNameNS(OOXML_NS.c, "chart")[0];
  if (!chartEl) return undefined;

  const autoTitleDeleted = chartEl.getElementsByTagNameNS(OOXML_NS.c, "autoTitleDeleted")[0];
  if (autoTitleDeleted?.getAttribute("val") === "1") return undefined;

  const titleEl = chartEl.getElementsByTagNameNS(OOXML_NS.c, "title")[0];
  if (!titleEl) return undefined;

  const tx = titleEl.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
  if (!tx) return undefined;

  const rich = tx.getElementsByTagNameNS(OOXML_NS.c, "rich")[0];
  if (!rich) return undefined;

  const p = rich.getElementsByTagNameNS(OOXML_NS.a, "p")[0];
  if (!p) return undefined;

  const tElements = p.getElementsByTagNameNS(OOXML_NS.a, "t");
  return Array.from(tElements)
    .map((t) => t.textContent ?? "")
    .join("");
}
