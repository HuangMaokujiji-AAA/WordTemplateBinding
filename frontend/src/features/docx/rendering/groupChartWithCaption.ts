import type { LocatedChartCaption } from "../ooxml/documentChartLocator";

const FIGURE_CLASS = "docx-chart-figure";

/**
 * Re-applies Word's keep-with-next relationship after docx-preview has split
 * the document into page sections. If the chart and caption landed on
 * different pages, the pair is placed on one page and wrapped as a unit.
 */
export function groupChartWithCaption(
  container: HTMLElement,
  chartElement: HTMLElement,
  caption: LocatedChartCaption | undefined
): HTMLElement | null {
  const chartParagraph = chartElement.closest("p");
  if (!chartParagraph || !caption || !container.contains(chartParagraph)) {
    return null;
  }

  const captionParagraph = findCaptionParagraph(
    container,
    chartParagraph,
    caption
  );

  chartParagraph.classList.add("docx-chart-paragraph");
  if (!captionParagraph) return null;

  captionParagraph.classList.add("docx-chart-caption");
  captionParagraph.dataset.chartCaptionPosition = caption.position;

  const chartArticle = chartParagraph.closest("article");
  const captionArticle = captionParagraph.closest("article");

  if (
    chartArticle !== captionArticle
    && chartParagraph.parentElement === chartArticle
    && captionParagraph.parentElement === captionArticle
  ) {
    if (caption.position === "after") {
      captionArticle?.insertBefore(chartParagraph, captionParagraph);
    } else {
      chartArticle?.insertBefore(captionParagraph, chartParagraph);
    }
  }

  const commonParent = chartParagraph.parentElement;
  if (!commonParent || captionParagraph.parentElement !== commonParent) {
    return null;
  }

  const existingFigure = chartParagraph.closest(`.${FIGURE_CLASS}`);
  if (existingFigure instanceof HTMLElement) return existingFigure;

  const figure = document.createElement("figure");
  figure.className = FIGURE_CLASS;
  figure.setAttribute("role", "group");
  figure.setAttribute("aria-label", caption.text);

  const firstElement = caption.position === "before"
    ? captionParagraph
    : chartParagraph;
  commonParent.insertBefore(figure, firstElement);

  if (caption.position === "before") {
    figure.append(captionParagraph, chartParagraph);
  } else {
    figure.append(chartParagraph, captionParagraph);
  }

  return figure;
}

function findCaptionParagraph(
  container: HTMLElement,
  chartParagraph: HTMLParagraphElement,
  caption: LocatedChartCaption
): HTMLParagraphElement | null {
  const paragraphs = Array.from(
    container.querySelectorAll<HTMLParagraphElement>("section.docx article p")
  ).filter((paragraph) => !paragraph.closest("header, footer"));
  const chartIndex = paragraphs.indexOf(chartParagraph);
  if (chartIndex < 0) return null;

  const expectedText = normalizeText(caption.text);
  const step = caption.position === "after" ? 1 : -1;

  for (
    let index = chartIndex + step;
    index >= 0 && index < paragraphs.length;
    index += step
  ) {
    const paragraph = paragraphs[index];
    if (paragraph.querySelector(".docx-chart-slot")) break;
    if (normalizeText(paragraph.textContent ?? "") === expectedText) {
      return paragraph;
    }
  }

  return null;
}

function normalizeText(value: string): string {
  return value.replace(/\s+/g, "").trim();
}
