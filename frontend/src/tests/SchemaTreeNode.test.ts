import { createApp, type App } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import SchemaTreeNode from "../components/SchemaTreeNode.vue";
import type { DataFieldNode } from "../api/types";

let app: App<Element> | null = null;

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.replaceChildren();
});

function mountNode(node: DataFieldNode): HTMLElement {
  const container = document.createElement("div");
  document.body.append(container);
  app = createApp(SchemaTreeNode, { node });
  app.mount(container);
  return container;
}

describe("SchemaTreeNode hover details", () => {
  it("renders metadata, sample value and nested data composition in the tooltip", () => {
    const node: DataFieldNode = {
      name: "专业列表",
      path: "items",
      type: "Array",
      isCollection: true,
      isLeaf: false,
      isBindable: true,
      comment: "当前学校的专业集合",
      isNullable: false,
      sampleValueJson: '[{"name":"人工智能","count":120}]',
      children: [
        {
          name: "专业名称",
          path: "items[].name",
          type: "String",
          isCollection: false,
          isLeaf: true,
          isBindable: true,
          comment: "专业名称",
          isNullable: false,
          sampleValueJson: '"人工智能"',
          children: [],
        },
        {
          name: "学生人数",
          path: "items[].count",
          type: "Integer",
          isCollection: false,
          isLeaf: true,
          isBindable: true,
          isNullable: true,
          sampleValueJson: "120",
          children: [],
        },
      ],
    };

    const container = mountNode(node);
    const tooltip = container.querySelector<HTMLElement>(".schema-field-tooltip");
    const label = container.querySelector<HTMLElement>(".schema-label");

    expect(tooltip).not.toBeNull();
    expect(label?.getAttribute("aria-describedby")).toBe(tooltip?.id);
    expect(tooltip?.textContent).toContain("当前学校的专业集合");
    expect(tooltip?.textContent).toContain("人工智能");
    expect(tooltip?.textContent).toContain("数组单条记录字段（2 项）");
    expect(tooltip?.textContent).toContain("items[].name");
    expect(tooltip?.textContent).toContain("items[].count");
    const sample = tooltip?.querySelector<HTMLElement>(".schema-sample-value");
    expect(sample?.textContent).toContain('\n  {\n    "name": "人工智能"');
  });

  it("marks a scalar leaf as having no child structure", () => {
    const container = mountNode({
      name: "学校名称",
      path: "school.name",
      type: "String",
      isCollection: false,
      isLeaf: true,
      isBindable: true,
      comment: null,
      isNullable: false,
      sampleValueJson: '"测试大学"',
      children: [],
    });

    expect(container.querySelector(".schema-field-tooltip")?.textContent)
      .toContain("叶子字段，无下级数据结构");
  });

  it("preserves line breaks and marks oversized sample values as truncated", () => {
    const container = mountNode({
      name: "长文本",
      path: "content",
      type: "String",
      isCollection: false,
      isLeaf: true,
      isBindable: true,
      isNullable: false,
      sampleValueJson: JSON.stringify(`第一行\r\n${"长内容".repeat(500)}\n最后一行`),
      children: [],
    });
    const sample = container.querySelector<HTMLElement>(".schema-sample-value");

    expect(sample?.textContent).toContain("第一行\n长内容");
    expect(sample?.textContent).not.toContain("\r");
    expect(sample?.textContent).toContain("示例值内容已截断");
  });
});
