import { OOXML_NS } from "./namespaces";

/**
 * Parse an XML string into a DOM Document.
 */
export function parseXmlString(xmlString: string): Document {
  const parser = new DOMParser();
  const doc = parser.parseFromString(xmlString, "application/xml");

  const parseError = doc.querySelector("parsererror");
  if (parseError) {
    throw new Error(`XML parsing failed: ${parseError.textContent}`);
  }

  return doc;
}

/**
 * Get the text content of the first child element matching the given
 * namespace and local name, or undefined if not found.
 */
export function getChildTextContentNS(
  parent: Element,
  ns: string,
  localName: string
): string | undefined {
  const children = parent.getElementsByTagNameNS(ns, localName);
  if (children.length === 0) {
    return undefined;
  }
  return children[0].textContent?.trim() || undefined;
}

/**
 * Get all child elements matching the given namespace and local name.
 */
export function getChildElementsNS(
  parent: Element,
  ns: string,
  localName: string
): Element[] {
  return Array.from(parent.getElementsByTagNameNS(ns, localName));
}

/**
 * Get the value of an attribute on an element by local name.
 * Returns undefined if the attribute is not present.
 */
export function getAttr(element: Element, localName: string): string | undefined {
  const val = element.getAttribute(localName);
  return val !== null ? val : undefined;
}

/**
 * Get the value of an attribute on the first child element matching
 * the given namespace and local name, or undefined.
 */
export function getChildAttrNS(
  parent: Element,
  ns: string,
  localName: string,
  attrName: string
): string | undefined {
  const children = parent.getElementsByTagNameNS(ns, localName);
  if (children.length === 0) {
    return undefined;
  }
  return getAttr(children[0], attrName);
}

const CHART_NS = OOXML_NS.c;

/**
 * Get text content from <c:v> inside an element.
 */
export function getChartV(parent: Element): string | undefined {
  return getChildTextContentNS(parent, CHART_NS, "v");
}

/**
 * Get the val attribute from a direct child or descendant.
 */
export function getChartVal(parent: Element, localName: string): string | undefined {
  return getChildAttrNS(parent, CHART_NS, localName, "val");
}

/**
 * Get all <c:pt> elements sorted by their idx attribute.
 */
export function getSortedPtElements(parent: Element): Element[] {
  const pts = getChildElementsNS(parent, CHART_NS, "pt");
  return pts.sort((a, b) => {
    const idxA = parseInt(getAttr(a, "idx") ?? "0", 10);
    const idxB = parseInt(getAttr(b, "idx") ?? "0", 10);
    return idxA - idxB;
  });
}

/**
 * Get values from sorted <c:pt> elements, with null for missing indices.
 */
export function getSortedPtValues(parent: Element): Array<string | null> {
  const pts = getSortedPtElements(parent);
  if (pts.length === 0) return [];

  const maxIdx = Math.max(
    ...pts.map((p) => parseInt(getAttr(p, "idx") ?? "0", 10))
  );
  const result: Array<string | null> = new Array(maxIdx + 1).fill(null);

  for (const pt of pts) {
    const idx = parseInt(getAttr(pt, "idx") ?? "0", 10);
    const v = getChartV(pt);
    result[idx] = v ?? null;
  }

  return result;
}
