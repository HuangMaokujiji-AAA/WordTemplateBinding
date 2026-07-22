import type JSZip from "jszip";
import { readRelationships, resolveRelationshipTarget } from "../../ooxml/relationshipParser";

export interface ChartRelationshipInfo {
  /** Normalized rels part path, e.g. "word/charts/_rels/chart1.xml.rels", or null if absent. */
  relsPath: string | null;
  /** Relationship id of the externalData (embedded workbook) link, if present. */
  externalDataRelationshipId: string | null;
  /** Normalized ZIP path of the embedded workbook, e.g. "word/embeddings/Microsoft_Excel_Worksheet1.xlsx". */
  embeddedWorkbookPath: string | null;
}

const EXTERNAL_DATA_REL_TYPE_SUFFIX = "/oleObject";
const PACKAGE_REL_TYPE_SUFFIX = "/package";

/**
 * Reads word/charts/_rels/chartN.xml.rels to find the embedded workbook
 * relationship. Chart parts typically reference their data source via
 * <c:externalData r:id="rIdN"/> in the chart XML itself; the actual
 * target (the .xlsx path) lives in the chart's own relationships part.
 */
export async function readChartRelationships(
  zip: JSZip,
  chartPartPath: string,
  externalDataRelationshipId: string | null
): Promise<ChartRelationshipInfo> {
  const lastSlash = chartPartPath.lastIndexOf("/");
  const dir = lastSlash >= 0 ? chartPartPath.slice(0, lastSlash) : "";
  const fileName = lastSlash >= 0 ? chartPartPath.slice(lastSlash + 1) : chartPartPath;
  const relsPath = dir ? `${dir}/_rels/${fileName}.rels` : `_rels/${fileName}.rels`;

  const relsFile = zip.file(relsPath);
  if (!relsFile) {
    return { relsPath: null, externalDataRelationshipId, embeddedWorkbookPath: null };
  }

  const rels = await readRelationships(zip, relsPath);

  let embeddedWorkbookPath: string | null = null;

  if (externalDataRelationshipId) {
    const rel = rels.find((r) => r.id === externalDataRelationshipId);
    if (rel) {
      embeddedWorkbookPath = resolveRelationshipTarget(dir, rel.target);
    }
  }

  if (!embeddedWorkbookPath) {
    // Fall back to scanning for any relationship pointing at a workbook-like
    // part — some producers omit r:id on <c:externalData> or use a package rel.
    const workbookRel = rels.find(
      (r) =>
        r.type.endsWith(EXTERNAL_DATA_REL_TYPE_SUFFIX) ||
        r.type.endsWith(PACKAGE_REL_TYPE_SUFFIX) ||
        /\.xlsx$/i.test(r.target)
    );
    if (workbookRel) {
      embeddedWorkbookPath = resolveRelationshipTarget(dir, workbookRel.target);
    }
  }

  return { relsPath, externalDataRelationshipId, embeddedWorkbookPath };
}
