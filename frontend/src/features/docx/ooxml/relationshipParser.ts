import type JSZip from "jszip";
import { OOXML_NS } from "./namespaces";
import { resolveZipPath } from "./pathUtils";

export interface ParsedRelationship {
  id: string;
  type: string;
  target: string;
  targetMode?: string;
}

/**
 * Parse a .rels XML file and return all relationships.
 * Relationships with TargetMode="External" are filtered out.
 */
export function parseRelationships(xmlString: string): ParsedRelationship[] {
  const parser = new DOMParser();
  const doc = parser.parseFromString(xmlString, "application/xml");

  const rels = doc.getElementsByTagNameNS(
    OOXML_NS.packageRelationships,
    "Relationship"
  );
  const results: ParsedRelationship[] = [];

  for (const rel of Array.from(rels)) {
    const targetMode = rel.getAttribute("TargetMode");
    // Skip external relationships
    if (targetMode === "External") {
      continue;
    }

    const id = rel.getAttribute("Id");
    const type = rel.getAttribute("Type");
    const target = rel.getAttribute("Target");

    if (id && type && target) {
      results.push({
        id,
        type,
        target,
        targetMode: targetMode ?? undefined,
      });
    }
  }

  return results;
}

/**
 * Resolve a relationship target to an absolute ZIP path.
 *
 * @param relsBasePath - The directory containing the .rels file
 *   (e.g. "word" for document.xml.rels).
 * @param target - The Target attribute from the Relationship element.
 */
export function resolveRelationshipTarget(
  relsBasePath: string,
  target: string
): string {
  return resolveZipPath(relsBasePath, target);
}

/**
 * Read and parse a .rels file from a JSZip instance.
 */
export async function readRelationships(
  zip: JSZip,
  relsPath: string
): Promise<ParsedRelationship[]> {
  const file = zip.file(relsPath);
  if (!file) {
    return [];
  }
  const content = await file.async("text");
  return parseRelationships(content);
}
