const fieldMimeType = "application/x-word-template-field";

export function renderPreview(container, preview, mockItems, handlers) {
  container.replaceChildren();
  const mockByLocator = new Map(
    mockItems.map((item) => [item.locatorId, item]),
  );

  for (const paragraph of preview.paragraphs) {
    const element = document.createElement("p");
    element.className = "preview-paragraph";
    element.id = `paragraph-${paragraph.paragraphIndex}`;
    element.dataset.paragraphIndex = String(paragraph.paragraphIndex);

    let cursor = 0;
    const highlights = [...paragraph.highlights].sort(
      (left, right) => left.startOffset - right.startOffset,
    );
    for (const highlight of highlights) {
      if (highlight.startOffset > cursor) {
        element.append(
          document.createTextNode(
            paragraph.text.slice(cursor, highlight.startOffset),
          ),
        );
      }

      const mockItem = mockByLocator.get(highlight.locatorId);
      const span = document.createElement("span");
      span.className = "mock-highlight";
      span.tabIndex = 0;
      span.setAttribute("role", "button");
      span.dataset.locatorId = highlight.locatorId;
      span.append(
        document.createTextNode(
          paragraph.text.slice(
            highlight.startOffset,
            highlight.startOffset + highlight.length,
          ),
        ),
      );
      if (mockItem?.isBound) {
        span.classList.add("bound");
        span.title = `已绑定：${mockItem.boundDataPath}`;
        span.setAttribute(
          "aria-label",
          `模拟值 ${highlight.mockValue}，已绑定 ${mockItem.boundDataPath}`,
        );
      } else {
        span.title = "拖拽一个兼容字段到此处";
        span.setAttribute(
          "aria-label",
          `模拟值 ${highlight.mockValue}，未绑定`,
        );
      }

      span.addEventListener("click", () => handlers.onSelect(mockItem));
      span.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") {
          handlers.onSelect(mockItem);
        }
      });
      span.addEventListener("dragover", (event) => {
        event.preventDefault();
        span.classList.add("drag-over");
      });
      span.addEventListener("dragleave", () => {
        span.classList.remove("drag-over");
      });
      span.addEventListener("drop", (event) => {
        event.preventDefault();
        span.classList.remove("drag-over");
        const serialized = event.dataTransfer.getData(fieldMimeType);
        if (!serialized) {
          return;
        }
        try {
          handlers.onBind(highlight.locatorId, JSON.parse(serialized));
        } catch {
          handlers.onError("拖拽字段数据无效。");
        }
      });

      element.append(span);
      cursor = highlight.startOffset + highlight.length;
    }

    if (cursor < paragraph.text.length) {
      element.append(document.createTextNode(paragraph.text.slice(cursor)));
    }
    if (paragraph.text.length === 0) {
      element.append(document.createTextNode("\u00a0"));
    }
    container.append(element);
  }
}

export function renderParagraphNavigation(container, preview) {
  container.replaceChildren();
  for (const paragraph of preview.paragraphs) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "paragraph-link";

    const title = document.createElement("strong");
    title.textContent = `段落 ${paragraph.paragraphIndex + 1}`;
    const excerpt = document.createElement("span");
    excerpt.textContent = paragraph.text.trim() || "空段落";
    button.append(title, excerpt);
    button.addEventListener("click", () => {
      document
        .getElementById(`paragraph-${paragraph.paragraphIndex}`)
        ?.scrollIntoView({ behavior: "smooth", block: "start" });
    });
    container.append(button);
  }
}

export function focusMockItem(locatorId) {
  const element = document.querySelector(
    `.mock-highlight[data-locator-id="${CSS.escape(locatorId)}"]`,
  );
  element?.scrollIntoView({ behavior: "smooth", block: "center" });
  element?.focus({ preventScroll: true });
}
