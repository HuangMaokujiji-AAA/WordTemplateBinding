import { describe, expect, it } from "vitest";
import { hydrateTemplateResponse } from "../api/client";
import type { BindingItemRecord, TemplateVersionView } from "../api/types";

describe("template response table hydration", () => {
  it("restores table metadata and its saved mapping", () => {
    const view = {
      template: {
        id: "1", templateCode: "T", templateName: "模板", templateType: "DOCX",
        categoryCode: null, templateStatus: "ACTIVE", description: null,
        currentVersionNo: 1, createdAt: "2026-01-01", updatedAt: "2026-01-01",
      },
      version: { id: "2", templateId: "1", versionNo: 1, fileObjectId: "3", versionStatus: "READY", elementCount: 1, createdAt: "2026-01-01" },
      file: { id: "3", originalName: "test.docx", mimeType: "application/docx", fileSize: 10, sha256: null, objectStatus: "READY" },
      elements: [{
        id: "4", templateVersionId: "2", segmentId: "5", elementKey: "table:x",
        elementType: "TABLE", locatorType: "OPENXML_TABLE", displayName: "专业表",
        locator: { locatorId: "table-x", partKey: "/word/document.xml", tableIndex: 0, firstParagraphIndex: 1, headerSignature: "名称|人数" },
        bindingSchema: {
          headerRowCount: 1, templateRowCount: 2,
          tableColumns: [
            { columnIndex: 0, header: "名称", suggestedField: "name" },
            { columnIndex: 1, header: "人数", suggestedField: "count" },
          ],
          columns: [{ columnIndex: 0, header: "名称", sourceField: "name", fallbackValue: null }],
        },
        defaultValue: null, isRequired: false, sortNo: 0, segmentLocalOrder: 0,
        parseStatus: "VALID", parseMessage: null,
      }],
      parseResult: {
        scanResult: { contentHash: "hash", mockItems: [], charts: [], preview: { paragraphs: [] } },
        importSummary: { textBindingsRestored: 0, chartBindingsRestored: 0, tableBindingsRestored: 0, unresolvedPlaceholders: [], warnings: [] },
        warnings: [],
      },
    } as TemplateVersionView;
    const bindings: BindingItemRecord[] = [{
      id: "6", bindingSetId: "7", templateElementId: "4", targetProperty: "$",
      sourceKind: "DATA_SOURCE", dataSourceId: "8", sourcePath: "items",
      transformConfig: null,
      formatConfig: {
        tableMapping: {
          headerRowCount: 1,
          columns: [{ columnIndex: 1, header: "人数", sourceField: "count", fallbackValue: "0" }],
          filterField: null,
          filterValue: null,
        },
      },
      fallbackValue: null, isRequired: false,
    }];

    const result = hydrateTemplateResponse(view, bindings);
    expect(result.tables).toHaveLength(1);
    const table = result.tables[0];
    expect(result.tableCount).toBe(1);
    expect(table.isBound).toBe(true);
    expect(table.boundDataPath).toBe("items");
    expect(table.columns).toHaveLength(2);
    expect(table.tableMapping?.columns[0].sourceField).toBe("count");
  });
});
