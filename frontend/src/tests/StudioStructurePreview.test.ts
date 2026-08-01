import { createApp, nextTick, type App } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import StudioStructurePreview from "../features/template-studio/components/StudioStructurePreview.vue";
import {
  applyStructurePreviewRange,
  configureSplitNodeLane,
  findSafePageEndSplitCandidates,
  findRenderedBlockForMarker,
  placeSplitNodeInOwnRow,
  STRUCTURE_PREVIEW_HIDDEN_CLASS,
  STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS,
  shouldShowSplitNode,
} from "../features/template-studio/structureSplitNodes";
import type { TemplateOutlineBlock } from "../api/types";

let app: App<Element> | null = null;

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.replaceChildren();
});

describe("StudioStructurePreview zoom controls", () => {
  it("zooms the Word preview without changing document data", async () => {
    const container = document.createElement("div");
    document.body.append(container);
    app = createApp(StudioStructurePreview, {
      versionView: null,
      blocks: [],
      splitIndexes: [],
      pendingIndex: null,
    });
    app.mount(container);

    const zoomContent = container.querySelector<HTMLElement>(
      ".preview-zoom-content"
    );
    const select = container.querySelector<HTMLSelectElement>(".preview-zoom select");
    expect(zoomContent?.style.getPropertyValue("--preview-zoom")).toBe("1");
    expect(select?.value).toBe("100");

    findButton(container, "−").click();
    await nextTick();
    expect(zoomContent?.style.getPropertyValue("--preview-zoom")).toBe("0.9");
    expect(select?.value).toBe("90");

    findButton(container, "+").click();
    await nextTick();
    expect(zoomContent?.style.getPropertyValue("--preview-zoom")).toBe("1");

    select!.value = "50";
    select!.dispatchEvent(new Event("change"));
    await nextTick();
    expect(zoomContent?.style.getPropertyValue("--preview-zoom")).toBe("0.5");

    findButton(container, "100%").click();
    await nextTick();
    expect(select?.value).toBe("100");
  });

  it("fits a wide page to the canvas and keeps supported zoom steps", async () => {
    const container = document.createElement("div");
    document.body.append(container);
    app = createApp(StudioStructurePreview, {
      versionView: null,
      blocks: [],
      splitIndexes: [],
      pendingIndex: null,
    });
    app.mount(container);

    const canvas = container.querySelector<HTMLElement>(".preview-canvas")!;
    const documentContainer = container.querySelector<HTMLElement>(
      ".docx-viewer__document"
    )!;
    const page = document.createElement("section");
    page.className = "docx";
    page.getBoundingClientRect = () =>
      ({ width: 800 } as DOMRect);
    documentContainer.append(page);
    Object.defineProperty(canvas, "clientWidth", { configurable: true, value: 500 });

    findButton(container, "适应宽度").click();
    await nextTick();

    const select = container.querySelector<HTMLSelectElement>(".preview-zoom select");
    const zoomContent = container.querySelector<HTMLElement>(
      ".preview-zoom-content"
    );
    expect(select?.value).toBe("50");
    expect(zoomContent?.style.getPropertyValue("--preview-zoom")).toBe("0.5");
  });
});

describe("StudioStructurePreview split node visibility", () => {
  it("gives split controls their own non-overlapping row", () => {
    const container = document.createElement("div");
    document.body.append(container);
    app = createApp(StudioStructurePreview, {
      versionView: null,
      blocks: [],
      splitIndexes: [],
      pendingIndex: null,
    });
    app.mount(container);

    const documentContainer = container.querySelector<HTMLElement>(
      ".docx-viewer__document"
    )!;
    const host = document.createElement("div");
    host.className = "structure-node-host";
    const button = document.createElement("button");
    button.className = "structure-node";
    host.append(button);
    configureSplitNodeLane(host, button);
    documentContainer.append(host);

    expect(host.style.position).toBe("relative");
    expect(host.style.minHeight).toBe("28px");
    expect(host.style.margin).toBe("8px 0px");
    expect(host.style.padding).toBe("0px");
    expect(host.style.overflow).toBe("visible");
    expect(host.style.pointerEvents).toBe("none");
    expect(button.style.pointerEvents).toBe("auto");
  });

  it("places separate rows above the text and the following image", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <section class="docx">
        <article><p id="caption">（30）文字说明</p><p id="image"><img /></p></article>
      </section>`;
    const article = container.querySelector<HTMLElement>("article")!;
    const caption = container.querySelector<HTMLElement>("#caption")!;
    const image = container.querySelector<HTMLElement>("#image")!;
    const captionHost = document.createElement("div");
    const imageHost = document.createElement("div");

    expect(placeSplitNodeInOwnRow(captionHost, caption, "before")).toBe(true);
    expect(placeSplitNodeInOwnRow(imageHost, image, "before")).toBe(true);
    expect(Array.from(article.children)).toEqual([
      captionHost,
      caption,
      imageHost,
      image,
    ]);
  });

  it("hides whitespace-only paragraphs without hiding their content block", () => {
    const paragraph = document.createElement("p");
    paragraph.innerHTML = "&nbsp; \u200b";

    expect(shouldShowSplitNode(outlineBlock("PARAGRAPH"), paragraph)).toBe(false);
  });

  it("keeps paragraphs with text or visual content and always keeps tables", () => {
    const textParagraph = document.createElement("p");
    textParagraph.textContent = "正文";
    const imageParagraph = document.createElement("p");
    imageParagraph.append(document.createElement("img"));
    const emptyTable = document.createElement("table");

    expect(shouldShowSplitNode(outlineBlock("PARAGRAPH"), textParagraph)).toBe(true);
    expect(shouldShowSplitNode(outlineBlock("PARAGRAPH"), imageParagraph)).toBe(true);
    expect(shouldShowSplitNode(outlineBlock("TABLE"), emptyTable)).toBe(true);
  });

  it("finds a marked block on a later rendered page without using DOM indexes", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx"><article><p>第一页</p><p>分页续段</p></article></section>
        <section class="docx"><article><figure class="docx-chart-figure"><p><span id="wtb_block_8"></span>图表</p></figure></article></section>
      </div>`;

    const renderedBlock = findRenderedBlockForMarker(
      container,
      "wtb_block_8"
    );

    expect(renderedBlock?.tagName).toBe("P");
    expect(renderedBlock?.textContent).toContain("图表");
  });

  it("keeps separate split anchors above and below a caption before its chart", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx">
          <article>
            <figure class="docx-chart-figure">
              <p class="docx-chart-caption"><span id="caption_marker"></span>市场营销专业</p>
              <p class="docx-chart-paragraph"><span id="chart_marker"></span><span class="docx-chart-slot"></span></p>
            </figure>
          </article>
        </section>
      </div>`;

    const captionAnchor = findRenderedBlockForMarker(container, "caption_marker");
    const chartAnchor = findRenderedBlockForMarker(container, "chart_marker");

    expect(captionAnchor).not.toBe(chartAnchor);
    expect(captionAnchor?.classList.contains("docx-chart-caption")).toBe(true);
    expect(chartAnchor?.classList.contains("docx-chart-paragraph")).toBe(true);
    expect(captionAnchor?.nextElementSibling).toBe(chartAnchor);
  });

  it("keeps a table bookmark anchored to the whole table", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx"><article><table><tbody><tr><td><p><span id="table_marker"></span>单元格</p></td></tr></tbody></table></article></section>
      </div>`;

    const tableAnchor = findRenderedBlockForMarker(container, "table_marker");

    expect(tableAnchor?.tagName).toBe("TABLE");
  });

  it("shows only the selected partition and hides unrelated rendered pages", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx" id="page-1"><article><p id="intro">引言</p></article></section>
        <section class="docx" id="page-2"><article>
          <figure id="figure"><p id="caption">图表标题</p><p id="chart">图表</p></figure>
          <p id="summary">总结</p>
        </article></section>
      </div>`;
    const intro = container.querySelector<HTMLElement>("#intro")!;
    const figure = container.querySelector<HTMLElement>("#figure")!;
    const caption = container.querySelector<HTMLElement>("#caption")!;
    const chart = container.querySelector<HTMLElement>("#chart")!;
    const summary = container.querySelector<HTMLElement>("#summary")!;

    const visible = applyStructurePreviewRange(
      container,
      [intro, figure, summary],
      [intro, caption, chart, summary],
      { startIndex: 1, endIndex: 1 }
    );

    expect(visible).toEqual([caption]);
    expect(container.querySelector("#page-1")?.classList).toContain(
      STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS
    );
    expect(figure.classList).not.toContain(STRUCTURE_PREVIEW_HIDDEN_CLASS);
    expect(chart.classList).toContain(STRUCTURE_PREVIEW_HIDDEN_CLASS);
    expect(summary.classList).toContain(STRUCTURE_PREVIEW_HIDDEN_CLASS);

    applyStructurePreviewRange(
      container,
      [intro, figure, summary],
      [intro, caption, chart, summary],
      null
    );
    expect(container.querySelector(`.${STRUCTURE_PREVIEW_HIDDEN_CLASS}`)).toBeNull();
    expect(container.querySelector(`.${STRUCTURE_PREVIEW_PAGE_HIDDEN_CLASS}`)).toBeNull();
  });

  it("adds page-end boundaries only between complete, non-continuous blocks", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx" id="page-a"><article><p id="a-start">第一块</p><span id="a-end"></span></article></section>
        <section class="docx" id="page-b"><article><p id="b-start">第二块</p><span id="b-end"></span></article></section>
      </div>`;
    const aStart = container.querySelector<HTMLElement>("#a-start")!;
    const bStart = container.querySelector<HTMLElement>("#b-start")!;

    const candidates = findSafePageEndSplitCandidates(
      container,
      [aStart, bStart],
      [aStart, bStart]
    );

    expect(candidates).toHaveLength(1);
    expect(candidates[0].splitIndex).toBe(1);
    expect(candidates[0].page.id).toBe("page-a");
  });

  it("does not add a page-end boundary between explanatory text and its image", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx"><article><p id="caption">（30）指标雷达图</p><span id="caption-end"></span></article></section>
        <section class="docx"><article><p id="blank">&nbsp;</p><p id="image"><img /></p><span id="image-end"></span></article></section>
      </div>`;

    expect(
      findSafePageEndSplitCandidates(
        container,
        [
          container.querySelector<HTMLElement>("#caption")!,
          container.querySelector<HTMLElement>("#blank")!,
          container.querySelector<HTMLElement>("#image")!,
        ],
        [
          container.querySelector<HTMLElement>("#caption")!,
          container.querySelector<HTMLElement>("#blank")!,
          container.querySelector<HTMLElement>("#image")!,
        ]
      )
    ).toEqual([]);
  });

  it("does not split a source block that continues onto the next page", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <div class="docx-wrapper">
        <section class="docx"><article><p id="long-start">长段落开头</p></article></section>
        <section class="docx"><article><p id="long-end">长段落结尾</p><p id="next-start">下一块</p></article></section>
      </div>`;

    expect(
      findSafePageEndSplitCandidates(
        container,
        [
          container.querySelector<HTMLElement>("#long-start")!,
          container.querySelector<HTMLElement>("#next-start")!,
        ],
        [
          container.querySelector<HTMLElement>("#long-end")!,
          container.querySelector<HTMLElement>("#next-start")!,
        ]
      )
    ).toEqual([]);
  });
});

function outlineBlock(blockType: string): TemplateOutlineBlock {
  return {
    blockId: "body/1",
    blockType,
    displayText: "",
    segmentKey: null,
    canSelect: true,
    depth: 0,
    children: [],
  };
}

function findButton(container: HTMLElement, label: string): HTMLButtonElement {
  const button = Array.from(container.querySelectorAll("button")).find(
    (candidate) => candidate.textContent?.trim() === label
  );
  if (!(button instanceof HTMLButtonElement)) {
    throw new Error(`找不到按钮：${label}`);
  }
  return button;
}
