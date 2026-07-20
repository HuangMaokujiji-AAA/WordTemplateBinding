import { OOXML_NS } from "../../ooxml/namespaces";

/**
 * Detect whether a chart XML document is a bar/column chart
 * (2D clustered, stacked, or percent-stacked).
 *
 * Recognition rules:
 *  - Presence of <c:barChart> → supported bar/column chart
 *  - Presence of <c:bar3DChart> → unsupported
 *  - Multiple different chart types in plotArea → combo, unsupported
 */
export function detectBarChart(xmlDoc: Document): {
  supported: boolean;
  barDir: "bar" | "col";
  grouping: string;
} {
  const plotArea = xmlDoc.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  if (!plotArea) {
    return { supported: false, barDir: "col", grouping: "clustered" };
  }

  // Collect direct children of plotArea that are chart type elements
  const chartTypes: string[] = [];
  for (const child of Array.from(plotArea.children)) {
    if (child.namespaceURI === OOXML_NS.c) {
      const localName = child.localName;
      // Recognized chart type elements
      if (
        localName === "barChart" ||
        localName === "bar3DChart" ||
        localName === "lineChart" ||
        localName === "pieChart" ||
        localName === "doughnutChart" ||
        localName === "areaChart" ||
        localName === "scatterChart" ||
        localName === "radarChart" ||
        localName === "surfaceChart" ||
        localName === "stockChart" ||
        localName === "line3DChart" ||
        localName === "pie3DChart" ||
        localName === "area3DChart" ||
        localName === "surface3DChart"
      ) {
        chartTypes.push(localName);
      }
    }
  }

  if (chartTypes.length === 0) {
    return { supported: false, barDir: "col", grouping: "clustered" };
  }

  // Only support single barChart (2D)
  if (chartTypes.length === 1 && chartTypes[0] === "barChart") {
    const barChartEl = plotArea.getElementsByTagNameNS(
      OOXML_NS.c,
      "barChart"
    )[0];
    const barDirEl = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "barDir")[0];
    const barDir = barDirEl?.getAttribute("val") === "bar" ? "bar" : "col";

    const groupingEl = barChartEl.getElementsByTagNameNS(
      OOXML_NS.c,
      "grouping"
    )[0];
    const grouping = groupingEl?.getAttribute("val") ?? "clustered";

    return { supported: true, barDir, grouping };
  }

  return { supported: false, barDir: "col", grouping: "clustered" };
}
