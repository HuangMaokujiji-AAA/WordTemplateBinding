import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordDoughnutChartModel } from "../types";
import { parseAllSeries } from "./chartSeriesParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartTitle } from "./barChartParser";

export async function parseDoughnutChart(context: ChartParseContext): Promise<WordDoughnutChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const doughnutEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "doughnutChart")[0];
  if (!doughnutEl) throw new Error("doughnutChart element not found");

  const series = parseAllSeries(doughnutEl);

  // Categories from first series
  const categories = parseDoughnutCategories(doughnutEl);

  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  // Hole size
  const holeSize = doughnutEl.getElementsByTagNameNS(OOXML_NS.c, "holeSize")[0];
  const holeSizeVal = holeSize ? parseInt(holeSize.getAttribute("val") ?? "50", 10) : 50;

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "doughnut", holeSize: holeSizeVal,
    categories, series, legend, widthPx, heightPx,
    title,
  };
}

function parseDoughnutCategories(doughnutEl: Element) {
  const firstSer = doughnutEl.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
  if (!firstSer) return [];
  const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];
  if (!cat) return [];
  const strRef = cat.getElementsByTagNameNS(OOXML_NS.c, "strRef")[0];
  if (strRef) {
    const strCache = strRef.getElementsByTagNameNS(OOXML_NS.c, "strCache")[0];
    if (strCache) {
      const pts = Array.from(strCache.getElementsByTagNameNS(OOXML_NS.c, "pt"))
        .sort((a, b) => parseInt(a.getAttribute("idx") ?? "0", 10) - parseInt(b.getAttribute("idx") ?? "0", 10));
      return pts.map((pt) => {
        const v = pt.getElementsByTagNameNS(OOXML_NS.c, "v")[0]?.textContent?.trim() ?? "";
        return { value: v, displayValue: v };
      });
    }
  }
  return [];
}
