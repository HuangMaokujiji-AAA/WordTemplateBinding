import { describe, expect, it, vi } from "vitest";
import type { DataFieldNode, TableItem } from "../api/types";
import {
  decorateRenderedTables,
  refreshTableBindingTargetStates,
} from "../features/binding/renderedTableBindings";
import { FIELD_MIME_TYPE } from "../features/binding/renderedDocumentBindings";

function table(overrides: Partial<TableItem> = {}): TableItem {
  return {
    templateElementId: "10",
    locatorId: "table-locator-1",
    title: "专业表",
    locator: {
      partKey: "/word/document.xml",
      tableIndex: 0,
      firstParagraphIndex: 0,
      headerSignature: "名称|人数",
    },
    columns: [
      { columnIndex: 0, header: "名称", suggestedField: "name" },
      { columnIndex: 1, header: "人数", suggestedField: "count" },
    ],
    headerRowCount: 1,
    templateRowCount: 2,
    suggestedSourcePath: "items",
    contextLabel: null,
    defaultMapping: {
      headerRowCount: 1,
      columns: [],
      filterField: null,
      filterValue: null,
    },
    tableMapping: null,
    isBindable: true,
    isBound: false,
    boundDataPath: null,
    parseMessage: null,
    segmentLocalOrder: 0,
    ...overrides,
  };
}

describe("rendered table bindings", () => {
  it("decorates a rendered table, selects it and accepts Array drops", () => {
    const container = document.createElement("div");
    container.innerHTML = '<section class="docx"><article><table><tr><td>样例</td></tr></table></article></section>';
    const onSelect = vi.fn();
    const onBind = vi.fn();
    const onError = vi.fn();
    const item = table();

    const result = decorateRenderedTables(container, [item], {
      onSelect,
      onBind,
      onError,
    });
    const target = container.querySelector<HTMLElement>("table")!;
    target.click();
    expect(onSelect).toHaveBeenCalledWith(item);
    expect(result.renderedCount).toBe(1);
    expect(target.dataset.tableLocatorId).toBe(item.locatorId);

    const field: DataFieldNode = {
      name: "专业列表",
      path: "items",
      type: "Array",
      isCollection: true,
      isLeaf: false,
      isBindable: true,
      children: [],
    };
    const drop = new Event("drop", { bubbles: true, cancelable: true });
    Object.defineProperty(drop, "dataTransfer", {
      value: {
        types: [FIELD_MIME_TYPE],
        getData: (type: string) => type === FIELD_MIME_TYPE ? JSON.stringify(field) : "",
      },
    });
    target.dispatchEvent(drop);
    expect(onBind).toHaveBeenCalledWith(item.locatorId, field);
    expect(onError).not.toHaveBeenCalled();
  });

  it("refreshes the bound state without redecorating", () => {
    const container = document.createElement("div");
    container.innerHTML = '<section class="docx"><article><table></table></article></section>';
    const item = table();
    decorateRenderedTables(container, [item], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });
    refreshTableBindingTargetStates(container, [
      { ...item, isBound: true, boundDataPath: "items" },
    ]);
    expect(container.querySelector("table")?.classList.contains("is-bound")).toBe(true);
    expect(container.querySelector("table")?.getAttribute("title")).toContain("items");
  });

  it("matches by header signature when an unrelated layout table comes first", () => {
    const container = document.createElement("div");
    container.innerHTML = `
      <section class="docx"><article>
        <table id="layout"><tr><td>布局</td></tr></table>
        <table id="business"><tr><td>名称</td><td>人数</td></tr><tr><td>A</td><td>1</td></tr></table>
      </article></section>`;
    decorateRenderedTables(container, [table()], {
      onSelect: vi.fn(),
      onBind: vi.fn(),
      onError: vi.fn(),
    });
    expect(container.querySelector("#layout")?.classList.contains("template-table-target")).toBe(false);
    expect(container.querySelector("#business")?.classList.contains("template-table-target")).toBe(true);
  });
});
