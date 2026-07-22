import { describe, it, expect } from "vitest";
import { parseXmlString } from "../../features/docx/ooxml/xmlUtils";
import { OOXML_NS } from "../../features/docx/ooxml/namespaces";
import { assignAxisRoles, parseAllAxes } from "../../features/docx/chart-analysis/parsers/axisAnalyzer";

function plotAreaFrom(xml: string): Element {
  const wrapped = `<c:chartSpace xmlns:c="${OOXML_NS.c}" xmlns:a="${OOXML_NS.a}"><c:chart><c:plotArea>${xml}</c:plotArea></c:chart></c:chartSpace>`;
  const doc = parseXmlString(wrapped);
  return doc.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
}

describe("parseAllAxes", () => {
  it("parses a category axis with position, tick marks and crossAx (real Word shape)", () => {
    const plotArea = plotAreaFrom(`
      <c:catAx>
        <c:axId val="298855864"/>
        <c:scaling><c:orientation val="minMax"/></c:scaling>
        <c:delete val="0"/>
        <c:axPos val="b"/>
        <c:numFmt formatCode="General" sourceLinked="1"/>
        <c:majorTickMark val="out"/>
        <c:minorTickMark val="none"/>
        <c:tickLblPos val="nextTo"/>
        <c:crossAx val="2099321248"/>
      </c:catAx>
    `);
    const axes = parseAllAxes(plotArea);
    expect(axes).toHaveLength(1);
    const ax = axes[0];
    expect(ax.type).toBe("category");
    expect(ax.position).toBe("bottom");
    expect(ax.majorTickMark).toBe("out");
    expect(ax.minorTickMark).toBe("none");
    expect(ax.tickLabelPosition).toBe("nextTo");
    expect(ax.crossAxisId).toBe("2099321248");
    expect(ax.sourceLinked).toBe(true);
    expect(ax.visible).toBe(true);
  });

  it("parses a value axis with explicit min/max, title and reversed orientation", () => {
    const plotArea = plotAreaFrom(`
      <c:valAx>
        <c:axId val="2099321248"/>
        <c:scaling><c:orientation val="maxMin"/><c:max val="600"/><c:min val="0"/></c:scaling>
        <c:delete val="0"/>
        <c:axPos val="l"/>
        <c:title><c:tx><c:rich><a:p><a:r><a:t>平均分</a:t></a:r></a:p></c:rich></c:tx></c:title>
        <c:majorUnit val="100"/>
        <c:crossAx val="298855864"/>
      </c:valAx>
    `);
    const [ax] = parseAllAxes(plotArea);
    expect(ax.type).toBe("value");
    expect(ax.min).toBe(0);
    expect(ax.max).toBe(600);
    expect(ax.majorUnit).toBe(100);
    expect(ax.reversed).toBe(true);
    expect(ax.title?.plainText).toBe("平均分");
  });

  it("marks a deleted axis as not visible", () => {
    const plotArea = plotAreaFrom(`
      <c:valAx><c:axId val="1"/><c:delete val="1"/></c:valAx>
    `);
    const [ax] = parseAllAxes(plotArea);
    expect(ax.visible).toBe(false);
    expect(ax.delete).toBe(true);
  });

  it("parses a date axis type", () => {
    const plotArea = plotAreaFrom(`<c:dateAx><c:axId val="1"/></c:dateAx>`);
    const [ax] = parseAllAxes(plotArea);
    expect(ax.type).toBe("date");
  });

  it("returns an empty array for a plotArea with no axes (e.g. a pie chart)", () => {
    const plotArea = plotAreaFrom(`<c:pieChart></c:pieChart>`);
    expect(parseAllAxes(plotArea)).toEqual([]);
  });

  it("parses multiple catAx/valAx pairs, not just the first", () => {
    const plotArea = plotAreaFrom(`
      <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
      <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
      <c:valAx><c:axId val="3"/><c:crossAx val="4"/></c:valAx>
      <c:catAx><c:axId val="4"/><c:crossAx val="3"/></c:catAx>
    `);
    const axes = parseAllAxes(plotArea);
    expect(axes).toHaveLength(4);
    expect(axes.map((a) => a.id)).toEqual(["1", "2", "3", "4"]);
  });
});

describe("assignAxisRoles", () => {
  it("assigns primary x/y to the first plot group's axes", () => {
    const plotArea = plotAreaFrom(`
      <c:catAx><c:axId val="1"/></c:catAx>
      <c:valAx><c:axId val="2"/></c:valAx>
    `);
    const axes = parseAllAxes(plotArea);
    assignAxisRoles(axes, [["1", "2"]]);
    expect(axes.find((a) => a.id === "1")?.role).toBe("x");
    expect(axes.find((a) => a.id === "2")?.role).toBe("y");
  });

  it("assigns secondary-y to a second plot group's value axis not shared with the primary group", () => {
    const plotArea = plotAreaFrom(`
      <c:catAx><c:axId val="1"/></c:catAx>
      <c:valAx><c:axId val="2"/></c:valAx>
      <c:valAx><c:axId val="3"/></c:valAx>
      <c:catAx><c:axId val="4"/></c:catAx>
    `);
    const axes = parseAllAxes(plotArea);
    // bar group uses axId 1/2 (primary); line group uses axId 4/3 (secondary)
    assignAxisRoles(axes, [["1", "2"], ["4", "3"]]);
    expect(axes.find((a) => a.id === "2")?.role).toBe("y");
    expect(axes.find((a) => a.id === "3")?.role).toBe("secondary-y");
  });

  it("does not mark a second group's category axis as secondary when it is category type reused", () => {
    const plotArea = plotAreaFrom(`
      <c:catAx><c:axId val="1"/></c:catAx>
      <c:valAx><c:axId val="2"/></c:valAx>
      <c:valAx><c:axId val="3"/></c:valAx>
    `);
    const axes = parseAllAxes(plotArea);
    // Both groups share the same category axis id "1" (typical combo chart).
    assignAxisRoles(axes, [["1", "2"], ["1", "3"]]);
    expect(axes.find((a) => a.id === "1")?.role).toBe("x");
    expect(axes.find((a) => a.id === "3")?.role).toBe("secondary-y");
  });
});
