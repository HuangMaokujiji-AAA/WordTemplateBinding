import { describe, expect, it } from "vitest";
import { groupChartWithCaption } from "../features/docx/rendering/groupChartWithCaption";

function createContainer(html: string): HTMLElement {
  const container = document.createElement("div");
  container.innerHTML = `<div class="docx-wrapper">${html}</div>`;
  document.body.appendChild(container);
  return container;
}

describe("groupChartWithCaption", () => {
  it("groups a chart with its following caption on the same page", () => {
    const container = createContainer(`
      <section class="docx"><article>
        <p id="chart"><span class="docx-chart-slot"></span></p>
        <p id="caption">图1 学生成绩分布</p>
      </article></section>
    `);
    const chart = container.querySelector<HTMLElement>(".docx-chart-slot")!;

    const figure = groupChartWithCaption(container, chart, {
      position: "after",
      text: "图1 学生成绩分布",
    });

    expect(figure).not.toBeNull();
    expect(figure?.children[0].id).toBe("chart");
    expect(figure?.children[1].id).toBe("caption");
    expect(figure?.getAttribute("aria-label")).toBe("图1 学生成绩分布");
    container.remove();
  });

  it("moves a chart to the caption page when docx-preview split them", () => {
    const container = createContainer(`
      <section class="docx"><article>
        <p>正文</p>
        <p id="chart"><span class="docx-chart-slot"></span></p>
      </article></section>
      <section class="docx"><article>
        <p id="caption">图2 学业水平</p>
        <p>后续正文</p>
      </article></section>
    `);
    const chart = container.querySelector<HTMLElement>(".docx-chart-slot")!;

    const figure = groupChartWithCaption(container, chart, {
      position: "after",
      text: "图2 学业水平",
    });

    const pages = container.querySelectorAll("section.docx");
    expect(pages[0].querySelector("#chart")).toBeNull();
    expect(pages[1].querySelector(".docx-chart-figure")).toBe(figure);
    expect(figure?.children[0].id).toBe("chart");
    expect(figure?.children[1].id).toBe("caption");
    container.remove();
  });

  it("moves a preceding caption to the chart page", () => {
    const container = createContainer(`
      <section class="docx"><article>
        <p id="caption">图3 校际差异</p>
      </article></section>
      <section class="docx"><article>
        <p id="chart"><span class="docx-chart-slot"></span></p>
      </article></section>
    `);
    const chart = container.querySelector<HTMLElement>(".docx-chart-slot")!;

    const figure = groupChartWithCaption(container, chart, {
      position: "before",
      text: "图3 校际差异",
    });

    const pages = container.querySelectorAll("section.docx");
    expect(pages[0].querySelector("#caption")).toBeNull();
    expect(pages[1].querySelector(".docx-chart-figure")).toBe(figure);
    expect(figure?.children[0].id).toBe("caption");
    expect(figure?.children[1].id).toBe("chart");
    container.remove();
  });
});
