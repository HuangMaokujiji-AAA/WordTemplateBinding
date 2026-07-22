import { describe, it, expect } from "vitest";
import JSZip from "jszip";
import { locateDocumentCharts } from "../features/docx/ooxml/documentChartLocator";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";
import { findHandler, initChartRecognition } from "../features/docx/chart-recognition/index";
import { detectBarChart } from "../features/docx/chart-recognition/detectors/barChartDetector";
import { parseCategoryContainer } from "../features/docx/chart-analysis/parsers/categoryAnalyzer";
import { readFileSync } from "fs";
import { resolve } from "path";
import { OOXML_NS } from "../features/docx/ooxml/namespaces";

/**
 * Integration test for the sample DOCX document.
 *
 * This test is ONLY run when the sample document exists.
 * It verifies chart detection, parsing, and the expected document structure
 * without hardcoding data in production code.
 */
async function loadSampleZip(): Promise<JSZip | null> {
  try {
    const samplePath = resolve(
      __dirname,
      "../../../public/samples/第一部分 科学监测结果.docx"
    );
    const buffer = readFileSync(samplePath);
    return await JSZip.loadAsync(buffer);
  } catch {
    // Sample not found — skip integration tests
    return null;
  }
}

describe("Sample Document Integration", () => {
  it("sample DOCX exists and can be loaded", async () => {
    const zip = await loadSampleZip();
    if (!zip) {
      console.warn("Sample DOCX not found — skipping integration test");
      return;
    }
    expect(zip.file("word/document.xml")).toBeDefined();
  });

  it("locates all 10 charts in the sample document", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    const charts = await locateDocumentCharts(zip);
    expect(charts).toHaveLength(10);
  });

  it("identifies 9 bar charts and 1 unsupported chart", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    initChartRecognition();
    const charts = await locateDocumentCharts(zip);

    let barChartCount = 0;
    let unsupportedCount = 0;

    for (const chart of charts) {
      const chartFile = zip.file(chart.chartPath);
      if (!chartFile) {
        throw new Error(`Chart file not found: ${chart.chartPath}`);
      }
      const chartXmlString = await chartFile.async("text");
      const chartXml = parseXmlString(chartXmlString);

      const detection = detectBarChart(chartXml);
      if (detection.supported) {
        barChartCount++;
      } else {
        // Check what type it is
        const handler = findHandler(chartXml);
        const model = await handler?.parse({
          chartXml,
          chartXmlPath: chart.chartPath,
          chartId: chart.slotId,
          relationshipId: chart.relationshipId,
          zip,
          widthPx: chart.widthPx,
          heightPx: chart.heightPx,
        });
        if (model?.type === "unsupported") {
          unsupportedCount++;
        }
      }
    }

    expect(barChartCount).toBe(9);
    expect(unsupportedCount).toBe(1);
  });

  it("chart3 is identified as scatterChart", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    initChartRecognition();
    const charts = await locateDocumentCharts(zip);

    // chart3 should have chartPath matching */chart3.xml
    const chart3 = charts.find((c) => c.chartPath.endsWith("chart3.xml"));
    expect(chart3).toBeDefined();

    const chartFile = zip.file(chart3!.chartPath);
    expect(chartFile).toBeDefined();

    const chartXmlString = await chartFile!.async("text");
    const chartXml = parseXmlString(chartXmlString);

    const detection = detectBarChart(chartXml);
    expect(detection.supported).toBe(false);

    // Verify it has scatterChart element
    const plotArea = chartXml.getElementsByTagNameNS(
      "http://schemas.openxmlformats.org/drawingml/2006/chart",
      "plotArea"
    )[0];
    const scatterChart = plotArea?.getElementsByTagNameNS(
      "http://schemas.openxmlformats.org/drawingml/2006/chart",
      "scatterChart"
    )[0];
    expect(scatterChart).toBeDefined();
  });

  it("chart1 has correct category and series structure", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    const charts = await locateDocumentCharts(zip);
    const chart1 = charts.find((c) => c.chartPath.endsWith("chart1.xml"));
    expect(chart1).toBeDefined();

    const chartFile = zip.file(chart1!.chartPath);
    const chartXmlString = await chartFile!.async("text");
    const chartXml = parseXmlString(chartXmlString);

    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS(OOXML_NS.c, "barChart")[0];

    // Parse series
    const serEls = Array.from(barChart.getElementsByTagNameNS(OOXML_NS.c, "ser"));
    expect(serEls.length).toBeGreaterThanOrEqual(2);

    // Parse categories
    const firstSer = serEls[0];
    const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];
    const categories = parseCategoryContainer(cat);
    expect(categories.map((c) => c.value)).toContain("四年级");
    expect(categories.map((c) => c.value)).toContain("八年级");
  });

  it("chart7 has 6 cognitive domain categories", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    const charts = await locateDocumentCharts(zip);
    const chart7 = charts.find((c) => c.chartPath.endsWith("chart7.xml"));
    expect(chart7).toBeDefined();

    const chartFile = zip.file(chart7!.chartPath);
    const chartXmlString = await chartFile!.async("text");
    const chartXml = parseXmlString(chartXmlString);

    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS(OOXML_NS.c, "barChart")[0];
    const firstSer = barChart.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
    const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];

    const categories = parseCategoryContainer(cat);
    const expectedDomains = ["识记", "理解", "应用", "分析", "评价", "创造"];
    const catValues = categories.map((c) => c.value);

    for (const domain of expectedDomains) {
      expect(catValues).toContain(domain);
    }
  });

  it("chart2 has multi-level categories", async () => {
    const zip = await loadSampleZip();
    if (!zip) return;

    const charts = await locateDocumentCharts(zip);
    const chart2 = charts.find((c) => c.chartPath.endsWith("chart2.xml"));
    expect(chart2).toBeDefined();

    const chartFile = zip.file(chart2!.chartPath);
    const chartXmlString = await chartFile!.async("text");
    const chartXml = parseXmlString(chartXmlString);

    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS(OOXML_NS.c, "barChart")[0];
    const firstSer = barChart.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
    const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];

    const multiCategories = parseCategoryContainer(cat);
    expect(multiCategories.length).toBeGreaterThan(0);
    expect(multiCategories.some((c) => c.levels.length > 0)).toBe(true);
  });
});
