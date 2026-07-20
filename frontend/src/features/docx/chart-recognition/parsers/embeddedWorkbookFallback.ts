import type JSZip from "jszip";
import * as XLSX from "xlsx";

/**
 * Fallback parser that reads chart data from an embedded Excel workbook
 * when the chart cache data is missing or empty.
 *
 * Steps:
 *  1. Read word/charts/_rels/chartN.xml.rels to find the Excel relationship.
 *  2. Resolve the path (e.g. "../embeddings/Microsoft_Excel_Worksheet.xlsx"
 *     → "word/embeddings/Microsoft_Excel_Worksheet.xlsx").
 *  3. Use SheetJS to read the workbook and extract data by formula reference.
 *
 * Formula examples:
 *   Sheet1!$A$2:$A$5
 *   '工作表 1'!$B$2:$B$5
 *
 * Currently this is a fallback path — the target document has cache data
 * so this is kept as a clear interface for future use.
 */
export interface EmbeddedWorkbookData {
  values: Array<string | number | null>;
  sheetName: string;
  range: string;
}

/**
 * Parse an Excel formula reference like "Sheet1!$A$2:$A$5".
 * Returns { sheetName, startCol, startRow, endCol, endRow } or null.
 */
export function parseFormulaRef(formula: string): {
  sheetName: string;
  startCol: string;
  startRow: number;
  endCol: string;
  endRow: number;
} | null {
  // Match: 'Sheet Name'!$A$2:$A$5 or Sheet1!$A$2:$A$5
  const match = formula.match(
    /^(?:'([^']+)'|([^!]+))!\$([A-Z]+)\$(\d+)(?::\$([A-Z]+)\$(\d+))?$/
  );
  if (!match) return null;

  const sheetName = match[1] || match[2];
  const startCol = match[3];
  const startRow = parseInt(match[4], 10);
  const endCol = match[5] || startCol;
  const endRow = match[6] ? parseInt(match[6], 10) : startRow;

  return { sheetName, startCol, startRow, endCol, endRow };
}

/**
 * Read data from an embedded Excel workbook using a formula reference.
 */
export async function readEmbeddedWorkbook(
  zip: JSZip,
  xlsxPath: string,
  formula: string
): Promise<EmbeddedWorkbookData | null> {
  try {
    const file = zip.file(xlsxPath);
    if (!file) {
      console.warn(`Embedded workbook not found: ${xlsxPath}`);
      return null;
    }

    const data = await file.async("arraybuffer");
    const workbook = XLSX.read(new Uint8Array(data), { type: "array" });

    const ref = parseFormulaRef(formula);
    if (!ref) {
      console.warn(`Could not parse formula: ${formula}`);
      return null;
    }

    // Find the sheet (try both exact name and with trailing space)
    let sheet = workbook.Sheets[ref.sheetName];
    if (!sheet) {
      // Try finding by partial name
      const sheetNames = workbook.SheetNames;
      const foundName = sheetNames.find(
        (n) => n.trim() === ref.sheetName.trim()
      );
      if (foundName) {
        sheet = workbook.Sheets[foundName];
        ref.sheetName = foundName;
      }
    }

    if (!sheet) {
      console.warn(
        `Sheet "${ref.sheetName}" not found in workbook. Available: ${workbook.SheetNames.join(", ")}`
      );
      return null;
    }

    // Read the range
    const values: Array<string | number | null> = [];
    for (let row = ref.startRow; row <= ref.endRow; row++) {
      for (
        let col = colLetterToIndex(ref.startCol);
        col <= colLetterToIndex(ref.endCol);
        col++
      ) {
        const cellRef = XLSX.utils.encode_cell({ r: row - 1, c: col });
        const cell = sheet[cellRef];
        if (cell) {
          values.push(cell.v ?? null);
        } else {
          values.push(null);
        }
      }
    }

    return {
      values,
      sheetName: ref.sheetName,
      range: formula,
    };
  } catch (err) {
    console.warn(`Failed to read embedded workbook: ${err}`);
    return null;
  }
}

function colLetterToIndex(col: string): number {
  let result = 0;
  for (let i = 0; i < col.length; i++) {
    result = result * 26 + (col.charCodeAt(i) - 64);
  }
  return result - 1;
}
