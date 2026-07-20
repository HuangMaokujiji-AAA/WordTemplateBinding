import type JSZip from "jszip";
import { OOXML_NS } from "./namespaces";
import { parseXmlString } from "./xmlUtils";
import type { LocatedChart } from "./documentChartLocator";

/**
 * Inject text markers into word/document.xml to replace chart drawings.
 *
 * This modifies the in-memory document.xml inside the JSZip instance.
 * The original file on disk is never touched.
 *
 * Strategy:
 *  - Find every <w:drawing> that contains a <c:chart>.
 *  - Get the enclosing <w:r> run element.
 *  - If the run contains ONLY the drawing, replace the run content
 *    with a text marker run.
 *  - If the run contains other content (text + drawing), insert a new
 *    marker run before the original run and remove only the drawing.
 */
export function injectChartMarkers(
  zip: JSZip,
  charts: LocatedChart[],
  docXmlString: string
): { modifiedXml: string; markerMap: Map<string, LocatedChart> } {
  const doc = parseXmlString(docXmlString);
  const markerMap = new Map<string, LocatedChart>();

  // Find all chart elements
  const chartElements = doc.getElementsByTagNameNS(OOXML_NS.c, "chart");
  const chartEls = Array.from(chartElements);

  // Create document fragment for new content
  const body = doc.getElementsByTagNameNS(OOXML_NS.w, "body")[0];
  if (!body) {
    throw new Error("word/document.xml has no <w:body>");
  }

  for (let i = 0; i < chartEls.length; i++) {
    const chartEl = chartEls[i];
    const chart = charts[i];
    if (!chart) continue;

    // Find the enclosing <w:drawing>
    const drawingEl = findAncestorByLocalName(chartEl, "drawing", [
      OOXML_NS.w,
    ]);
    if (!drawingEl) continue;

    // Find the enclosing <w:r> run
    const runEl = findAncestorByLocalName(drawingEl, "r", [OOXML_NS.w]);
    if (!runEl) continue;

    // Create the marker run
    const markerRun = doc.createElementNS(OOXML_NS.w, "w:r");
    const markerRunPr = doc.createElementNS(OOXML_NS.w, "w:rPr");
    const markerT = doc.createElementNS(OOXML_NS.w, "w:t");
    markerT.setAttribute("xml:space", "preserve");
    markerT.textContent = chart.marker;
    markerRun.appendChild(markerRunPr);
    markerRun.appendChild(markerT);

    // Check if the run contains only the drawing
    const runChildren = Array.from(runEl.childNodes);
    const nonDrawingChildren = runChildren.filter((child) => {
      if (child.nodeType !== Node.ELEMENT_NODE) return false;
      const el = child as Element;
      return !(
        el.namespaceURI === OOXML_NS.w && el.localName === "drawing"
      );
    });

    if (nonDrawingChildren.length === 0) {
      // Run ONLY contains the drawing — replace the entire run
      runEl.parentNode?.replaceChild(markerRun, runEl);
    } else {
      // Run contains other content — insert new marker run before,
      // then remove only the drawing element
      runEl.parentNode?.insertBefore(markerRun, runEl);
      drawingEl.parentNode?.removeChild(drawingEl);
    }

    markerMap.set(chart.marker, chart);
  }

  const serializer = new XMLSerializer();
  const modifiedXml = serializer.serializeToString(doc);

  // Update the in-memory ZIP
  zip.file("word/document.xml", modifiedXml);

  return { modifiedXml, markerMap };
}

/**
 * Find the nearest ancestor with the given local name, checking
 * against the provided namespaces.
 */
function findAncestorByLocalName(
  el: Element,
  localName: string,
  namespaces: string[]
): Element | null {
  let current: Element | null = el;
  while (current) {
    if (
      namespaces.includes(current.namespaceURI || "") &&
      current.localName === localName
    ) {
      return current;
    }
    current = current.parentElement;
  }
  return null;
}
