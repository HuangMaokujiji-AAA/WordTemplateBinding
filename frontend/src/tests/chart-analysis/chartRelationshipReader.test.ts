import { describe, it, expect } from "vitest";
import JSZip from "jszip";
import { readChartRelationships } from "../../features/docx/chart-analysis/parsers/chartRelationshipReader";

const RELS_XML = (target: string, type: string, id = "rId4") => `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.microsoft.com/office/2011/relationships/chartStyle" Target="style1.xml"/>
  <Relationship Id="${id}" Type="${type}" Target="${target}"/>
</Relationships>`;

describe("readChartRelationships", () => {
  it("resolves the embedded workbook path via a /package relationship matched by r:id (real Word shape)", async () => {
    const zip = new JSZip();
    zip.file(
      "word/charts/_rels/chart2.xml.rels",
      RELS_XML("../embeddings/Microsoft_Excel_Worksheet1.xlsx", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package")
    );
    const info = await readChartRelationships(zip, "word/charts/chart2.xml", "rId4");
    expect(info.embeddedWorkbookPath).toBe("word/embeddings/Microsoft_Excel_Worksheet1.xlsx");
    expect(info.relsPath).toBe("word/charts/_rels/chart2.xml.rels");
  });

  it("falls back to scanning by target extension when externalDataRelationshipId is null", async () => {
    const zip = new JSZip();
    zip.file(
      "word/charts/_rels/chart3.xml.rels",
      RELS_XML("../embeddings/Microsoft_Excel_Worksheet2.xlsx", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject", "rId9")
    );
    const info = await readChartRelationships(zip, "word/charts/chart3.xml", null);
    expect(info.embeddedWorkbookPath).toBe("word/embeddings/Microsoft_Excel_Worksheet2.xlsx");
  });

  it("returns null embeddedWorkbookPath when the rels file does not exist", async () => {
    const zip = new JSZip();
    const info = await readChartRelationships(zip, "word/charts/chart1.xml", "rId4");
    expect(info.relsPath).toBeNull();
    expect(info.embeddedWorkbookPath).toBeNull();
  });

  it("returns null embeddedWorkbookPath when no relationship matches the given id or workbook-like target", async () => {
    const zip = new JSZip();
    zip.file(
      "word/charts/_rels/chart4.xml.rels",
      RELS_XML("style4.xml", "http://schemas.microsoft.com/office/2011/relationships/chartStyle", "rId1")
    );
    const info = await readChartRelationships(zip, "word/charts/chart4.xml", "rId4");
    expect(info.embeddedWorkbookPath).toBeNull();
  });
});
