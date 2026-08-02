import type {
  BindingItemRecord,
  ChartBindingMappingResponse,
  TemplateElementRecord,
  TemplateRecord,
  TemplateResponse,
  TemplateSegmentBoundaryDraft,
  TemplateSegmentOutline,
  TemplateSegmentRecord,
  TemplateStudioWorkspace,
  TableBindingMapping,
  TemplateVersionRecord,
  TemplateVersionView,
  PublishedTemplateList,
} from "./types";
import type { PagedResponse } from "./httpClient";
import { DOCX_CONTENT_TYPE, downloadDocx, parseError, requestJson } from "./httpClient";

export async function uploadTemplate(file: File): Promise<TemplateResponse> {
  const body = new FormData();
  body.append("file", file);
  return requestJson<TemplateResponse>("/api/templates/upload", {
    method: "POST",
    body,
  });
}

export function getTemplate(templateId: string): Promise<TemplateResponse> {
  return requestJson<TemplateResponse>(
    `/api/templates/${encodeURIComponent(templateId)}`
  );
}

export function rescanTemplate(templateId: string): Promise<TemplateResponse> {
  return requestJson<TemplateResponse>(
    `/api/templates/${encodeURIComponent(templateId)}/rescan`,
    { method: "POST" }
  );
}
export async function downloadReusableTemplate(
  templateId: string
): Promise<string> {
  return downloadDocx(
    `/api/templates/${encodeURIComponent(templateId)}/export-reusable`,
    { method: "POST" },
    "template-template.docx"
  );
}
export async function listPersistentTemplates(
  params?: {
    name?: string;
    code?: string;
    type?: string;
    status?: string;
    page?: number;
    pageSize?: number;
  }
): Promise<PagedResponse<TemplateRecord>> {
  const url = new URL("/api/templates", window.location.origin);
  if (params?.name) url.searchParams.set("name", params.name);
  if (params?.code) url.searchParams.set("code", params.code);
  if (params?.type) url.searchParams.set("type", params.type);
  if (params?.status) url.searchParams.set("status", params.status);
  url.searchParams.set("page", String(params?.page || 1));
  url.searchParams.set("pageSize", String(params?.pageSize || 20));
  return requestJson<PagedResponse<TemplateRecord>>(url);
}

export function getPersistentTemplate(templateId: string): Promise<TemplateRecord> {
  return requestJson<TemplateRecord>(
    `/api/templates/${encodeURIComponent(templateId)}`
  );
}

export function updateTemplate(
  templateId: string,
  request: {
    templateName?: string;
    categoryCode?: string | null;
    description?: string | null;
    templateStatus?: string;
    expectedRowVersion: number;
  }
): Promise<TemplateRecord> {
  return requestJson<TemplateRecord>(
    `/api/templates/${encodeURIComponent(templateId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function archiveTemplate(
  templateId: string
): Promise<{ archived: boolean }> {
  return requestJson<{ archived: boolean }>(
    `/api/templates/${encodeURIComponent(templateId)}`,
    { method: "DELETE" }
  );
}

export function restoreTemplate(
  templateId: string
): Promise<{ restored: boolean }> {
  return requestJson<{ restored: boolean }>(
    `/api/templates/${encodeURIComponent(templateId)}/restore`,
    { method: "POST" }
  );
}

export function listTemplateVersions(
  templateId: string
): Promise<TemplateVersionView[]> {
  return requestJson<TemplateVersionRecord[]>(
    `/api/templates/${encodeURIComponent(templateId)}/versions`
  ).then((versions) =>
    Promise.all(
      versions.map((version) =>
        getTemplateVersion(version.id)
      )
    )
  );
}

export function getTemplateVersion(
  templateVersionId: string
): Promise<TemplateVersionView> {
  return requestJson<TemplateVersionView>(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}`
  );
}

export async function uploadPersistentTemplate(
  file: File,
  templateId?: string | null,
  metadata?: {
    templateCode: string;
    templateName: string;
    templateType: string;
    categoryCode?: string | null;
    description?: string | null;
  }
): Promise<TemplateVersionView> {
  const body = new FormData();
  body.append("file", file);
  if (templateId) {
    return requestJson<TemplateVersionView>(
      `/api/templates/${encodeURIComponent(templateId)}/versions`,
      { method: "POST", body }
    );
  }

  body.append("templateCode", metadata?.templateCode || `TPL_${Date.now()}`);
  body.append(
    "templateName",
    metadata?.templateName || file.name.replace(/\.docx$/i, "")
  );
  body.append("templateType", metadata?.templateType || "SECTION");
  if (metadata?.categoryCode) {
    body.append("categoryCode", metadata.categoryCode);
  }
  if (metadata?.description) {
    body.append("description", metadata.description);
  }
  return requestJson<TemplateVersionView>("/api/templates", {
    method: "POST",
    body,
  });
}

export function getCurrentTemplateVersion(
  templateId: string
): Promise<TemplateVersionView> {
  return requestJson<TemplateVersionView>(
    `/api/templates/${encodeURIComponent(templateId)}/current`
  );
}

export function rescanTemplateVersion(
  templateVersionId: string
): Promise<TemplateVersionView> {
  return requestJson<TemplateVersionView>(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/rescan`,
    { method: "POST" }
  );
}

export async function getTemplateVersionFile(
  templateVersionId: string,
  fileName: string
): Promise<File> {
  const response = await fetch(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/file`
  );
  if (!response.ok) return parseError(response);
  return new File([await response.blob()], fileName, {
    type: DOCX_CONTENT_TYPE,
  });
}

export function listTemplateSegments(
  templateVersionId: string,
  bindingSetId?: string
): Promise<{ items: TemplateSegmentRecord[] }> {
  const url = new URL(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/segments`,
    window.location.origin
  );
  if (bindingSetId) url.searchParams.set("bindingSetId", bindingSetId);
  return requestJson<{ items: TemplateSegmentRecord[] }>(
    url
  );
}

export function listTemplateSegmentElements(
  segmentId: string
): Promise<TemplateElementRecord[]> {
  return requestJson<TemplateElementRecord[]>(
    `/api/template-segments/${encodeURIComponent(segmentId)}/elements`
  );
}

export async function getTemplateSegmentPreview(
  segmentId: string,
  fileName: string
): Promise<File> {
  const response = await fetch(
    `/api/template-segments/${encodeURIComponent(segmentId)}/preview`
  );
  if (!response.ok) return parseError(response);
  return new File([await response.blob()], fileName, {
    type: DOCX_CONTENT_TYPE,
  });
}

export function getTemplateSegmentOutline(
  templateVersionId: string
): Promise<TemplateSegmentOutline> {
  return requestJson<TemplateSegmentOutline>(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/segment-outline`
  );
}

export function insertTemplateSegmentBoundary(
  templateVersionId: string,
  request: {
    segmentKey: string;
    segmentName: string;
    startBlockId: string;
    endBlockId: string;
    expectedContentHash: string;
  }
): Promise<TemplateVersionView> {
  return requestJson<TemplateVersionView>(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/segment-boundaries`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function saveTemplateSegmentBoundaries(
  templateVersionId: string,
  request: {
    expectedContentHash: string;
    boundaries: TemplateSegmentBoundaryDraft[];
  }
): Promise<TemplateVersionView> {
  return requestJson<TemplateVersionView>(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}/segment-boundaries/batch`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function removeTemplateSegmentBoundary(
  templateVersionId: string,
  segmentKey: string,
  expectedContentHash: string
): Promise<TemplateVersionView> {
  const url = new URL(
    `/api/template-versions/${encodeURIComponent(templateVersionId)}` +
      `/segment-boundaries/${encodeURIComponent(segmentKey)}`,
    window.location.origin
  );
  url.searchParams.set("expectedContentHash", expectedContentHash);
  return requestJson<TemplateVersionView>(url, { method: "DELETE" });
}

export function getTemplateStudioWorkspace(
  templateId: string,
  params?: {
    versionId?: string;
    bindingSetId?: string;
  }
): Promise<TemplateStudioWorkspace> {
  const url = new URL(
    `/api/template-studio/${encodeURIComponent(templateId)}`,
    window.location.origin
  );
  if (params?.versionId) url.searchParams.set("versionId", params.versionId);
  if (params?.bindingSetId) {
    url.searchParams.set("bindingSetId", params.bindingSetId);
  }
  return requestJson<TemplateStudioWorkspace>(url);
}

export function listPublishedTemplates(): Promise<PublishedTemplateList> {
  return requestJson<PublishedTemplateList>("/api/template-releases");
}

export function hydrateTemplateResponse(
  view: TemplateVersionView,
  bindingItems: BindingItemRecord[] = []
): TemplateResponse {
  const elementsByLocator = new Map<string, TemplateElementRecord>();
  for (const element of view.elements) {
    const locatorId =
      typeof element.locator?.locatorId === "string"
        ? element.locator.locatorId
        : null;
    if (locatorId) elementsByLocator.set(locatorId, element);
  }
  const bindingsByElement = new Map(
    bindingItems.map((binding) => [binding.templateElementId, binding])
  );
  const scan = view.parseResult.scanResult;
  const mockItems = scan.mockItems.filter((item) =>
    elementsByLocator.has(item.locatorId)
  ).map((item) => {
    const element = elementsByLocator.get(item.locatorId);
    const binding = element ? bindingsByElement.get(element.id) : undefined;
    return {
      ...item,
      templateElementId: element?.id,
      isBound: Boolean(binding),
      boundDataPath: binding?.sourcePath || null,
      boundDataType: null,
    };
  });
  const charts = scan.charts.filter((chart) =>
    elementsByLocator.has(chart.locatorId)
  ).map((chart) => {
    const element = elementsByLocator.get(chart.locatorId);
    const binding = element ? bindingsByElement.get(element.id) : undefined;
    return {
      ...chart,
      templateElementId: element?.id,
      isBound: Boolean(binding),
      boundDataPath: binding?.sourcePath || null,
      boundDataType: null,
      chartMapping: readWrappedConfig<ChartBindingMappingResponse>(
        binding?.formatConfig,
        "chartMapping"
      ),
    };
  });
  const tables = view.elements
    .filter((element) => element.elementType === "TABLE")
    .map((element) => {
      const locatorId = typeof element.locator.locatorId === "string"
        ? element.locator.locatorId
        : "";
      const schema = asRecord(element.bindingSchema);
      const binding = bindingsByElement.get(element.id);
      const configuredColumns = readArray(schema.columns)
        .map((value) => asRecord(value))
        .map((column) => ({
          columnIndex: numberValue(column.columnIndex),
          header: stringValue(column.header),
          sourceField: stringValue(column.sourceField),
          fallbackValue: nullableString(column.fallbackValue),
        }))
        .filter((column) => column.columnIndex >= 0 && column.sourceField.length > 0);
      const tableColumnsSource = readArray(schema.tableColumns);
      let tableColumns = (tableColumnsSource.length
        ? tableColumnsSource
        : configuredColumns
      )
        .map((value) => asRecord(value))
        .map((column) => ({
          columnIndex: numberValue(column.columnIndex),
          header: stringValue(column.header),
          suggestedField:
            nullableString(column.suggestedField) ||
            nullableString(column.sourceField),
        }))
        .filter((column) => column.columnIndex >= 0)
        .sort((left, right) => left.columnIndex - right.columnIndex);
      if (tableColumns.length === 0) {
        tableColumns = stringValue(element.locator.headerSignature)
          .split("|")
          .filter((header) => header.length > 0)
          .map((header, columnIndex) => ({
            columnIndex,
            header,
            suggestedField: null,
          }));
      }
      const defaultMapping = {
        headerRowCount: positiveNumber(schema.headerRowCount, 1),
        columns: configuredColumns,
        filterField: nullableString(schema.filterField),
        filterValue: nullableString(schema.filterValue),
      };
      return {
        templateElementId: element.id,
        locatorId,
        title: element.displayName || `表格 ${numberValue(element.locator.tableIndex) + 1}`,
        locator: {
          partKey: stringValue(element.locator.partKey),
          tableIndex: numberValue(element.locator.tableIndex),
          firstParagraphIndex: numberValue(element.locator.firstParagraphIndex),
          headerSignature: stringValue(element.locator.headerSignature),
        },
        columns: tableColumns,
        headerRowCount: positiveNumber(schema.headerRowCount, 1),
        templateRowCount: positiveNumber(schema.templateRowCount, 2),
        suggestedSourcePath: nullableString(schema.suggestedSourcePath),
        contextLabel: nullableString(schema.contextLabel),
        defaultMapping,
        tableMapping: readWrappedConfig<TableBindingMapping>(
          binding?.formatConfig,
          "tableMapping"
        ),
        isBindable: element.parseStatus.toUpperCase() === "VALID",
        isBound: Boolean(binding),
        boundDataPath: binding?.sourcePath || null,
        parseMessage: element.parseMessage,
        segmentLocalOrder: element.segmentLocalOrder,
      };
    })
    .filter((table) => table.locatorId.length > 0);
  return {
    templateId: view.template.id,
    fileName: view.file.originalName,
    contentHash: scan.contentHash,
    mockItemCount: mockItems.length,
    chartCount: charts.length,
    tableCount: tables.length,
    bindingCount: bindingItems.length,
    mockItems,
    charts,
    tables,
    preview: scan.preview,
    importSummary: view.parseResult.importSummary || {
      textBindingsRestored: 0,
      chartBindingsRestored: 0,
      tableBindingsRestored: 0,
      unresolvedPlaceholders: [],
      warnings: [],
    },
    createdAt: view.template.createdAt,
    updatedAt: view.template.updatedAt,
  };
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function readArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

function stringValue(value: unknown): string {
  return typeof value === "string" ? value : "";
}

function nullableString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

function numberValue(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) ? value : -1;
}

function positiveNumber(value: unknown, fallback: number): number {
  const parsed = numberValue(value);
  return parsed > 0 ? parsed : fallback;
}

function readWrappedConfig<T>(value: unknown, key: string): T | null {
  const root = asRecord(value);
  const candidate = key in root ? root[key] : value;
  return candidate && typeof candidate === "object" && !Array.isArray(candidate)
    ? candidate as T
    : null;
}
