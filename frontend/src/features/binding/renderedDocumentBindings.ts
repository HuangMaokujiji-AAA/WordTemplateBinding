import type { DataFieldNode, MockItem } from "../../api/types";

export const FIELD_MIME_TYPE = "application/x-word-template-field";

export interface BindingTargetHandlers {
  onSelect: (item: MockItem) => void;
  onBind: (locatorId: string, field: DataFieldNode) => void;
  onError: (message: string) => void;
}

export interface DecorationResult {
  renderedCount: number;
  unresolvedLocatorIds: string[];
}

interface TextBoundary {
  node: Text;
  offset: number;
}

/**
 * Maps the server's paragraph/character locators onto the docx-preview DOM.
 * Matching is deliberately based on paragraph text instead of renderer-specific
 * classes, so the backend remains the source of truth for replacement locators.
 */
export function decorateRenderedDocument(
  container: HTMLElement,
  mockItems: MockItem[],
  handlers: BindingTargetHandlers
): DecorationResult {
  const paragraphs = Array.from(container.querySelectorAll<HTMLElement>("p"));
  const byParagraph = new Map<number, MockItem[]>();

  for (const item of mockItems) {
    const group = byParagraph.get(item.locator.paragraphIndex) || [];
    group.push(item);
    byParagraph.set(item.locator.paragraphIndex, group);
  }

  let renderedCount = 0;
  let searchStart = 0;
  const unresolvedLocatorIds: string[] = [];

  for (const [, items] of [...byParagraph.entries()].sort(
    ([left], [right]) => left - right
  )) {
    const expectedText = items[0]?.paragraphText || "";
    const match = findParagraph(paragraphs, expectedText, searchStart);

    if (!match) {
      unresolvedLocatorIds.push(...items.map((item) => item.locatorId));
      continue;
    }

    searchStart = match.index + 1;
    const sortedItems = [...items].sort(
      (left, right) => right.locator.startOffset - left.locator.startOffset
    );

    for (const item of sortedItems) {
      const start = match.textOffset + item.locator.startOffset;
      const inserted = insertBindingTarget(
        match.paragraph,
        start,
        item.locator.length,
        item,
        handlers
      );

      if (inserted) {
        renderedCount += 1;
      } else {
        unresolvedLocatorIds.push(item.locatorId);
      }
    }
  }

  return { renderedCount, unresolvedLocatorIds };
}

export function refreshBindingTargetStates(
  container: HTMLElement,
  mockItems: MockItem[]
): void {
  const itemsByLocator = new Map(
    mockItems.map((item) => [item.locatorId, item])
  );

  for (const element of container.querySelectorAll<HTMLElement>(
    ".template-binding-target[data-locator-id]"
  )) {
    const locatorId = element.dataset.locatorId;
    const item = locatorId ? itemsByLocator.get(locatorId) : undefined;
    if (!item) continue;

    element.classList.toggle("is-bound", item.isBound);
    element.title = item.isBound
      ? `已绑定：${item.boundDataPath}`
      : "拖拽一个兼容字段到此处";
    element.setAttribute(
      "aria-label",
      item.isBound
        ? `模拟值 ${item.mockValue}，已绑定 ${item.boundDataPath}`
        : `模拟值 ${item.mockValue}，未绑定`
    );
  }
}

export function focusBindingTarget(
  container: HTMLElement,
  locatorId: string
): boolean {
  const target = Array.from(
    container.querySelectorAll<HTMLElement>(
      ".template-binding-target[data-locator-id]"
    )
  ).find((element) => element.dataset.locatorId === locatorId);

  if (!target) return false;
  target.scrollIntoView({ behavior: "smooth", block: "center" });
  target.focus({ preventScroll: true });
  return true;
}

function findParagraph(
  paragraphs: HTMLElement[],
  expectedText: string,
  searchStart: number
): { paragraph: HTMLElement; index: number; textOffset: number } | null {
  const normalizedExpected = normalizeText(expectedText);

  for (let index = searchStart; index < paragraphs.length; index += 1) {
    const paragraphText = getBindableText(paragraphs[index]);
    const textOffset = paragraphText.indexOf(normalizedExpected);
    if (textOffset >= 0) {
      return { paragraph: paragraphs[index], index, textOffset };
    }
  }

  // Repeated headers or renderer-specific nodes can disturb sequence matching.
  // A full-document fallback still requires an exact paragraph-text match.
  for (let index = 0; index < searchStart; index += 1) {
    const paragraphText = getBindableText(paragraphs[index]);
    const textOffset = paragraphText.indexOf(normalizedExpected);
    if (textOffset >= 0) {
      return { paragraph: paragraphs[index], index, textOffset };
    }
  }

  return null;
}

function insertBindingTarget(
  paragraph: HTMLElement,
  start: number,
  length: number,
  item: MockItem,
  handlers: BindingTargetHandlers
): boolean {
  const nodes = getBindableTextNodes(paragraph);
  const startBoundary = findBoundary(nodes, start, false);
  const endBoundary = findBoundary(nodes, start + length, true);
  if (!startBoundary || !endBoundary) return false;

  const range = document.createRange();
  range.setStart(startBoundary.node, startBoundary.offset);
  range.setEnd(endBoundary.node, endBoundary.offset);

  const target = document.createElement("span");
  target.className = "template-binding-target";
  target.dataset.locatorId = item.locatorId;
  target.tabIndex = 0;
  target.setAttribute("role", "button");
  target.append(range.extractContents());
  range.insertNode(target);

  target.addEventListener("click", () => handlers.onSelect(item));
  target.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      handlers.onSelect(item);
    }
  });
  target.addEventListener("dragover", (event) => {
    event.preventDefault();
    target.classList.add("is-drag-over");
  });
  target.addEventListener("dragleave", () => {
    target.classList.remove("is-drag-over");
  });
  target.addEventListener("drop", (event) => {
    event.preventDefault();
    target.classList.remove("is-drag-over");
    const serialized = event.dataTransfer?.getData(FIELD_MIME_TYPE);
    if (!serialized) return;

    try {
      handlers.onBind(item.locatorId, JSON.parse(serialized) as DataFieldNode);
    } catch {
      handlers.onError("拖拽字段数据无效。");
    }
  });

  refreshBindingTargetStates(paragraph, [item]);
  return true;
}

function getBindableText(paragraph: HTMLElement): string {
  return normalizeText(
    getBindableTextNodes(paragraph)
      .map((node) => node.data)
      .join("")
  );
}

function getBindableTextNodes(paragraph: HTMLElement): Text[] {
  const walker = document.createTreeWalker(
    paragraph,
    NodeFilter.SHOW_TEXT,
    {
      acceptNode(node: Text): number {
        const parent = node.parentElement;
        if (
          parent?.closest(
            ".docx-chart-slot, script, style, .docx-chart-unsupported"
          )
        ) {
          return NodeFilter.FILTER_REJECT;
        }
        return NodeFilter.FILTER_ACCEPT;
      },
    }
  );

  const nodes: Text[] = [];
  let node: Text | null;
  while ((node = walker.nextNode() as Text | null)) {
    nodes.push(node);
  }
  return nodes;
}

function findBoundary(
  nodes: Text[],
  absoluteOffset: number,
  preferPreviousAtBoundary: boolean
): TextBoundary | null {
  let cursor = 0;

  for (const node of nodes) {
    const next = cursor + node.data.length;
    if (
      absoluteOffset < next ||
      (absoluteOffset === next && preferPreviousAtBoundary)
    ) {
      return { node, offset: absoluteOffset - cursor };
    }
    cursor = next;
  }

  if (absoluteOffset === cursor && nodes.length > 0) {
    const node = nodes[nodes.length - 1];
    return { node, offset: node.data.length };
  }

  return null;
}

function normalizeText(value: string): string {
  return value.replace(/\u00a0/g, " ");
}

