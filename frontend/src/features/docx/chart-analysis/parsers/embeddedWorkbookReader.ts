import type JSZip from "jszip";
import * as XLSX from "xlsx";

/**
 * Reads embedded XLSX workbooks (word/embeddings/*.xlsx) referenced by
 * chart formulas, for use as a supplementary/verification data source.
 *
 * Priority rule enforced by callers (chartXmlAnalyzer.ts), not here:
 *   1. chart XML cache (numCache/strCache) — always the preview value
 *   2. embedded workbook range — used only to fill gaps or produce a
 *      cache/workbook-mismatch diagnostic
 *   3. literal values
 *   4. missing
 *
 * This module never executes formulas and never fetches anything outside
 * the DOCX ZIP — it only reads a byte range already present in the
 * uploaded file.
 */

export interface ParsedFormulaRef {
  sheetName: string;
  startCol: string;
  startRow: number;
  endCol: string;
  endRow: number;
  isSingleCell: boolean;
}

const MAX_WORKBOOK_BYTES = 25 * 1024 * 1024; // 25MB guard against pathological embeds
const MAX_RANGE_CELLS = 20000; // guard against runaway ranges from malformed formulas

/**
 * Parse a formula reference such as:
 *   Sheet1!$A$2:$A$8
 *   'Data Sheet'!$B$2:$B$8
 *   Sheet1!$B$1
 *
 * Unsupported forms (named ranges, structured table refs, functions,
 * external workbook refs, dynamic arrays) return null — callers must
 * treat that as "unresolved", not as a parse failure.
 */
export function parseFormulaRef(formula: string): ParsedFormulaRef | null {
  const trimmed = formula.trim();
  const match = trimmed.match(
    /^(?:'([^']+)'|([A-Za-z0-9_.]+))!\$([A-Z]+)\$(\d+)(?::\$([A-Z]+)\$(\d+))?$/
  );
  if (!match) return null;

  const sheetName = match[1] ?? match[2];
  const startCol = match[3];
  const startRow = parseInt(match[4], 10);
  const endCol = match[5] ?? startCol;
  const endRow = match[6] ? parseInt(match[6], 10) : startRow;

  return {
    sheetName,
    startCol,
    startRow,
    endCol,
    endRow,
    isSingleCell: !match[5],
  };
}

export interface EmbeddedWorkbookRangeResult {
  values: Array<string | number | null>;
  resolvedSheetName: string;
}

export interface WorkbookHandle {
  workbook: XLSX.WorkBook;
}

const workbookCache = new WeakMap<JSZip, Map<string, WorkbookHandle | null>>();

/**
 * Loads (and caches, per zip + path) the embedded workbook so that
 * multiple charts sharing the same embedding only pay the parse cost once.
 */
export async function loadEmbeddedWorkbook(
  zip: JSZip,
  xlsxPath: string
): Promise<WorkbookHandle | null> {
  let perZipCache = workbookCache.get(zip);
  if (!perZipCache) {
    perZipCache = new Map();
    workbookCache.set(zip, perZipCache);
  }
  if (perZipCache.has(xlsxPath)) {
    return perZipCache.get(xlsxPath) ?? null;
  }

  const normalized = normalizeZipEntryPath(xlsxPath);
  if (!normalized) {
    perZipCache.set(xlsxPath, null);
    return null;
  }

  const file = zip.file(normalized);
  if (!file) {
    perZipCache.set(xlsxPath, null);
    return null;
  }

  try {
    const data = await file.async("arraybuffer");
    if (data.byteLength > MAX_WORKBOOK_BYTES) {
      perZipCache.set(xlsxPath, null);
      return null;
    }
    const workbook = XLSX.read(new Uint8Array(data), { type: "array" });
    const handle: WorkbookHandle = { workbook };
    perZipCache.set(xlsxPath, handle);
    return handle;
  } catch {
    perZipCache.set(xlsxPath, null);
    return null;
  }
}

/** Rejects ZIP path traversal (".." segments) and absolute paths outside the package. */
function normalizeZipEntryPath(path: string): string | null {
  const cleaned = path.replace(/\\/g, "/").replace(/^\/+/, "");
  const segments = cleaned.split("/");
  if (segments.some((s) => s === "..")) return null;
  return cleaned;
}

/**
 * Read a cell range from an already-loaded workbook using a parsed
 * formula reference. Returns null when the sheet cannot be resolved.
 */
export function readWorkbookRange(
  handle: WorkbookHandle,
  ref: ParsedFormulaRef
): EmbeddedWorkbookRangeResult | null {
  const { workbook } = handle;
  let sheetName = ref.sheetName;
  let sheet = workbook.Sheets[sheetName];

  if (!sheet) {
    const found = workbook.SheetNames.find((n) => n.trim() === sheetName.trim());
    if (found) {
      sheet = workbook.Sheets[found];
      sheetName = found;
    }
  }

  if (!sheet) return null;

  const startColIdx = colLetterToIndex(ref.startCol);
  const endColIdx = colLetterToIndex(ref.endCol);
  const cellCount = (ref.endRow - ref.startRow + 1) * (endColIdx - startColIdx + 1);
  if (cellCount <= 0 || cellCount > MAX_RANGE_CELLS) return null;

  const values: Array<string | number | null> = [];
  for (let row = ref.startRow; row <= ref.endRow; row++) {
    for (let col = startColIdx; col <= endColIdx; col++) {
      const cellRef = XLSX.utils.encode_cell({ r: row - 1, c: col });
      const cell = sheet[cellRef];
      values.push(cell?.v ?? null);
    }
  }

  return { values, resolvedSheetName: sheetName };
}

function colLetterToIndex(col: string): number {
  let result = 0;
  for (let i = 0; i < col.length; i++) {
    result = result * 26 + (col.charCodeAt(i) - 64);
  }
  return result - 1;
}
