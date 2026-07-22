import type { TemplateImportSummary } from "../../api/types";

/** Formats the backend-owned automatic binding result for the non-blocking status bar. */
export function formatImportSummary(summary: TemplateImportSummary): string {
  const restoredCount =
    summary.textBindingsRestored + summary.chartBindingsRestored;
  const restoredNote = restoredCount
    ? `，自动恢复 ${summary.textBindingsRestored} 个文本绑定和 ${summary.chartBindingsRestored} 个图表绑定`
    : "";
  const unresolvedNote = summary.unresolvedPlaceholders.length
    ? `，${summary.unresolvedPlaceholders.length} 个字段无法恢复：${summary.unresolvedPlaceholders.join("、")}`
    : "";
  const warningNote = summary.warnings.length
    ? `，恢复警告：${summary.warnings.join("；")}`
    : "";
  return `${restoredNote}${unresolvedNote}${warningNote}`;
}
