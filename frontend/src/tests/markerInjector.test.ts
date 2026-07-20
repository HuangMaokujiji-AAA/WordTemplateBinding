import { describe, it, expect } from "vitest";
import JSZip from "jszip";
import { injectChartMarkers } from "../features/docx/ooxml/docxMarkerInjector";
import type { LocatedChart } from "../features/docx/ooxml/documentChartLocator";

const DOC_XML = `<?xml version="1.0" encoding="UTF-8"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
            xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
  <w:body>
    <w:p>
      <w:r>
        <w:t>Before chart text</w:t>
      </w:r>
      <w:r>
        <w:drawing>
          <wp:inline>
            <wp:extent cx="5715000" cy="3429000"/>
            <c:chart r:id="rId7"/>
          </wp:inline>
        </w:drawing>
      </w:r>
      <w:r>
        <w:t>After chart text</w:t>
      </w:r>
    </w:p>
  </w:body>
</w:document>`;

function makeTestChart(
  slotId: string,
  rId: string,
  path: string,
  order: number,
  marker: string
): LocatedChart {
  return {
    slotId,
    relationshipId: rId,
    chartPath: path,
    documentOrder: order,
    widthEmu: 5715000,
    heightEmu: 3429000,
    widthPx: 600,
    heightPx: 360,
    marker,
  };
}

describe("injectChartMarkers", () => {
  it("replaces chart drawing with a text marker", async () => {
    const zip = new JSZip();
    zip.file("word/document.xml", DOC_XML);

    const chart = makeTestChart(
      "chart-1-rId7",
      "rId7",
      "word/charts/chart1.xml",
      0,
      "[[DOCX_CHART_SLOT:chart-1-rId7]]"
    );

    const { modifiedXml, markerMap } = injectChartMarkers(zip, [chart], DOC_XML);

    // Marker should be in the XML
    expect(modifiedXml).toContain("[[DOCX_CHART_SLOT:chart-1-rId7]]");

    // The original chart element should be gone
    expect(modifiedXml).not.toContain("c:chart");

    // Before text should be preserved
    expect(modifiedXml).toContain("Before chart text");

    // After text should be preserved
    expect(modifiedXml).toContain("After chart text");

    // Marker map should have the entry
    expect(markerMap.has("[[DOCX_CHART_SLOT:chart-1-rId7]]")).toBe(true);
  });

  it("uses unique markers for multiple charts", () => {
    const charts: LocatedChart[] = [
      makeTestChart("chart-1-rId7", "rId7", "word/charts/chart1.xml", 0, "[[DOCX_CHART_SLOT:chart-1-rId7]]"),
      makeTestChart("chart-2-rId8", "rId8", "word/charts/chart2.xml", 1, "[[DOCX_CHART_SLOT:chart-2-rId8]]"),
    ];

    const markers = charts.map((c) => c.marker);
    const unique = new Set(markers);
    expect(unique.size).toBe(markers.length);
  });

  it("preserves EMU dimensions in the located chart", () => {
    const chart = makeTestChart(
      "test",
      "rId1",
      "word/charts/chart1.xml",
      0,
      "[[DOCX_CHART_SLOT:test]]"
    );

    expect(chart.widthEmu).toBe(5715000);
    expect(chart.heightEmu).toBe(3429000);
    expect(chart.widthPx).toBe(600);
    expect(chart.heightPx).toBe(360);
  });
});
