import { describe, it, expect } from "vitest";
import { parseXmlString } from "../../features/docx/ooxml/xmlUtils";
import { OOXML_NS } from "../../features/docx/ooxml/namespaces";
import { convertToNumber, parseValueSource } from "../../features/docx/chart-analysis/parsers/cacheParser";

function parseFragment(xml: string): Element {
  const wrapped = `<c:root xmlns:c="${OOXML_NS.c}">${xml}</c:root>`;
  const doc = parseXmlString(wrapped);
  return doc.documentElement;
}

describe("parseValueSource — sparse idx handling", () => {
  it("gap-fills a missing middle index (idx 0 and 2 present, 1 absent)", () => {
    const val = parseFragment(`
      <c:val>
        <c:numRef>
          <c:f>Sheet1!$A$1:$A$3</c:f>
          <c:numCache>
            <c:ptCount val="3"/>
            <c:pt idx="0"><c:v>10</c:v></c:pt>
            <c:pt idx="2"><c:v>30</c:v></c:pt>
          </c:numCache>
        </c:numRef>
      </c:val>`);

    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);

    expect(source.points).toHaveLength(3);
    expect(source.points[0]).toMatchObject({ index: 0, value: 10, isMissing: false });
    expect(source.points[1]).toMatchObject({ index: 1, value: null, isMissing: true });
    expect(source.points[2]).toMatchObject({ index: 2, value: 30, isMissing: false });
  });

  it("does not shift idx 2's value into position 1", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="3"/>
        <c:pt idx="0"><c:v>10</c:v></c:pt>
        <c:pt idx="2"><c:v>30</c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    const values = source.points.map((p) => p.value);
    expect(values).toEqual([10, null, 30]);
  });

  it("handles ptCount greater than actual node count", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="5"/>
        <c:pt idx="0"><c:v>1</c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.points).toHaveLength(5);
    expect(source.points[4]).toMatchObject({ isMissing: true });
  });

  it("treats empty <c:v/> as missing", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="1"/>
        <c:pt idx="0"><c:v></c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.points[0].isMissing).toBe(true);
  });

  it("treats a non-numeric cached string as missing rather than NaN", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="1"/>
        <c:pt idx="0"><c:v>abc</c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.points[0].value).toBeNull();
    expect(source.points[0].isMissing).toBe(true);
  });

  it("parses a real zero value as present, not missing", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="1"/>
        <c:pt idx="0"><c:v>0</c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.points[0]).toMatchObject({ value: 0, isMissing: false });
  });

  it("parses negative numbers and scientific notation", () => {
    const val = parseFragment(`
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="2"/>
        <c:pt idx="0"><c:v>-42.5</c:v></c:pt>
        <c:pt idx="1"><c:v>1.5E+3</c:v></c:pt>
      </c:numCache></c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.points[0].value).toBe(-42.5);
    expect(source.points[1].value).toBe(1500);
  });

  it("reads the formula from numRef", () => {
    const val = parseFragment(`
      <c:val><c:numRef>
        <c:f>Sheet1!$B$2:$B$4</c:f>
        <c:numCache><c:ptCount val="0"/></c:numCache>
      </c:numRef></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.formula).toBe("Sheet1!$B$2:$B$4");
    expect(source.sourceKind).toBe("reference");
  });

  it("falls back to numLit when numRef is absent (literal series)", () => {
    const val = parseFragment(`
      <c:val><c:numLit>
        <c:ptCount val="2"/>
        <c:pt idx="0"><c:v>5</c:v></c:pt>
        <c:pt idx="1"><c:v>6</c:v></c:pt>
      </c:numLit></c:val>`);
    const source = parseValueSource(val, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.sourceKind).toBe("literal");
    expect(source.formula).toBeNull();
    expect(source.points.map((p) => p.value)).toEqual([5, 6]);
  });

  it("returns 'missing' sourceKind when the container is null", () => {
    const source = parseValueSource(null, "numRef", "numCache", "numLit", convertToNumber);
    expect(source.sourceKind).toBe("missing");
    expect(source.points).toEqual([]);
  });
});
