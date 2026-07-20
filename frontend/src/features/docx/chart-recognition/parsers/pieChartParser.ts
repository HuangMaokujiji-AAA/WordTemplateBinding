import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartParseContext } from "../chartDetector";
import type { WordPieChartModel } from "../types";
import { parseAllSeries } from "./chartSeriesParser";
import { parseChartLegend } from "./chartLegendParser";
import { extractChartTitle } from "./barChartParser";

export async function parsePieChart(context: ChartParseContext): Promise<WordPieChartModel> {
  const { chartXml, chartId, relationshipId, chartXmlPath, widthPx, heightPx } = context;

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  const pieChartEl = plotArea?.getElementsByTagNameNS(OOXML_NS.c, "pieChart")[0];
  if (!pieChartEl) throw new Error("pieChart element not found");

  const series = parseAllSeries(pieChartEl);

  // Pie charts use the first series data points as category names
  // Categories come from the series' <c:cat> element
  const categories = parsePieCategories(pieChartEl);

  const legend = parseChartLegend(chartXml);
  const title = extractChartTitle(chartXml);

  // Explosion (first slice)
  const explosion = pieChartEl.getElementsByTagNameNS(OOXML_NS.c, "explosion")[0];
  const explosionVal = explosion ? parseInt(explosion.getAttribute("val") ?? "0", 10) : undefined;

  return {
    id: chartId, relationshipId, sourcePath: chartXmlPath,
    type: "pie", explosion: explosionVal,
    categories, series, legend, widthPx, heightPx,
    title,
  };
}

function parsePieCategories(pieChartEl: Element) {
  const firstSer = pieChartEl.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
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
