import { describe, it, expect } from "vitest";
import { parseXmlString } from "../../features/docx/ooxml/xmlUtils";
import { OOXML_NS } from "../../features/docx/ooxml/namespaces";
import { parseCategoryContainer } from "../../features/docx/chart-analysis/parsers/categoryAnalyzer";

function parseFragment(xml: string): Element {
  const wrapped = `<c:root xmlns:c="${OOXML_NS.c}">${xml}</c:root>`;
  const doc = parseXmlString(wrapped);
  return doc.documentElement.firstElementChild!;
}

describe("parseCategoryContainer", () => {
  it("parses strRef + strCache", () => {
    const cat = parseFragment(`
      <c:cat><c:strRef>
        <c:f>Sheet1!$A$2:$A$4</c:f>
        <c:strCache>
          <c:ptCount val="2"/>
          <c:pt idx="0"><c:v>四年级</c:v></c:pt>
          <c:pt idx="1"><c:v>八年级</c:v></c:pt>
        </c:strCache>
      </c:strRef></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories.map((c) => c.value)).toEqual(["四年级", "八年级"]);
    expect(categories[0].sourceFormula).toBe("Sheet1!$A$2:$A$4");
    expect(categories[0].valueType).toBe("string");
  });

  it("parses numRef + numCache as number type", () => {
    const cat = parseFragment(`
      <c:cat><c:numRef><c:numCache>
        <c:formatCode>General</c:formatCode>
        <c:ptCount val="3"/>
        <c:pt idx="0"><c:v>2020</c:v></c:pt>
        <c:pt idx="1"><c:v>2021</c:v></c:pt>
        <c:pt idx="2"><c:v>2022</c:v></c:pt>
      </c:numCache></c:numRef></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories.map((c) => c.value)).toEqual([2020, 2021, 2022]);
    expect(categories[0].valueType).toBe("number");
  });

  it("infers date type from a date-like numFmt format code", () => {
    const cat = parseFragment(`
      <c:cat><c:numRef><c:numCache>
        <c:formatCode>yyyy/m/d</c:formatCode>
        <c:ptCount val="1"/>
        <c:pt idx="0"><c:v>44562</c:v></c:pt>
      </c:numCache></c:numRef></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories[0].valueType).toBe("date");
  });

  it("parses strLit (literal, no formula)", () => {
    const cat = parseFragment(`
      <c:cat><c:strLit>
        <c:ptCount val="2"/>
        <c:pt idx="0"><c:v>A</c:v></c:pt>
        <c:pt idx="1"><c:v>B</c:v></c:pt>
      </c:strLit></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories.map((c) => c.value)).toEqual(["A", "B"]);
    expect(categories[0].sourceFormula).toBeNull();
  });

  it("parses numLit", () => {
    const cat = parseFragment(`
      <c:cat><c:numLit>
        <c:ptCount val="2"/>
        <c:pt idx="0"><c:v>1</c:v></c:pt>
        <c:pt idx="1"><c:v>2</c:v></c:pt>
      </c:numLit></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories.map((c) => c.value)).toEqual([1, 2]);
  });

  it("parses multi-level categories with forward-fill (fixture matches real Word output: first <c:lvl> is innermost/densest)", () => {
    // Structure verified against a real Word-generated chart2.xml: lvl[0]
    // (doc order) is the region column (changes every row), lvl[1] is the
    // grade column (sparse, forward-filled — spans multiple rows).
    const cat = parseFragment(`
      <c:cat><c:multiLvlStrRef>
        <c:f>Sheet1!$A$2:$B$5</c:f>
        <c:multiLvlStrCache>
          <c:ptCount val="4"/>
          <c:lvl>
            <c:pt idx="0"><c:v>全省</c:v></c:pt>
            <c:pt idx="1"><c:v>你县</c:v></c:pt>
            <c:pt idx="2"><c:v>全省</c:v></c:pt>
            <c:pt idx="3"><c:v>你县</c:v></c:pt>
          </c:lvl>
          <c:lvl>
            <c:pt idx="0"><c:v>八年级</c:v></c:pt>
            <c:pt idx="2"><c:v>四年级</c:v></c:pt>
          </c:lvl>
        </c:multiLvlStrCache>
      </c:multiLvlStrRef></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories).toHaveLength(4);
    // levels field is [innermost, ..., outermost].
    expect(categories[0].levels).toEqual(["全省", "八年级"]);
    expect(categories[1].levels).toEqual(["你县", "八年级"]);
    expect(categories[2].levels).toEqual(["全省", "四年级"]);
    expect(categories[3].levels).toEqual(["你县", "四年级"]);
    // value/displayValue present outermost first.
    expect(categories[0].value).toBe("八年级");
    expect(categories[0].displayValue).toBe("八年级 / 全省");
  });

  it("returns empty array for an empty ptCount", () => {
    const cat = parseFragment(`
      <c:cat><c:strRef><c:strCache>
        <c:ptCount val="0"/>
      </c:strCache></c:strRef></c:cat>`);
    expect(parseCategoryContainer(cat)).toEqual([]);
  });

  it("marks a category with no XML container as an empty result, not a throw", () => {
    expect(parseCategoryContainer(null)).toEqual([]);
  });

  it("gap-fills sparse category indices", () => {
    const cat = parseFragment(`
      <c:cat><c:strRef><c:strCache>
        <c:ptCount val="3"/>
        <c:pt idx="0"><c:v>A</c:v></c:pt>
        <c:pt idx="2"><c:v>C</c:v></c:pt>
      </c:strCache></c:strRef></c:cat>`);
    const categories = parseCategoryContainer(cat);
    expect(categories).toHaveLength(3);
    expect(categories[1].isMissing).toBe(true);
    expect(categories[0].value).toBe("A");
    expect(categories[2].value).toBe("C");
  });
});
