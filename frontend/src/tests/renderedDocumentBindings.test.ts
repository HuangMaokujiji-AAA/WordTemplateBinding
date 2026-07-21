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

  it("uses locator context when docx-preview inserts a footnote number", () => {
    const paragraphText = "平均分为226，高于全省36分";
    const container = document.createElement("div");
    container.innerHTML =
      "<p><span>平均分为226</span><sup>1</sup><span>，高于全省36分</span></p>";
    const first = createMockItem({
      locatorId: "score",
      mockValue: "226",
      dataType: "Integer",
      paragraphText,
      locator: {
        partKind: "MainDocument",
        partKey: "/word/document.xml",
        paragraphIndex: 0,
        startOffset: paragraphText.indexOf("226"),
        length: 3,
        occurrenceIndex: 0,
        originalValue: "226",
        contextHash: "score-hash",
      },
    });
    const second = createMockItem({
      locatorId: "difference",
      mockValue: "36",
      dataType: "Integer",
      paragraphText,
      locator: {
        partKind: "MainDocument",
        partKey: "/word/document.xml",
        paragraphIndex: 0,
        startOffset: paragraphText.indexOf("36"),
        length: 2,
        occurrenceIndex: 1,
        originalValue: "36",
        contextHash: "difference-hash",
      },
    });

    const result = decorateRenderedDocument(container, [first, second], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });

    expect(result.renderedCount).toBe(2);
    expect(result.unresolvedLocatorIds).toEqual([]);
    expect(
      Array.from(container.querySelectorAll(".template-binding-target")).map(
        (element) => element.textContent
      )
    ).toEqual(["226", "36"]);
  });

  it("decorates every rendered occurrence of a footer with a distinct style", () => {
    const container = document.createElement("div");
    container.innerHTML = [
      "<section><article><p>正文</p></article><footer><p>页脚统计 88.5 分</p></footer></section>",
      "<section><article><p>第二页</p></article><footer><p>页脚统计 88.5 分</p></footer></section>",
    ].join("");
    const item = createMockItem({
      paragraphText: "页脚统计 88.5 分",
      locator: {
        partKind: "Footer",
        partKey: "/word/footer1.xml",
        paragraphIndex: 0,
        startOffset: 5,
        length: 4,
        occurrenceIndex: 0,
        originalValue: "88.5",
        contextHash: "footer-hash",
      },
    });

    const result = decorateRenderedDocument(container, [item], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });

    const targets = container.querySelectorAll(
      ".template-binding-target.is-footer"
    );
    expect(result.renderedCount).toBe(1);
    expect(result.unresolvedLocatorIds).toEqual([]);
    expect(targets).toHaveLength(2);
    expect(container.querySelectorAll("footer.template-footer-region")).toHaveLength(2);
  });
});
