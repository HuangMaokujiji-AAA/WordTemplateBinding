import { describe, expect, it, vi } from "vitest";
import type { MockItem } from "../api/types";
import {
  decorateRenderedDocument,
  refreshBindingTargetStates,
} from "../features/binding/renderedDocumentBindings";

function createMockItem(overrides: Partial<MockItem> = {}): MockItem {
  return {
    locatorId: "locator-1",
    mockValue: "88.5",
    dataType: "Decimal",
    paragraphText: "得分 88.5 分",
    previewParagraphIndex: 0,
    isBound: false,
    boundDataPath: null,
    boundDataType: null,
    locator: {
      partKind: "MainDocument",
      partKey: "/word/document.xml",
      paragraphIndex: 0,
      startOffset: 3,
      length: 4,
      occurrenceIndex: 0,
      originalValue: "88.5",
      contextHash: "hash",
    },
    ...overrides,
  };
}

describe("decorateRenderedDocument", () => {
  it("wraps a locator that spans multiple rendered runs", () => {
    const container = document.createElement("div");
    container.innerHTML = "<p><span>得分 88</span><span>.5 分</span></p>";
    const item = createMockItem();
    const onSelect = vi.fn();

    const result = decorateRenderedDocument(container, [item], {
      onSelect,
      onBind: vi.fn(),
      onError: vi.fn(),
    });

    const target = container.querySelector<HTMLElement>(
      ".template-binding-target"
    );
    expect(result.renderedCount).toBe(1);
    expect(result.unresolvedLocatorIds).toEqual([]);
    expect(target?.textContent).toBe("88.5");
    expect(target?.dataset.locatorId).toBe("locator-1");

    target?.click();
    expect(onSelect).toHaveBeenCalledWith(item);
  });

  it("updates an injected target when the backend binding snapshot changes", () => {
    const container = document.createElement("div");
    container.innerHTML = "<p>得分 88.5 分</p>";
    const item = createMockItem();
    decorateRenderedDocument(container, [item], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });

    refreshBindingTargetStates(container, [
      createMockItem({
        isBound: true,
        boundDataPath: "StudentStatistics.AverageScore",
        boundDataType: "Decimal",
      }),
    ]);

    const target = container.querySelector<HTMLElement>(
      ".template-binding-target"
    );
    expect(target?.classList.contains("is-bound")).toBe(true);
    expect(target?.title).toContain("StudentStatistics.AverageScore");
  });

  it("reports locators whose paragraph cannot be mapped", () => {
    const container = document.createElement("div");
    container.innerHTML = "<p>另一段文字</p>";

    const result = decorateRenderedDocument(
      container,
      [createMockItem()],
      { onSelect: vi.fn(), onBind: vi.fn(), onError: vi.fn() }
    );

    expect(result.renderedCount).toBe(0);
    expect(result.unresolvedLocatorIds).toEqual(["locator-1"]);
  });
});

