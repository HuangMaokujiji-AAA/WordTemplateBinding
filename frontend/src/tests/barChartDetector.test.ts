import { describe, it, expect } from "vitest";
import { detectBarChart } from "../features/docx/chart-recognition/detectors/barChartDetector";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";

function makeChartXml(plotAreaChildren: string): string {
  return `<?xml version="1.0" encoding="UTF-8"?>
<c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
  <c:chart>
    <c:plotArea>
      ${plotAreaChildren}
    </c:plotArea>
  </c:chart>
</c:chartSpace>`;
}

describe("detectBarChart", () => {
  it("detects barChart with barDir=col → column, clustered", () => {
    const xml = makeChartXml(`
      <c:barChart>
        <c:barDir val="col"/>
        <c:grouping val="clustered"/>
      </c:barChart>
    `);
    const doc = parseXmlString(xml);
    const result = detectBarChart(doc);
    expect(result.supported).toBe(true);
    expect(result.barDir).toBe("col");
    expect(result.grouping).toBe("clustered");
  });

  it("detects barChart with barDir=bar → bar, stacked", () => {
    const xml = makeChartXml(`
      <c:barChart>
        <c:barDir val="bar"/>
        <c:grouping val="stacked"/>
      </c:barChart>
    `);
    const doc = parseXmlString(xml);
    const result = detectBarChart(doc);
    expect(result.supported).toBe(true);
    expect(result.barDir).toBe("bar");
    expect(result.grouping).toBe("stacked");
  });

  it("rejects bar3DChart as unsupported", () => {
    const xml = makeChartXml(`
      <c:bar3DChart>
        <c:barDir val="col"/>
      </c:bar3DChart>
    `);
    const doc = parseXmlString(xml);
    const result = detectBarChart(doc);
    expect(result.supported).toBe(false);
  });

  it("rejects scatterChart as unsupported", () => {
    const xml = makeChartXml(`
      <c:scatterChart>
        <c:scatterStyle val="lineMarker"/>
      </c:scatterChart>
    `);
    const doc = parseXmlString(xml);
    const result = detectBarChart(doc);
    expect(result.supported).toBe(false);
  });

  it("rejects barChart + lineChart combo as unsupported", () => {
    const xml = makeChartXml(`
      <c:barChart>
        <c:barDir val="col"/>
      </c:barChart>
      <c:lineChart>
      </c:lineChart>
    `);
    const doc = parseXmlString(xml);
    const result = detectBarChart(doc);
    expect(result.supported).toBe(false);
  });
});
