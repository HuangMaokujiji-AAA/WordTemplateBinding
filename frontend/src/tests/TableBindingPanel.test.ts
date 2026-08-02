import { createApp, nextTick, type App } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import TableBindingPanel from "../components/TableBindingPanel.vue";
import type { TableItem } from "../api/types";

let app: App<Element> | null = null;

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.replaceChildren();
});

function makeTable(overrides: Partial<TableItem> = {}): TableItem {
  return {
    templateElementId: "table-element-1",
    locatorId: "table-locator-1",
    title: "专业数据表",
    locator: {
      partKey: "/word/document.xml",
      tableIndex: 0,
      firstParagraphIndex: 3,
      headerSignature: "序号|专业名称",
    },
    columns: [
      { columnIndex: 0, header: "序号", suggestedField: null },
      { columnIndex: 1, header: "专业名称", suggestedField: "name" },
    ],
    headerRowCount: 1,
    templateRowCount: 2,
    suggestedSourcePath: null,
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

function mountPanel(table: TableItem, onSave = vi.fn()): HTMLElement {
  const container = document.createElement("div");
  document.body.append(container);
  app = createApp(TableBindingPanel, {
    table,
    dataPath: "items",
    fieldOptions: [
      { label: "专业名称 · String", value: "name" },
      { label: "学生人数 · Number", value: "count" },
    ],
    onSave,
  });
  app.mount(container);
  return container;
}

describe("TableBindingPanel", () => {
  it("uses database-field selects for every Word table column", () => {
    const container = mountPanel(makeTable());
    const selects = container.querySelectorAll<HTMLSelectElement>(
      ".table-column-list article select"
    );

    expect(selects).toHaveLength(2);
    expect([...selects[0].options].map((option) => option.value)).toEqual([
      "",
      "rowNumber",
      "name",
      "count",
    ]);
    expect(selects[1].value).toBe("name");
  });

  it("saves only the fields selected from the current array", async () => {
    const onSave = vi.fn();
    const container = mountPanel(makeTable(), onSave);
    const selects = container.querySelectorAll<HTMLSelectElement>(
      ".table-column-list article select"
    );
    selects[0].value = "rowNumber";
    selects[0].dispatchEvent(new Event("change"));
    await nextTick();

    container.querySelector<HTMLButtonElement>(".table-binding-save")?.click();
    expect(onSave).toHaveBeenCalledOnce();
    expect(onSave.mock.calls[0][0].mapping.columns).toEqual([
      {
        columnIndex: 0,
        header: "序号",
        sourceField: "rowNumber",
        fallbackValue: null,
      },
      {
        columnIndex: 1,
        header: "专业名称",
        sourceField: "name",
        fallbackValue: null,
      },
    ]);
  });

  it("keeps a saved historical field visible when the new snapshot no longer contains it", () => {
    const table = makeTable({
      tableMapping: {
        headerRowCount: 1,
        columns: [
          {
            columnIndex: 1,
            header: "专业名称",
            sourceField: "legacyName",
            fallbackValue: null,
          },
        ],
        filterField: null,
        filterValue: null,
      },
    });
    const container = mountPanel(table);
    const secondColumn = container.querySelectorAll<HTMLSelectElement>(
      ".table-column-list article select"
    )[1];

    expect(secondColumn.value).toBe("legacyName");
    expect(secondColumn.selectedOptions[0].textContent).toContain("当前数据源中未找到");
  });
});
