import type JSZip from "jszip";
import type { ChartCategoryDefinition, ParsedChartSeries } from "../models/types";
import { ChartDiagnosticsCollector } from "../diagnostics/diagnostics";
import {
  loadEmbeddedWorkbook,
  parseFormulaRef,
  readWorkbookRange,
  type WorkbookHandle,
} from "./embeddedWorkbookReader";

/**
 * Reconciles chart XML cache values against the embedded workbook.
 *
 * Priority (per the design brief): chart XML cache is always the value
 * used for preview. The workbook is read only to (a) fill points whose
 * cache is missing/empty, using the workbook value in place of the
 * missing cache entry's displayValue, and (b) detect and report a
 * cache/workbook mismatch as a diagnostic — the cached, already-parsed
 * point values themselves are never overwritten.
 */
export async function reconcileWithWorkbook(
  zip: JSZip,
  workbookPath: string,
  series: ParsedChartSeries[],
  categories: ChartCategoryDefinition[],
  diagnostics: ChartDiagnosticsCollector
): Promise<void> {
  const handle = await loadEmbeddedWorkbook(zip, workbookPath);
  if (!handle) {
    diagnostics.warn("embedded-workbook-unreadable", `无法读取嵌入工作簿：${workbookPath}`);
    return;
  }

  reconcileCategoryFormula(handle, categories, diagnostics);

  for (const s of series) {
    reconcileSeriesSource("value", s.index, handle, s.values.formula, s.values.points, diagnostics);
    if (s.xValues) reconcileSeriesSource("x-value", s.index, handle, s.xValues.formula, s.xValues.points, diagnostics);
    if (s.yValues) reconcileSeriesSource("y-value", s.index, handle, s.yValues.formula, s.yValues.points, diagnostics);
  }
}

function reconcileCategoryFormula(
  handle: WorkbookHandle,
  categories: ChartCategoryDefinition[],
  diagnostics: ChartDiagnosticsCollector
): void {
  const formula = categories.find((c) => c.sourceFormula)?.sourceFormula;
  if (!formula) return;

  const ref = parseFormulaRef(formula);
  if (!ref) {
    diagnostics.info("category-formula-unsupported", `分类公式暂不支持解析：${formula}`);
    return;
  }

  const range = readWorkbookRange(handle, ref);
  if (!range) {
    diagnostics.warn("category-range-unresolved", `分类公式引用的工作表/区域无法解析：${formula}`);
    return;
  }

  compareLengths("category", null, categories.length, range.values.length, diagnostics);
  fillMissingFromWorkbook(
    categories.map((c) => ({ index: c.index, isMissing: c.isMissing })),
    range.values,
    (idx, workbookValue) => {
      const cat = categories[idx];
      if (cat && cat.isMissing && workbookValue != null) {
        cat.value = workbookValue;
        cat.displayValue = String(workbookValue);
        cat.isMissing = false;
      }
    }
  );
}

function reconcileSeriesSource(
  kind: "value" | "x-value" | "y-value",
  seriesIndex: number,
  handle: WorkbookHandle,
  formula: string | null,
  points: Array<{ index: number; value: number | null; displayValue?: string | null; isMissing: boolean }>,
  diagnostics: ChartDiagnosticsCollector
): void {
  if (!formula) return;

  const ref = parseFormulaRef(formula);
  if (!ref) {
    diagnostics.info("formula-unsupported", `公式暂不支持解析：${formula}`, { seriesKey: String(seriesIndex) });
    return;
  }

  const range = readWorkbookRange(handle, ref);
  if (!range) {
    diagnostics.warn("range-unresolved", `公式引用的工作表/区域无法解析：${formula}`, {
      seriesKey: String(seriesIndex),
    });
    return;
  }

  compareLengths(kind, seriesIndex, points.length, range.values.length, diagnostics);
  fillMissingFromWorkbook(points, range.values, (idx, workbookValue) => {
    const point = points[idx];
    if (!point || !point.isMissing) return;
    const numeric = typeof workbookValue === "number" ? workbookValue : Number(workbookValue);
    if (Number.isFinite(numeric)) {
      point.value = numeric;
      point.displayValue = String(workbookValue);
      point.isMissing = false;
    }
  });
}

function compareLengths(
  kind: string,
  seriesIndex: number | null,
  cacheLength: number,
  workbookLength: number,
  diagnostics: ChartDiagnosticsCollector
): void {
  if (cacheLength !== workbookLength) {
    diagnostics.warn(
      "cache-workbook-length-mismatch",
      `缓存点数 (${cacheLength}) 与嵌入工作簿区域长度 (${workbookLength}) 不一致，${kind}`,
      seriesIndex != null ? { seriesKey: String(seriesIndex) } : undefined
    );
  }
}

function fillMissingFromWorkbook<T extends { index: number; isMissing: boolean }>(
  points: T[],
  workbookValues: Array<string | number | null>,
  apply: (idx: number, workbookValue: string | number | null) => void
): void {
  for (let i = 0; i < points.length && i < workbookValues.length; i++) {
    if (points[i].isMissing) {
      apply(i, workbookValues[i]);
    }
  }
}
