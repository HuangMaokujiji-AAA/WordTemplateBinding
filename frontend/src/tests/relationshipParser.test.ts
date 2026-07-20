import { describe, it, expect } from "vitest";
import { parseRelationships, resolveRelationshipTarget } from "../features/docx/ooxml/relationshipParser";

const SAMPLE_RELS_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="charts/chart1.xml"/>
  <Relationship Id="rId8" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
  <Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" Target="../embeddings/test.xlsx"/>
  <Relationship Id="rIdExt" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="http://example.com" TargetMode="External"/>
</Relationships>`;

describe("parseRelationships", () => {
  it("parses chart relationship: rId7 → charts/chart1.xml", () => {
    const rels = parseRelationships(SAMPLE_RELS_XML);
    const chart = rels.find((r) => r.id === "rId7");
    expect(chart).toBeDefined();
    expect(chart!.target).toBe("charts/chart1.xml");
    expect(chart!.type).toContain("chart");
  });

  it("filters out TargetMode=External relationships", () => {
    const rels = parseRelationships(SAMPLE_RELS_XML);
    const ext = rels.find((r) => r.id === "rIdExt");
    expect(ext).toBeUndefined();
  });

  it("parses relative path: ../embeddings/test.xlsx", () => {
    const rels = parseRelationships(SAMPLE_RELS_XML);
    const emb = rels.find((r) => r.id === "rId9");
    expect(emb).toBeDefined();
    expect(emb!.target).toBe("../embeddings/test.xlsx");
  });
});

describe("resolveRelationshipTarget", () => {
  it("resolves ../embeddings/test.xlsx from word → embeddings/test.xlsx", () => {
    const result = resolveRelationshipTarget("word", "../embeddings/test.xlsx");
    expect(result).toBe("embeddings/test.xlsx");
  });

  it("resolves charts/chart1.xml from word → word/charts/chart1.xml", () => {
    const result = resolveRelationshipTarget("word", "charts/chart1.xml");
    expect(result).toBe("word/charts/chart1.xml");
  });
});
