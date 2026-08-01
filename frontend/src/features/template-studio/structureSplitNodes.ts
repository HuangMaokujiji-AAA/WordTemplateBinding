import type { TemplateOutlineBlock } from "../../api/types";

export interface StructurePreviewRange {
  startIndex: number;
  endIndex: number;
}

export interface PageEndSplitCandidate {
  splitIndex: number;
  page: HTMLElement;
  anchor: HTMLElement;
}

export const STRUCTURE_PREVIEW_HIDDEN_CLASS = "structure-preview-hidden";
export const STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS = "structure-preview-page-hidden";

const VISUAL_CONTENT_SELECTOR = [
  "img",
  "svg",
  "canvas",
  "video",
  "audio",
  "object",
  "embed",
  "iframe",
  "math",
  ".docx-chart-slot",
].join(",");

export function configureSplitNodeLane(
  host: HTMLElement,
  button: HTMLElement
): void {
  Object.assign(host.style, {
    position: "relative",
    minHeight: "28px",
    margin: "8px 0px",
    padding: "0px",
    overflow: "visible",
    pointerEvents: "none",
  });
  button.style.pointerEvents = "auto";
}

export function placeSplitNodeInOwnRow(
  host: HTMLElement,
  renderedElement: HTMLElement,
  edge: "before" | "after"
): boolean {
  const parent = renderedElement.parentElement;
  if (!parent) return false;
  if (edge === "before") parent.insertBefore(host, renderedElement);
  else parent.insertBefore(host, renderedElement.nextSibling);
  return true;
}

/**
 * Empty paragraphs remain part of the partition range, but do not get their
 * own selectable split line in the rendered Word preview.
 */
export function shouldShowSplitNode(
  block: TemplateOutlineBlock,
  renderedElement: HTMLElement
): boolean {
  if (block.blockType !== "PARAGRAPH") return true;
  if (hasRenderedVisualContent(renderedElement)) return true;

  return hasMeaningfulRenderedText(renderedElement);
}

export function hasRenderedVisualContent(element: HTMLElement): boolean {
  return element.matches(VISUAL_CONTENT_SELECTOR)
    || !!element.querySelector(VISUAL_CONTENT_SELECTOR);
}

export function hasMeaningfulRenderedText(element: HTMLElement): boolean {
  const meaningfulText = (element.textContent || "").replace(
    /[\s\u00a0\u200b-\u200d\ufeff]/gu,
    ""
  );
  return meaningfulText.length > 0;
}

export function findSafePageEndSplitCandidates(
  container: HTMLElement,
  startElements: Array<HTMLElement | null>,
  endElements: Array<HTMLElement | null>
): PageEndSplitCandidate[] {
  const pages = Array.from(container.querySelectorAll<HTMLElement>("section.docx"));
  const pageOrder = new Map(pages.map((page, index) => [page, index]));
  const usedPages = new Set<HTMLElement>();
  const candidates: PageEndSplitCandidate[] = [];

  for (let splitIndex = 1; splitIndex < startElements.length; splitIndex += 1) {
    const previousStart = startElements[splitIndex - 1];
    const previousEnd = endElements[splitIndex - 1];
    const nextStart = startElements[splitIndex];
    if (!previousStart || !previousEnd || !nextStart) continue;

    const previousStartPage = previousStart.closest<HTMLElement>("section.docx");
    const previousEndPage = previousEnd.closest<HTMLElement>("section.docx");
    const nextPage = nextStart.closest<HTMLElement>("section.docx");
    if (!previousStartPage || !previousEndPage || !nextPage) continue;
    const previousStartPageIndex = pageOrder.get(previousStartPage) ?? -1;
    const previousEndPageIndex = pageOrder.get(previousEndPage) ?? -1;
    const nextPageIndex = pageOrder.get(nextPage) ?? -1;
    if (nextPageIndex <= previousStartPageIndex) continue;

    // When the previous block has visible continuation content on the page
    // where the next block starts, their true boundary is inside that page,
    // not at the end of the preceding page.
    if (
      previousEndPageIndex === nextPageIndex
      && (
        hasMeaningfulRenderedText(previousEnd)
        || hasRenderedVisualContent(previousEnd)
      )
    ) {
      continue;
    }
    if (previousEndPageIndex > nextPageIndex) continue;

    const pageEnd = pages[nextPageIndex - 1];
    if (!pageEnd || usedPages.has(pageEnd)) continue;
    const previousContent = findNearestMeaningfulElement(
      endElements,
      splitIndex - 1,
      -1
    );
    const nextContent = findNearestMeaningfulElement(
      startElements,
      splitIndex,
      1
    );
    if (
      previousContent
      && nextContent
      && areContinuousVisualContent(previousContent, nextContent)
    ) {
      continue;
    }

    const article = pageEnd.querySelector<HTMLElement>("article");
    const anchor = article
      ? Array.from(article.children)
          .filter(
            (element): element is HTMLElement =>
              element instanceof HTMLElement
              && !element.classList.contains("structure-node-host")
          )
          .at(-1) || null
      : null;
    if (!anchor) continue;

    usedPages.add(pageEnd);
    candidates.push({ splitIndex, page: pageEnd, anchor });
  }

  return candidates;
}

function findNearestMeaningfulElement(
  elements: Array<HTMLElement | null>,
  startIndex: number,
  step: -1 | 1
): HTMLElement | null {
  for (
    let index = startIndex;
    index >= 0 && index < elements.length;
    index += step
  ) {
    const element = elements[index];
    if (
      element
      && (hasMeaningfulRenderedText(element) || hasRenderedVisualContent(element))
    ) {
      return element;
    }
  }
  return null;
}

function areContinuousVisualContent(
  previous: HTMLElement,
  next: HTMLElement
): boolean {
  const previousFigure = previous.closest(".docx-chart-figure");
  const nextFigure = next.closest(".docx-chart-figure");
  if (previousFigure && previousFigure === nextFigure) {
    return true;
  }

  // A chart figure already contains the caption relationship recognized from
  // Word. Text immediately outside that figure is ordinary document content,
  // not automatically part of the chart merely because it precedes it.
  if (previousFigure || nextFigure) return false;

  return (
    hasMeaningfulRenderedText(previous)
    && hasRenderedVisualContent(next)
    && isLikelyVisualCaption(previous)
  );
}

function isLikelyVisualCaption(element: HTMLElement): boolean {
  if (element.classList.contains("docx-chart-caption")) return true;
  const text = (element.textContent || "").replace(/\s+/g, "").trim();
  if (!text || text.length > 80) return false;

  const textAlign = getComputedStyle(element).textAlign;
  if (textAlign === "center") return true;

  return /^(?:[（(]\d+[）)]|图\s*\d*|表\s*\d*|figure|fig\.?|chart)/iu.test(text);
}

export function findRenderedBlockForMarker(
  container: HTMLElement,
  markerId: string
): HTMLElement | null {
  const marker = Array.from(container.querySelectorAll<HTMLElement>("[id]")).find(
    (element) => element.id === markerId
  );
  const article = marker?.closest<HTMLElement>(".docx article");
  if (!marker || !article) return null;

  // Chart captions and their charts are deliberately grouped in one figure
  // so they stay on the same page. Keep their split anchors on the individual
  // paragraphs, though, so a caption that precedes a chart gets one candidate
  // above its text and another between the text and chart.
  const chartFigure = marker.closest<HTMLElement>(".docx-chart-figure");
  if (chartFigure && article.contains(chartFigure)) {
    const figureParagraph = marker.closest<HTMLElement>("p");
    if (figureParagraph?.parentElement === chartFigure) {
      return figureParagraph;
    }
  }

  let renderedBlock: HTMLElement = marker;
  while (
    renderedBlock.parentElement &&
    renderedBlock.parentElement !== article
  ) {
    renderedBlock = renderedBlock.parentElement;
  }
  return renderedBlock.parentElement === article ? renderedBlock : null;
}

export function applyStructurePreviewRange(
  container: HTMLElement,
  topLevelElements: HTMLElement[],
  mappedElements: Array<HTMLElement | null>,
  range: StructurePreviewRange | null
): HTMLElement[] {
  container
    .querySelectorAll(`.${STRUCTURE_PREVIEW_HIDDEN_CLASS}`)
    .forEach((element) => element.classList.remove(STRUCTURE_PREVIEW_HIDDEN_CLASS));
  container
    .querySelectorAll(`.${STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS}`)
    .forEach((element) => element.classList.remove(STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS));

  if (!range) return mappedElements.filter((item): item is HTMLElement => !!item);

  const startIndex = Math.max(0, range.startIndex);
  const endIndex = Math.min(mappedElements.length - 1, range.endIndex);
  const visibleAnchors = mappedElements
    .slice(startIndex, endIndex + 1)
    .filter((item): item is HTMLElement => !!item);
  const visibleTopLevel = new Set(
    visibleAnchors
      .map(findArticleChild)
      .filter((item): item is HTMLElement => !!item)
  );

  topLevelElements.forEach((element) => {
    element.classList.toggle(
      STRUCTURE_PREVIEW_HIDDEN_CLASS,
      !visibleTopLevel.has(element)
    );
  });

  mappedElements.forEach((element, index) => {
    if (!element || (index >= startIndex && index <= endIndex)) return;
    const topLevel = findArticleChild(element);
    if (topLevel && visibleTopLevel.has(topLevel) && element !== topLevel) {
      element.classList.add(STRUCTURE_PREVIEW_HIDDEN_CLASS);
    }
  });

  const visiblePages = new Set(
    visibleAnchors
      .map((element) => element.closest<HTMLElement>("section.docx"))
      .filter((item): item is HTMLElement => !!item)
  );
  container.querySelectorAll<HTMLElement>("section.docx").forEach((page) => {
    page.classList.toggle(
      STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS,
      !visiblePages.has(page)
    );
  });

  return visibleAnchors;
}

function findArticleChild(element: HTMLElement): HTMLElement | null {
  const article = element.closest<HTMLElement>(".docx article");
  if (!article) return null;
  let current = element;
  while (current.parentElement && current.parentElement !== article) {
    current = current.parentElement;
  }
  return current.parentElement === article ? current : null;
}
