/**
 * Resolve a relative path from a chart relationship to an absolute
 * path within the DOCX zip.
 *
 * Used for chart rels targets (e.g., embedding paths in
 * word/charts/_rels/chartN.xml.rels).
 *
 * Examples:
 *   baseDir="word/charts", "../embeddings/test.xlsx"  → "word/embeddings/test.xlsx"
 *   baseDir="word/charts", "../media/image1.png"      → "word/media/image1.png"
 */
export function resolveChartRelativePath(
  baseDir: string, // e.g. "word/charts"
  relativePath: string
): string {
  const segments = baseDir.split("/").filter(Boolean);
  const relParts = relativePath.split("/");

  for (const part of relParts) {
    if (part === "..") {
      segments.pop();
    } else if (part !== "." && part !== "") {
      segments.push(part);
    }
  }

  return segments.join("/");
}

/**
 * Given a relationship Target like "../embeddings/Microsoft_Excel_Worksheet.xlsx"
 * and a chart XML path like "word/charts/chart1.xml", return the normalized zip path.
 */
export function normalizeEmbeddingPath(
  chartXmlPath: string,
  target: string
): string {
  const baseDir = chartXmlPath.substring(0, chartXmlPath.lastIndexOf("/"));
  return resolveChartRelativePath(baseDir, target);
}
