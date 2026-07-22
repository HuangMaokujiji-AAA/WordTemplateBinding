import { describe, it, expect } from "vitest";
import { parseXmlString } from "../../features/docx/ooxml/xmlUtils";
import { OOXML_NS } from "../../features/docx/ooxml/namespaces";
import { parseSeries } from "../../features/docx/chart-analysis/parsers/seriesAnalyzer";

function serFrom(xml: string): Element {
  const wrapped = `<c:root xmlns:c="${OOXML_NS.c}" xmlns:a="${OOXML_NS.a}">${xml}</c:root>`;
  const doc = parseXmlString(wrapped);
  return doc.documentElement.firstElementChild!;
}

describe("parseSeries", () => {
  it("reads the series name from tx/strRef/strCache", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache>
          <c:ptCount val="1"/><c:pt idx="0"><c:v>你县</c:v></c:pt>
        </c:strCache></c:strRef></c:tx>
        <c:val><c:numRef><c:numCache>
          <c:ptCount val="2"/>
          <c:pt idx="0"><c:v>1</c:v></c:pt>
          <c:pt idx="1"><c:v>2</c:v></c:pt>
        </c:numCache></c:numRef></c:val>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", ["1", "2"], {}, "pg0");
    expect(series.name).toBe("你县");
    expect(series.nameSource.formula).toBe("Sheet1!$B$1");
    expect(series.values.points.map((p) => p.value)).toEqual([1, 2]);
  });

  it("falls back to a generated name when no tx element exists", () => {
    const ser = serFrom(`<c:ser><c:idx val="0"/><c:order val="2"/></c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], {}, "pg0");
    expect(series.name).toBe("系列 3");
  });

  it("resolves an srgbClr series fill to a hex color", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], {}, "pg0");
    expect(series.style.fill?.color?.resolvedHex).toBe("#4472C4");
    expect(series.style.fill?.color?.sourceKind).toBe("srgb");
  });

  it("resolves a schemeClr series fill against the document theme colors", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:spPr><a:solidFill><a:schemeClr val="accent1"/></a:solidFill></c:spPr>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], { accent1: "#123456" }, "pg0");
    expect(series.style.fill?.color?.resolvedHex).toBe("#123456");
    expect(series.style.fill?.color?.sourceKind).toBe("scheme");
  });

  it("keeps a schemeClr reference unresolved (not silently dropped) when no theme color matches", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:spPr><a:solidFill><a:schemeClr val="accent9"/></a:solidFill></c:spPr>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], {}, "pg0");
    expect(series.style.fill?.color?.resolvedHex).toBeNull();
    expect(series.style.fill?.color?.raw).toBe("accent9");
  });

  it("parses series-level data labels (showVal, dLblPos)", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:dLbls><c:dLblPos val="outEnd"/><c:showVal val="1"/><c:showCatName val="0"/></c:dLbls>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], {}, "pg0");
    expect(series.dataLabels?.showValue).toBe(true);
    expect(series.dataLabels?.position).toBe("outEnd");
  });

  it("marks a series hidden when <c:hidden val=\"1\"/> is present", () => {
    const ser = serFrom(`<c:ser><c:idx val="0"/><c:order val="0"/><c:hidden val="1"/></c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "column", [], {}, "pg0");
    expect(series.hidden).toBe(true);
  });

  it("assigns axisIds from the caller (plot-group axIds), for later cross-referencing", () => {
    const ser = serFrom(`<c:ser><c:idx val="0"/><c:order val="0"/></c:ser>`);
    const series = parseSeries(ser, 0, "pg1", "line", ["10", "20"], {}, "pg1");
    expect(series.axisIds).toEqual(["10", "20"]);
    expect(series.plotGroupId).toBe("pg1");
  });

  it("parses scatter xVal/yVal into dedicated x/y value sources", () => {
    const ser = serFrom(`
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:xVal><c:numRef><c:numCache>
          <c:ptCount val="2"/><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt>
        </c:numCache></c:numRef></c:xVal>
        <c:yVal><c:numRef><c:numCache>
          <c:ptCount val="2"/><c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt>
        </c:numCache></c:numRef></c:yVal>
      </c:ser>`);
    const series = parseSeries(ser, 0, "pg0", "scatter", [], {}, "pg0");
    expect(series.xValues?.points.map((p) => p.value)).toEqual([1, 2]);
    expect(series.yValues?.points.map((p) => p.value)).toEqual([10, 20]);
    expect(series.values.points.map((p) => p.value)).toEqual([10, 20]);
  });
});
