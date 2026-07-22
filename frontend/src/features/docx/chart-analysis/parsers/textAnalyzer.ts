import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTextContent, ChartTextParagraph, ChartTextRun } from "../models/types";

/**
 * Parses an <a:rich> (or any element containing <a:p> paragraphs of runs)
 * into full multi-run, multi-paragraph ChartTextContent — unlike the legacy
 * extractChartTitle/parseAxisInfo helpers, which only ever read the first
 * <a:p> and concatenated its <a:t> runs with no formatting.
 */
export function parseRichText(richEl: Element): ChartTextContent | null {
  const paragraphEls = Array.from(richEl.getElementsByTagNameNS(OOXML_NS.a, "p"));
  if (paragraphEls.length === 0) return null;

  const paragraphs: ChartTextParagraph[] = paragraphEls.map((p) => ({
    runs: parseRuns(p),
  }));

  const plainText = paragraphs
    .map((p) => p.runs.map((r) => r.text).join(""))
    .join("\n");

  if (!plainText) return null;

  return { plainText, paragraphs };
}

function parseRuns(paragraphEl: Element): ChartTextRun[] {
  const runEls = Array.from(paragraphEl.children).filter(
    (c) => c.namespaceURI === OOXML_NS.a && c.localName === "r"
  );

  if (runEls.length === 0) {
    // Some producers put <a:fld> (field code) or bare <a:t> without <a:r>.
    const bareT = Array.from(paragraphEl.getElementsByTagNameNS(OOXML_NS.a, "t"));
    return bareT.map((t) => ({ text: t.textContent ?? "" }));
  }

  return runEls.map((run) => {
    const t = run.getElementsByTagNameNS(OOXML_NS.a, "t")[0];
    const text = t?.textContent ?? "";
    const rPr = run.getElementsByTagNameNS(OOXML_NS.a, "rPr")[0];

    const runData: ChartTextRun = { text };
    if (!rPr) return runData;

    const b = rPr.getAttribute("b");
    if (b != null) runData.bold = b === "1";
    const i = rPr.getAttribute("i");
    if (i != null) runData.italic = i === "1";
    const sz = rPr.getAttribute("sz");
    if (sz != null) {
      const parsed = parseInt(sz, 10);
      if (Number.isFinite(parsed)) runData.fontSizePt = parsed / 100;
    }

    const solidFill = rPr.getElementsByTagNameNS(OOXML_NS.a, "solidFill")[0];
    const srgb = solidFill?.getElementsByTagNameNS(OOXML_NS.a, "srgbClr")[0];
    const val = srgb?.getAttribute("val");
    if (val) runData.color = `#${val.toUpperCase()}`;

    return runData;
  });
}

/** Parses <c:title><c:tx><c:rich> at the chart level, respecting autoTitleDeleted. */
export function parseChartTitle(chartXml: Document): { title: ChartTextContent | null; autoTitleDeleted: boolean } {
  const chartEl = chartXml.getElementsByTagNameNS(OOXML_NS.c, "chart")[0];
  if (!chartEl) return { title: null, autoTitleDeleted: false };

  const autoTitleDeletedEl = chartEl.getElementsByTagNameNS(OOXML_NS.c, "autoTitleDeleted")[0];
  const autoTitleDeleted = autoTitleDeletedEl?.getAttribute("val") === "1";
  if (autoTitleDeleted) return { title: null, autoTitleDeleted };

  const titleEl = chartEl.getElementsByTagNameNS(OOXML_NS.c, "title")[0];
  if (!titleEl) return { title: null, autoTitleDeleted };

  const tx = titleEl.getElementsByTagNameNS(OOXML_NS.c, "tx")[0];
  const rich = tx?.getElementsByTagNameNS(OOXML_NS.c, "rich")[0];
  if (!rich) return { title: null, autoTitleDeleted };

  return { title: parseRichText(rich), autoTitleDeleted };
}
