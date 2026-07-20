import type JSZip from "jszip";
import { OOXML_NS } from "./namespaces";
import { parseXmlString } from "./xmlUtils";
import { readRelationships, resolveRelationshipTarget } from "./relationshipParser";

/**
 * Information about a located chart within the document.
 */
export interface LocatedChart {
  /** Unique slot identifier for this chart. */
  slotId: string;
  /** The relationship ID (e.g. "rId7"). */
  relationshipId: string;
  /** Path within the ZIP (e.g. "word/charts/chart1.xml"). */
  chartPath: string;
  /** Order of appearance in the document (0-based). */
  documentOrder: number;

  /** Width in EMU from the drawing extent. */
  widthEmu?: number;
  /** Height in EMU from the drawing extent. */
  heightEmu?: number;

  /** Width in pixels (at 96 DPI). */
  widthPx: number;
  /** Height in pixels (at 96 DPI). */
  heightPx: number;

  /** Unique text marker injected into the document XML. */
  marker: string;

  /** The parent <w:r> element that contained the chart drawing. */
  parentRunIndex?: number;
}

/**
 * Locate all Word native charts within document.xml.
 *
 * Charts are referenced via `<c:chart r:id="rId7"/>` inside `<w:drawing>`
 * elements. Each chart's relationship is resolved through
 * word/_rels/document.xml.rels to find the actual chartN.xml path.
 */
export async function locateDocumentCharts(
  zip: JSZip
): Promise<LocatedChart[]> {
  // Read document.xml
  const docXmlFile = zip.file("word/document.xml");
  if (!docXmlFile) {
    throw new Error("word/document.xml not found in DOCX");
  }

  const docXmlString = await docXmlFile.async("text");
  const doc = parseXmlString(docXmlString);

  // Read relationships
  const rels = await readRelationships(zip, "word/_rels/document.xml.rels");

  // Build a map of rId → target
  const relMap = new Map<string, string>();
  for (const rel of rels) {
    relMap.set(rel.id, rel.target);
  }

  // Find all chart references in document.xml
  const chartElements = doc.getElementsByTagNameNS(OOXML_NS.c, "chart");
  const charts: LocatedChart[] = [];

  for (let i = 0; i < chartElements.length; i++) {
    const chartEl = chartElements[i];
    const rId = chartEl.getAttribute("r:id");
    if (!rId) continue;

    const relTarget = relMap.get(rId);
    if (!relTarget) {
      console.warn(`Chart relationship ${rId} not found in document.xml.rels`);
      continue;
    }

    const chartPath = resolveRelationshipTarget("word", relTarget);

    // Find the enclosing <wp:inline> or <wp:anchor> to get extent (size)
    const drawingEl = findAncestor(chartEl, OOXML_NS.w, "drawing");
    let extentEl: Element | null = null;

    if (drawingEl) {
      // Try <wp:inline> first
      extentEl = findDescendant(
        drawingEl,
        OOXML_NS.wp,
        "inline"
      );
      if (!extentEl) {
        extentEl = findDescendant(
          drawingEl,
          OOXML_NS.wp,
          "anchor"
        );
      }
    }

    let widthEmu: number | undefined;
    let heightEmu: number | undefined;

    if (extentEl) {
      const extents = extentEl.getElementsByTagNameNS(OOXML_NS.wp, "extent");
      if (extents.length > 0) {
        const cx = extents[0].getAttribute("cx");
        const cy = extents[0].getAttribute("cy");
        if (cx) widthEmu = parseInt(cx, 10);
        if (cy) heightEmu = parseInt(cy, 10);
      }
    }

    // Convert EMU to pixels (at 96 DPI)
    const EMU_PER_PX = 9525;
    const widthPx = widthEmu ? Math.round(widthEmu / EMU_PER_PX) : 560;
    const heightPx = heightEmu ? Math.round(heightEmu / EMU_PER_PX) : 320;

    const slotId = `chart-${i + 1}-${rId}`;
    const marker = `[[DOCX_CHART_SLOT:${slotId}]]`;

    charts.push({
      slotId,
      relationshipId: rId,
      chartPath,
      documentOrder: i,
      widthEmu,
      heightEmu,
      widthPx,
      heightPx,
      marker,
    });
  }

  return charts;
}

/**
 * Find the nearest ancestor element with the given namespace and local name.
 */
function findAncestor(
  el: Element,
  ns: string,
  localName: string
): Element | null {
  let current: Element | null = el;
  while (current) {
    if (current.namespaceURI === ns && current.localName === localName) {
      return current;
    }
    current = current.parentElement;
  }
  return null;
}

/**
 * Find the first descendant with the given namespace and local name.
 */
function findDescendant(
  el: Element,
  ns: string,
  localName: string
): Element | null {
  const children = el.getElementsByTagNameNS(ns, localName);
  return children.length > 0 ? children[0] : null;
}
