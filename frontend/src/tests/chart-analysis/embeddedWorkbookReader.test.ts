import { describe, it, expect } from "vitest";
import JSZip from "jszip";
import * as XLSX from "xlsx";
import {
  loadEmbeddedWorkbook,
  parseFormulaRef,
  readWorkbookRange,
} from "../../features/docx/chart-analysis/parsers/embeddedWorkbookReader";

function buildXlsxBuffer(sheetName: string, rows: Array<Array<string | number>>): ArrayBuffer {
  const worksheet = XLSX.utils.aoa_to_sheet(rows);
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, sheetName);
  const out = XLSX.write(workbook, { type: "array", bookType: "xlsx" });
  return out as ArrayBuffer;
}

async function zipWithWorkbook(path: string, sheetName: string, rows: Array<Array<string | number>>): Promise<JSZip> {
  const zip = new JSZip();
  zip.file(path, buildXlsxBuffer(sheetName, rows));
  return zip;
}

describe("parseFormulaRef", () => {
  it("parses a plain sheet name with a range", () => {
    const ref = parseFormulaRef("Sheet1!$A$2:$A$8");
    expect(ref).toMatchObject({ sheetName: "Sheet1", startCol: "A", startRow: 2, endCol: "A", endRow: 8 });
  });

  it("parses a quoted sheet name with spaces", () => {
    const ref = parseFormulaRef("'Data Sheet'!$B$2:$B$8");
    expect(ref?.sheetName).toBe("Data Sheet");
  });

  it("parses a single cell reference", () => {
    const ref = parseFormulaRef("Sheet1!$B$1");
    expect(ref).toMatchObject({ startCol: "B", startRow: 1, endCol: "B", endRow: 1, isSingleCell: true });
  });

  it("returns null for a named range (unsupported)", () => {
    expect(parseFormulaRef("MyNamedRange")).toBeNull();
  });

  it("returns null for a structured table reference (unsupported)", () => {
    expect(parseFormulaRef("Table1[Column1]")).toBeNull();
  });

  it("returns null for a function formula (unsupported)", () => {
    expect(parseFormulaRef("SUM(Sheet1!A1:A10)")).toBeNull();
  });
});

describe("loadEmbeddedWorkbook / readWorkbookRange", () => {
  it("reads a plain range from an embedded xlsx", async () => {
    const zip = await zipWithWorkbook("word/embeddings/wb1.xlsx", "Sheet1", [
      ["Category", "Value"],
      ["四年级", 543],
      ["八年级", 505],
    ]);
    const handle = await loadEmbeddedWorkbook(zip, "word/embeddings/wb1.xlsx");
    expect(handle).not.toBeNull();
    const ref = parseFormulaRef("Sheet1!$B$2:$B$3");
    const range = readWorkbookRange(handle!, ref!);
    expect(range?.values).toEqual([543, 505]);
  });

  it("resolves a quoted sheet name with trailing/leading space mismatch", async () => {
    const zip = await zipWithWorkbook("word/embeddings/wb2.xlsx", "数据表 ", [["A"], [1]]);
    const handle = await loadEmbeddedWorkbook(zip, "word/embeddings/wb2.xlsx");
    const ref = parseFormulaRef("'数据表'!$A$2");
    const range = readWorkbookRange(handle!, ref!);
    expect(range?.values).toEqual([1]);
    expect(range?.resolvedSheetName).toBe("数据表 ");
  });

  it("returns null when the workbook file is not present in the zip", async () => {
    const zip = new JSZip();
    const handle = await loadEmbeddedWorkbook(zip, "word/embeddings/missing.xlsx");
    expect(handle).toBeNull();
  });

  it("returns null when the sheet name cannot be resolved", async () => {
    const zip = await zipWithWorkbook("word/embeddings/wb3.xlsx", "Sheet1", [["A"], [1]]);
    const handle = await loadEmbeddedWorkbook(zip, "word/embeddings/wb3.xlsx");
    const ref = parseFormulaRef("NoSuchSheet!$A$1");
    const range = readWorkbookRange(handle!, ref!);
    expect(range).toBeNull();
  });

  it("rejects a ZIP path with traversal segments", async () => {
    const zip = new JSZip();
    zip.file("word/embeddings/wb.xlsx", buildXlsxBuffer("Sheet1", [[1]]));
    const handle = await loadEmbeddedWorkbook(zip, "word/embeddings/../../../etc/passwd");
    expect(handle).toBeNull();
  });

  it("caches the parsed workbook across repeated calls for the same zip+path", async () => {
    const zip = await zipWithWorkbook("word/embeddings/wb4.xlsx", "Sheet1", [["A"], [1]]);
    const first = await loadEmbeddedWorkbook(zip, "word/embeddings/wb4.xlsx");
    const second = await loadEmbeddedWorkbook(zip, "word/embeddings/wb4.xlsx");
    expect(first).toBe(second);
  });
});
