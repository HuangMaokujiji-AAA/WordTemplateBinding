import type JSZip from "jszip";
import { OOXML_NS } from "./namespaces";
import { parseXmlString } from "./xmlUtils";

export interface InjectedBlockBookmarks {
  modifiedXml: string;
  bookmarkIds: Record<string, string>;
  endBookmarkIds: Record<string, string>;
}

/**
 * Adds zero-content bookmarks to the start and end paragraphs of each
 * requested top-level body block. docx-preview renders bookmark starts as
 * zero-size spans, giving the structure editor stable block boundaries
 * without changing pagination or visible document content.
 */
export function injectBlockBookmarks(
  zip: JSZip,
  blockIds: string[],
  docXmlString: string
): InjectedBlockBookmarks {
  const doc = parseXmlString(docXmlString);
  const body = doc.getElementsByTagNameNS(OOXML_NS.w, "body")[0];
  if (!body) throw new Error("word/document.xml has no <w:body>");

  const bodyChildren = Array.from(body.children);
  const usedNames = new Set(
    Array.from(doc.getElementsByTagNameNS(OOXML_NS.w, "bookmarkStart"))
      .map((bookmark) => bookmark.getAttributeNS(OOXML_NS.w, "name"))
      .filter((name): name is string => !!name)
  );
  let nextBookmarkId =
    Math.max(
      0,
      ...Array.from(doc.getElementsByTagNameNS(OOXML_NS.w, "bookmarkStart"))
        .map((bookmark) => Number(bookmark.getAttributeNS(OOXML_NS.w, "id")))
        .filter(Number.isFinite)
    ) + 1;
  const bookmarkIds: Record<string, string> = {};
  const endBookmarkIds: Record<string, string> = {};

  blockIds.forEach((blockId, sequence) => {
    const match = /^body\/(\d+)$/.exec(blockId);
    if (!match) return;
    const block = bodyChildren[Number(match[1])];
    if (!block) return;

    const paragraphs = block.localName === "p"
      ? [block]
      : Array.from(block.getElementsByTagNameNS(OOXML_NS.w, "p"));
    const startParagraph = paragraphs[0];
    const endParagraph = paragraphs[paragraphs.length - 1];
    if (!startParagraph || !endParagraph) return;

    const bookmarkName = uniqueBookmarkName(
      `wtb_block_${sequence}_${match[1]}`,
      usedNames
    );
    const startPair = createBookmarkPair(
      doc,
      bookmarkName,
      String(nextBookmarkId++)
    );

    const firstContent = findStartInsertionPoint(startParagraph);
    startParagraph.insertBefore(startPair.start, firstContent);
    startParagraph.insertBefore(startPair.end, firstContent);
    bookmarkIds[blockId] = bookmarkName;
    usedNames.add(bookmarkName);

    const endBookmarkName = uniqueBookmarkName(
      `wtb_block_end_${sequence}_${match[1]}`,
      usedNames
    );
    const endPair = createBookmarkPair(
      doc,
      endBookmarkName,
      String(nextBookmarkId++)
    );
    endParagraph.append(endPair.start, endPair.end);
    endBookmarkIds[blockId] = endBookmarkName;
    usedNames.add(endBookmarkName);
  });

  const modifiedXml = new XMLSerializer().serializeToString(doc);
  zip.file("word/document.xml", modifiedXml);
  return { modifiedXml, bookmarkIds, endBookmarkIds };
}

function findStartInsertionPoint(paragraph: Element): Element | null {
  const children = Array.from(paragraph.children);
  let hasVisibleContent = false;

  for (const child of children) {
    if (child.localName === "pPr") continue;
    if (child.localName === "bookmarkStart" || child.localName === "bookmarkEnd") {
      continue;
    }

    if (child.localName !== "r") {
      return child;
    }

    const runChildren = Array.from(child.children);
    for (let index = 0; index < runChildren.length; index += 1) {
      const runChild = runChildren[index];
      if (runChild.localName === "rPr") continue;
      if (isPageBreak(runChild) && !hasVisibleContent) {
        const trailingContent = runChildren.slice(index + 1);
        if (trailingContent.length === 0) {
          return child.nextElementSibling;
        }

        const trailingRun = child.cloneNode(false) as Element;
        const runProperties = runChildren.find((item) => item.localName === "rPr");
        if (runProperties) trailingRun.append(runProperties.cloneNode(true));
        trailingContent.forEach((item) => trailingRun.append(item));
        paragraph.insertBefore(trailingRun, child.nextSibling);
        return trailingRun;
      }
      if (hasVisibleRunContent(runChild)) {
        hasVisibleContent = true;
        return child;
      }
    }
  }

  return null;
}

function isPageBreak(element: Element): boolean {
  if (element.namespaceURI !== OOXML_NS.w) return false;
  if (element.localName === "lastRenderedPageBreak") return true;
  if (element.localName !== "br") return false;
  const type = element.getAttributeNS(OOXML_NS.w, "type")
    ?? element.getAttribute("w:type")
    ?? element.getAttribute("type");
  return type === "page";
}

function hasVisibleRunContent(element: Element): boolean {
  if (element.namespaceURI !== OOXML_NS.w) return true;
  if (element.localName === "t") return !!element.textContent;
  return ![
    "rPr",
    "lastRenderedPageBreak",
    "bookmarkStart",
    "bookmarkEnd",
  ].includes(element.localName);
}

function createBookmarkPair(
  doc: Document,
  name: string,
  id: string
): { start: Element; end: Element } {
  const start = doc.createElementNS(OOXML_NS.w, "w:bookmarkStart");
  const end = doc.createElementNS(OOXML_NS.w, "w:bookmarkEnd");
  start.setAttributeNS(OOXML_NS.w, "w:id", id);
  start.setAttributeNS(OOXML_NS.w, "w:name", name);
  end.setAttributeNS(OOXML_NS.w, "w:id", id);
  return { start, end };
}

function uniqueBookmarkName(base: string, usedNames: Set<string>): string {
  const normalized = base.replace(/[^A-Za-z0-9_]/g, "_").slice(0, 36);
  let candidate = normalized;
  let suffix = 1;
  while (usedNames.has(candidate)) {
    candidate = `${normalized.slice(0, 35)}_${suffix++}`.slice(0, 40);
  }
  return candidate;
}
