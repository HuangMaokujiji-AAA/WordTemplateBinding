import type { TemplateImportSummary } from "../../api/types";

/** Formats the backend-owned automatic binding result for the non-blocking status bar. */
export function formatImportSummary(summary: TemplateImportSummary): string {
  const tableBindingsRestored = summary.tableBindingsRestored || 0;
  const restoredCount =
    summary.textBindingsRestored +
    summary.chartBindingsRestored +
    tableBindingsRestored;
  const restoredNote = restoredCount
    ? tableBindingsRestored > 0
      ? `，自动恢复 ${summary.textBindingsRestored} 个文本绑定、${summary.chartBindingsRestored} 个图表绑定和 ${tableBindingsRestored} 个表格绑定`
      : `，自动恢复 ${summary.textBindingsRestored} 个文本绑定和 ${summary.chartBindingsRestored} 个图表绑定`
    : "";
  const unresolvedNote = summary.unresolvedPlaceholders.length
    ? `，${summary.unresolvedPlaceholders.length} 个字段无法恢复：${summary.unresolvedPlaceholders.join("、")}`
    : "";
  const warningNote = summary.warnings.length
    ? `，恢复警告：${summary.warnings.join("；")}`
    : "";
  return `${restoredNote}${unresolvedNote}${warningNote}`;
}
