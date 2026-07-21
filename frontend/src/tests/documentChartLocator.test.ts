import { describe, expect, it } from "vitest";
import JSZip from "jszip";
import { locateDocumentCharts } from "../features/docx/ooxml/documentChartLocator";

const RELATIONSHIPS_XML = `<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="charts/chart1.xml"/>
</Relationships>`;

function createDocumentXml(body: string): string {
  return `<?xml version="1.0" encoding="UTF-8"?>
    <w:document
      xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
      xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
      xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
      <w:body>${body}</w:body>
    </w:document>`;
}

function chartParagraph(properties = ""): string {
  return `<w:p>
    ${properties}
    <w:r><w:drawing><wp:inline>
      <wp:extent cx="5715000" cy="3429000"/>
      <c:chart r:id="rId7"/>
    </wp:inline></w:drawing></w:r>
  </w:p>`;
}

async function locateOne(documentXml: string) {
  const zip = new JSZip();
  zip.file("word/document.xml", documentXml);
  zip.file("word/_rels/document.xml.rels", RELATIONSHIPS_XML);
  const charts = await locateDocumentCharts(zip);
  expect(charts).toHaveLength(1);
  return charts[0];
}

describe("locateDocumentCharts caption relationship", () => {
  it("recognizes a caption after a chart that keeps with the next paragraph", async () => {
    const chart = await locateOne(createDocumentXml(`
      ${chartParagraph("<w:pPr><w:keepNext/></w:pPr>")}
      <w:p><w:r><w:t>图1 学生成绩分布</w:t></w:r></w:p>
    `));

    expect(chart.caption).toEqual({
      position: "after",
      text: "图1 学生成绩分布",
    });
  });

  it("recognizes a caption before a chart when that paragraph keeps with next", async () => {
    const chart = await locateOne(createDocumentXml(`
      <w:p><w:pPr><w:keepNext/></w:pPr><w:r><w:t>图2 学业水平</w:t></w:r></w:p>
      ${chartParagraph()}
    `));

    expect(chart.caption).toEqual({
      position: "before",
      text: "图2 学业水平",
    });
  });

  it("falls back to a short centered caption when keepNext is absent", async () => {
    const chart = await locateOne(createDocumentXml(`
      ${chartParagraph()}
      <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>学生能力层级分数</w:t></w:r></w:p>
    `));

    expect(chart.caption).toEqual({
      position: "after",
      text: "学生能力层级分数",
    });
  });
});
