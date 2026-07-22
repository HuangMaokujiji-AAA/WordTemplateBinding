import type JSZip from "jszip";
import { OOXML_NS } from "../../ooxml/namespaces";
import { parseXmlString } from "../../ooxml/xmlUtils";
import { readRelationships, resolveRelationshipTarget } from "../../ooxml/relationshipParser";

/**
 * Reads word/theme/themeN.xml and resolves the <a:clrScheme> into a
 * scheme-name → sRGB hex map (dk1, lt1, dk2, lt2, accent1..accent6,
 * hlink, folHlink). This is the piece the legacy chart-recognition
 * pipeline never implemented — schemeClr series colors fell back to
 * palette-by-index because theme1.xml was never read.
 *
 * Result is cached per zip instance + path since multiple charts in the
 * same document usually share one theme part.
 */
const themeCache = new WeakMap<JSZip, Map<string, Record<string, string>>>();

export async function readThemeColors(
  zip: JSZip,
  themePath: string | null
): Promise<Record<string, string>> {
  if (!themePath) return {};

  let perZipCache = themeCache.get(zip);
  if (!perZipCache) {
    perZipCache = new Map();
    themeCache.set(zip, perZipCache);
  }
  const cached = perZipCache.get(themePath);
  if (cached) return cached;

  const file = zip.file(themePath);
  if (!file) {
    perZipCache.set(themePath, {});
    return {};
  }

  try {
    const xmlString = await file.async("text");
    const doc = parseXmlString(xmlString);
    const clrScheme = doc.getElementsByTagNameNS(OOXML_NS.a, "clrScheme")[0];
    const result: Record<string, string> = {};
    if (clrScheme) {
      for (const child of Array.from(clrScheme.children)) {
        if (child.namespaceURI !== OOXML_NS.a) continue;
        const hex = extractHex(child);
        if (hex) result[child.localName] = hex;
      }
    }
    perZipCache.set(themePath, result);
    return result;
  } catch {
    perZipCache.set(themePath, {});
    return {};
  }
}

function extractHex(schemeColorEl: Element): string | null {
  const srgb = schemeColorEl.getElementsByTagNameNS(OOXML_NS.a, "srgbClr")[0];
  if (srgb) {
    const val = srgb.getAttribute("val");
    if (val) return `#${val.toUpperCase()}`;
  }
  const sysClr = schemeColorEl.getElementsByTagNameNS(OOXML_NS.a, "sysClr")[0];
  if (sysClr) {
    const lastClr = sysClr.getAttribute("lastClr");
    if (lastClr) return `#${lastClr.toUpperCase()}`;
  }
  return null;
}

/**
 * Locate the theme part referenced by word/_rels/document.xml.rels.
 * Returns null when no theme relationship exists (defensive — Word
 * documents normally always carry one).
 */
export async function findDocumentThemePath(zip: JSZip): Promise<string | null> {
  const rels = await readRelationships(zip, "word/_rels/document.xml.rels");
  const themeRel = rels.find((r) => r.type.endsWith("/theme"));
  if (!themeRel) return null;
  return resolveRelationshipTarget("word", themeRel.target);
}
