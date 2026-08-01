import JSZip from "jszip";
import { describe, expect, it } from "vitest";
import { injectBlockBookmarks } from "../features/docx/ooxml/blockBookmarkInjector";
import { OOXML_NS } from "../features/docx/ooxml/namespaces";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";

const DOCUMENT_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="${OOXML_NS.w}">
  <w:body>
    <w:p><w:r><w:t>第一段</w:t></w:r></w:p>
    <w:tbl><w:tr><w:tc><w:p><w:r><w:t>表格</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
    <w:p><w:r><w:t>第三段</w:t></w:r></w:p>
    <w:sectPr />
  </w:body>
</w:document>`;

const PAGE_BREAK_DOCUMENT_XML = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="${OOXML_NS.w}">
  <w:body>
    <w:p><w:r><w:rPr><w:b /></w:rPr><w:lastRenderedPageBreak /><w:t>分页后的标题</w:t></w:r></w:p>
    <w:sectPr />
  </w:body>
</w:document>`;

describe("injectBlockBookmarks", () => {
  it("injects stable zero-content bookmarks into paragraphs and tables", () => {
    const zip = new JSZip();
    zip.file("word/document.xml", DOCUMENT_XML);

    const result = injectBlockBookmarks(
      zip,
      ["body/0", "body/1", "body/2"],
      DOCUMENT_XML
    );
    const documentXml = parseXmlString(result.modifiedXml);
    const bookmarks = Array.from(
      documentXml.getElementsByTagNameNS(OOXML_NS.w, "bookmarkStart")
    );

    expect(Object.keys(result.bookmarkIds)).toEqual([
      "body/0",
      "body/1",
      "body/2",
    ]);
    expect(Object.keys(result.endBookmarkIds)).toEqual([
      "body/0",
      "body/1",
      "body/2",
    ]);
    expect(bookmarks).toHaveLength(6);
    expect(bookmarks.map((bookmark) => bookmark.parentElement?.localName)).toEqual([
      "p", "p", "p", "p", "p", "p",
    ]);
    expect(new Set(Object.values(result.bookmarkIds)).size).toBe(3);
    expect(new Set(Object.values(result.endBookmarkIds)).size).toBe(3);
  });

  it("ignores invalid or non-renderable body paths", () => {
    const zip = new JSZip();
    zip.file("word/document.xml", DOCUMENT_XML);

    const result = injectBlockBookmarks(
      zip,
      ["body/99", "body/3", "body/0/content/0"],
      DOCUMENT_XML
    );

    expect(result.bookmarkIds).toEqual({});
    expect(result.endBookmarkIds).toEqual({});
  });

  it("places the start bookmark after a leading rendered page break", () => {
    const zip = new JSZip();
    zip.file("word/document.xml", PAGE_BREAK_DOCUMENT_XML);

    const result = injectBlockBookmarks(
      zip,
      ["body/0"],
      PAGE_BREAK_DOCUMENT_XML
    );
    const documentXml = parseXmlString(result.modifiedXml);
    const paragraph = documentXml.getElementsByTagNameNS(OOXML_NS.w, "p")[0];
    const children = Array.from(paragraph.children);
    const startIndex = children.findIndex(
      (child) =>
        child.localName === "bookmarkStart"
        && child.getAttributeNS(OOXML_NS.w, "name") === result.bookmarkIds["body/0"]
    );

    expect(children.map((child) => child.localName)).toEqual([
      "r",
      "bookmarkStart",
      "bookmarkEnd",
      "r",
      "bookmarkStart",
      "bookmarkEnd",
    ]);
    expect(children[0].getElementsByTagNameNS(OOXML_NS.w, "lastRenderedPageBreak"))
      .toHaveLength(1);
    expect(children[startIndex + 2].textContent).toBe("分页后的标题");
  });
});
