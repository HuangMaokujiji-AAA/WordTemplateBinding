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

interface LocatedTarget {
  offset: number;
  contextScore: number;
}

interface ParagraphMatch {
  paragraph: HTMLElement;
  index: number;
  text: string;
  score: number;
  offsets: Map<string, number>;
}

/**
 * Maps the server's part/paragraph/character locators onto the docx-preview DOM.
 * Exact paragraph matching is preferred. A context-scored fallback tolerates
 * renderer-only text such as footnote reference numbers without weakening the
 * backend locator used for the final DOCX replacement.
 */
export function decorateRenderedDocument(
  container: HTMLElement,
  mockItems: MockItem[],
  handlers: BindingTargetHandlers
): DecorationResult {
  const paragraphs = Array.from(container.querySelectorAll<HTMLElement>("p"));
  const groups = groupBySourceParagraph(mockItems);
  const renderedLocatorIds = new Set<string>();
  const unresolvedLocatorIds = new Set<string>();
  let mainDocumentSearchStart = 0;

  for (const items of groups) {
    const isFooter = items[0]?.locator.partKind === "Footer";
    const matches = findParagraphMatches(
      paragraphs,
      items,
      isFooter ? 0 : mainDocumentSearchStart,
      isFooter
    );

    if (matches.length === 0) {
      items.forEach((item) => unresolvedLocatorIds.add(item.locatorId));
      continue;
    }

    const selectedMatches = isFooter
      ? selectRepeatedFooterMatches(matches)
      : [matches[0]];
    if (!isFooter) {
      mainDocumentSearchStart = selectedMatches[0].index + 1;
    }

    for (const match of selectedMatches) {
      if (isFooter) markFooterRegion(match.paragraph);

      const targets = items
        .map((item) => ({ item, offset: match.offsets.get(item.locatorId) }))
        .filter(
          (target): target is { item: MockItem; offset: number } =>
            target.offset !== undefined
        )
        .sort((left, right) => right.offset - left.offset);

      for (const target of targets) {
        if (
          insertBindingTarget(
            match.paragraph,
            target.offset,
            target.item.locator.length,
            target.item,
            handlers
          )
        ) {
          renderedLocatorIds.add(target.item.locatorId);
        }
      }
    }

    for (const item of items) {
      if (!renderedLocatorIds.has(item.locatorId)) {
        unresolvedLocatorIds.add(item.locatorId);
      }
    }
  }

  return {
    renderedCount: renderedLocatorIds.size,
    unresolvedLocatorIds: [...unresolvedLocatorIds],
  };
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

    const isFooter = item.locator.partKind === "Footer";
    element.classList.toggle("is-footer", isFooter);
    element.classList.toggle("is-bound", item.isBound);
    element.dataset.partKind = item.locator.partKind;
    if (item.boundDataPath) {
      element.dataset.boundDataPath = item.boundDataPath;
    } else {
      delete element.dataset.boundDataPath;
    }
    refreshPlaceholderText(element, item);
    element.title = item.isBound
      ? `${isFooter ? "页脚 · " : ""}已绑定：${item.boundDataPath}；拖入其他兼容字段可直接改绑`
      : `${isFooter ? "页脚 · " : ""}拖拽一个兼容字段到此处`;
    element.setAttribute(
      "aria-label",
      item.isBound
        ? `${isFooter ? "页脚" : "正文"}模拟值 ${item.mockValue}，已绑定 ${item.boundDataPath}`
        : `${isFooter ? "页脚" : "正文"}模拟值 ${item.mockValue}，未绑定`
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

function groupBySourceParagraph(mockItems: MockItem[]): MockItem[][] {
  const groups = new Map<string, MockItem[]>();
  for (const item of mockItems) {
    const key = [
      item.locator.partKind,
      item.locator.partKey,
      item.locator.paragraphIndex,
    ].join("\u0000");
    const group = groups.get(key) || [];
    group.push(item);
    groups.set(key, group);
  }

  return [...groups.values()].sort((left, right) => {
    const leftFooter = left[0]?.locator.partKind === "Footer" ? 1 : 0;
    const rightFooter = right[0]?.locator.partKind === "Footer" ? 1 : 0;
    return (
      leftFooter - rightFooter ||
      left[0].previewParagraphIndex - right[0].previewParagraphIndex
    );
  });
}

function findParagraphMatches(
  paragraphs: HTMLElement[],
  items: MockItem[],
  searchStart: number,
  isFooter: boolean
): ParagraphMatch[] {
  const expectedText = normalizeText(items[0]?.paragraphText || "");
  const candidates = paragraphs
    .map((paragraph, index) => ({ paragraph, index }))
    .filter(({ paragraph, index }) => {
      if (isFooter) return true;
      return index >= searchStart && !isFooterParagraph(paragraph);
    });

  const matches: ParagraphMatch[] = [];
  for (const candidate of candidates) {
    const text = getBindableText(candidate.paragraph);
    const exactParagraphOffset = text.indexOf(expectedText);
    const offsets = new Map<string, number>();
    let matchedItemCount = 0;
    let contextScore = 0;

    for (const item of items) {
      const located =
        exactParagraphOffset >= 0
          ? {
              offset: exactParagraphOffset + item.locator.startOffset,
              contextScore: 100,
            }
          : locateTargetByContext(text, expectedText, item);
      if (!located) continue;
      offsets.set(item.locatorId, located.offset);
      matchedItemCount += 1;
      contextScore += located.contextScore;
    }

    if (matchedItemCount === 0) continue;
    const footerPreference = isFooterParagraph(candidate.paragraph)
      ? isFooter
        ? 50_000
        : -50_000
      : 0;
    matches.push({
      ...candidate,
      text,
      offsets,
      score:
        footerPreference +
        (exactParagraphOffset >= 0 ? 1_000_000 : 0) +
        matchedItemCount * 10_000 +
        contextScore,
    });
  }

  return matches.sort(
    (left, right) => right.score - left.score || left.index - right.index
  );
}

function locateTargetByContext(
  actualText: string,
  expectedText: string,
  item: MockItem
): LocatedTarget | null {
  const targetText = normalizeText(item.locator.originalValue);
  if (!targetText) return null;

  const expectedStart = item.locator.startOffset;
  const expectedEnd = expectedStart + item.locator.length;
  const expectedPrefix = expectedText.slice(
    Math.max(0, expectedStart - 48),
    expectedStart
  );
  const expectedSuffix = expectedText.slice(expectedEnd, expectedEnd + 48);
  let best: LocatedTarget | null = null;
  let searchFrom = 0;

  while (searchFrom <= actualText.length - targetText.length) {
    const offset = actualText.indexOf(targetText, searchFrom);
    if (offset < 0) break;

    const actualPrefix = actualText.slice(Math.max(0, offset - 48), offset);
    const actualSuffix = actualText.slice(
      offset + targetText.length,
      offset + targetText.length + 48
    );
    const contextScore =
      commonSuffixLength(expectedPrefix, actualPrefix) +
      commonPrefixLength(expectedSuffix, actualSuffix);
    if (!best || contextScore > best.contextScore) {
      best = { offset, contextScore };
    }
    searchFrom = offset + Math.max(1, targetText.length);
  }

  return best;
}

function commonPrefixLength(left: string, right: string): number {
  const max = Math.min(left.length, right.length);
  let length = 0;
  while (length < max && left[length] === right[length]) length += 1;
  return length;
}

function commonSuffixLength(left: string, right: string): number {
  const max = Math.min(left.length, right.length);
  let length = 0;
  while (
    length < max &&
    left[left.length - length - 1] === right[right.length - length - 1]
  ) {
    length += 1;
  }
  return length;
}

function selectRepeatedFooterMatches(matches: ParagraphMatch[]): ParagraphMatch[] {
  const semanticFooterMatches = matches.filter((match) =>
    isFooterParagraph(match.paragraph)
  );
  const candidates = semanticFooterMatches.length > 0
    ? semanticFooterMatches
    : matches;
  const bestScore = candidates[0].score;
  return candidates.filter((match) => match.score === bestScore);
}

function isFooterParagraph(paragraph: HTMLElement): boolean {
  return Boolean(paragraph.closest("footer"));
}

function markFooterRegion(paragraph: HTMLElement): void {
  paragraph.classList.add("template-footer-paragraph");
  paragraph.closest("footer")?.classList.add("template-footer-region");
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
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = target.classList.contains("is-bound")
        ? "link"
        : "copy";
    }
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

function refreshPlaceholderText(element: HTMLElement, item: MockItem): void {
  const originalValue = item.locator.originalValue;
  const hasPlaceholderMarker =
    Boolean(item.placeholderCandidatePath) ||
    (originalValue.startsWith("{{") &&
      originalValue.endsWith("}}") &&
      originalValue.length > 4);
  if (!hasPlaceholderMarker) return;

  const text =
    item.isBound && item.boundDataPath
      ? `{{${item.boundDataPath}}}`
      : originalValue;
  if (element.textContent === text) return;

  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  const textNodes: Text[] = [];
  let node: Text | null;
  while ((node = walker.nextNode() as Text | null)) textNodes.push(node);

  if (textNodes.length === 0) {
    element.append(document.createTextNode(text));
    return;
  }

  // Keep the first rendered run so Word's font styling remains intact.
  textNodes[0].data = text;
  for (const remainingNode of textNodes.slice(1)) {
    remainingNode.data = "";
  }
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
  while ((node = walker.nextNode() as Text | null)) nodes.push(node);
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
