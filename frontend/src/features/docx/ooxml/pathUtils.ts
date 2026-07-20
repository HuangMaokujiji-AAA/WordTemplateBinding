/**
 * Resolve a relative target path within the DOCX ZIP structure.
 *
 * Relationship targets in .rels files are relative to the directory
 * containing the .rels file:
 *
 *   "charts/chart1.xml"              → baseDir + "/charts/chart1.xml"
 *   "../embeddings/test.xlsx"        → resolve ".." relative to baseDir
 *
 * @param baseDir - The directory containing the .rels file
 *   (e.g. "word" for document.xml.rels, "word/charts" for chart rels).
 * @param target - The Target attribute from a Relationship element.
 */
export function resolveZipPath(baseDir: string, target: string): string {
  // Remove any leading "./"
  const clean = target.replace(/^\.\//, "");

  const baseSegments = baseDir.split("/").filter(Boolean);
  const relSegments = clean.split("/");

  for (const seg of relSegments) {
    if (seg === "..") {
      baseSegments.pop();
    } else if (seg !== ".") {
      baseSegments.push(seg);
    }
  }

  return baseSegments.join("/");
}
