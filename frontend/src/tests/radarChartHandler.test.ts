import { describe, expect, it } from "vitest";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";
import { RadarChartHandler } from "../features/docx/chart-recognition/detectors/radarChartHandler";
import { findHandler, initChartRecognition } from "../features/docx/chart-recognition";

describe("RadarChartHandler", () => {
  it("claims radarChart before the unsupported fallback", () => {
    const xml = parseXmlString(`
      <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
        <c:chart><c:plotArea><c:radarChart><c:radarStyle val="standard"/></c:radarChart></c:plotArea></c:chart>
      </c:chartSpace>`);
    initChartRecognition();
    expect(RadarChartHandler.canHandle(xml)).toBe(true);
    expect(findHandler(xml)).toBe(RadarChartHandler);
  });
});
