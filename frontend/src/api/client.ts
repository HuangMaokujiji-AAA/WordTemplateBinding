import type {
  BindingItemRecord,
  BindingSetRecord,
  ChapterRecord,
  DataFieldRecord,
  DataFieldNode,
  DataSchemaResponse,
  DataSourceRecord,
  ProjectRecord,
  TemplateElementRecord,
  TemplateRecord,
  TemplateResponse,
  TemplateVersionView,
} from "./types";

const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

async function parseError(response: Response): Promise<never> {
  let message = `请求失败（HTTP ${response.status}）`;

  try {
    const problem = (await response.json()) as {
      detail?: string;
      title?: string;
    };
    message = problem.detail || problem.title || message;
  } catch {
    // Keep the generic message when the response is not ProblemDetails JSON.
  }

  throw new Error(message);
}

async function requestJson<T>(
  input: RequestInfo | URL,
  init?: RequestInit
): Promise<T> {
  const response = await fetch(input, init);
  if (!response.ok) {
    return parseError(response);
  }

  return (await response.json()) as T;
}

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

export function getSchema(query = ""): Promise<DataSchemaResponse> {
  const url = new URL("/api/data-schema", window.location.origin);
  if (query.trim()) {
    url.searchParams.set("query", query.trim());
  }

  return requestJson<DataSchemaResponse>(url);
}

export function upsertBinding(
  templateId: string,
  locatorId: string,
  dataPath: string,
  chartMapping?: {
    mode: string;
    categoryField: string;
    seriesMappings: Array<{
      seriesIndex: number;
      seriesKey: string;
      valueField: string;
      seriesNameField?: string | null;
    }>;
  } | null
): Promise<{ success: boolean }> {
  return requestJson<{ success: boolean }>("/api/bindings", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(
      chartMapping
        ? { templateId, locatorId, dataPath, chartMapping }
        : { templateId, locatorId, dataPath }
    ),
  });
}

export function deleteBinding(
  templateId: string,
  locatorId: string
): Promise<{ success: boolean; deleted: boolean }> {
  return requestJson<{ success: boolean; deleted: boolean }>(
    `/api/templates/${encodeURIComponent(templateId)}/bindings/${encodeURIComponent(locatorId)}`,
    { method: "DELETE" }
  );
}

export async function downloadReport(
  templateId: string,
  values?: Record<string, unknown>
): Promise<string> {
  return downloadDocx(
    "/api/reports/generate",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values ? { templateId, values } : { templateId }),
    },
    "report_generated.docx"
  );
}

export type { TemplateResponse };

export async function downloadReusableTemplate(
  templateId: string
): Promise<string> {
  return downloadDocx(
    `/api/templates/${encodeURIComponent(templateId)}/export-reusable`,
    { method: "POST" },
    "template-template.docx"
  );
}

async function downloadDocx(
  input: RequestInfo | URL,
  init: RequestInit,
  fallbackFileName: string
): Promise<string> {
  const response = await fetch(input, init);

  if (!response.ok) {
    return parseError(response);
  }

  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes(DOCX_CONTENT_TYPE)) {
    throw new Error("服务器没有返回有效的 DOCX 文件。");
  }

  const blob = await response.blob();
  const fileName = getDownloadFileName(
    response.headers.get("content-disposition"),
    fallbackFileName
  );
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  try {
    anchor.href = objectUrl;
    anchor.download = fileName;
    document.body.append(anchor);
    anchor.click();
  } finally {
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
  }
  return fileName;
}

function getDownloadFileName(
  contentDisposition: string | null,
  fallbackFileName: string
): string {
  if (!contentDisposition) {
    return fallbackFileName;
  }

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8Match) {
    try {
      return decodeURIComponent(utf8Match[1].replace(/^"|"$/g, ""));
    } catch {
      return fallbackFileName;
    }
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plainMatch?.[1] || fallbackFileName;
}

export type { DataFieldNode };

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
  return requestJson<TemplateVersionView[]>(
    `/api/templates/${encodeURIComponent(templateId)}/versions`
  );
}

export async function uploadPersistentTemplate(
  file: File,
  templateId?: string | null
): Promise<TemplateVersionView> {
  const body = new FormData();
  body.append("file", file);
  if (templateId) {
    return requestJson<TemplateVersionView>(
      `/api/templates/${encodeURIComponent(templateId)}/versions`,
      { method: "POST", body }
    );
  }

  body.append("templateCode", `TPL_${Date.now()}`);
  body.append("templateName", file.name.replace(/\.docx$/i, ""));
  body.append("templateType", "SECTION");
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

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export function listProjects(params?: {
  query?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}): Promise<PagedResponse<ProjectRecord>> {
  const url = new URL("/api/projects", window.location.origin);
  if (params?.query) url.searchParams.set("query", params.query);
  if (params?.status) url.searchParams.set("status", params.status);
  url.searchParams.set("page", String(params?.page || 1));
  url.searchParams.set("pageSize", String(params?.pageSize || 20));
  return requestJson<PagedResponse<ProjectRecord>>(url);
}

export function getProject(projectId: string): Promise<ProjectRecord> {
  return requestJson<ProjectRecord>(
    `/api/projects/${encodeURIComponent(projectId)}`
  );
}

export function createProject(request: {
  projectCode: string;
  projectName: string;
  description?: string;
}): Promise<ProjectRecord> {
  return requestJson<ProjectRecord>("/api/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
}

export function updateProject(
  projectId: string,
  request: {
    projectName: string;
    description?: string | null;
    projectStatus?: string | null;
    rowVersion: number;
  }
): Promise<ProjectRecord> {
  return requestJson<ProjectRecord>(
    `/api/projects/${encodeURIComponent(projectId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function archiveProject(
  projectId: string,
  rowVersion: number
): Promise<ProjectRecord> {
  return requestJson<ProjectRecord>(
    `/api/projects/${encodeURIComponent(projectId)}`,
    {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rowVersion }),
    }
  );
}

export function restoreProject(
  projectId: string,
  rowVersion: number
): Promise<ProjectRecord> {
  return requestJson<ProjectRecord>(
    `/api/projects/${encodeURIComponent(projectId)}/restore`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rowVersion }),
    }
  );
}

export function initializeDevDataSource(
  projectId: string,
  forceRefresh = false
): Promise<{
  projectId: string;
  dataSourceId: string;
  snapshotId: string;
  fieldCount: number;
  created: boolean;
  refreshed: boolean;
}> {
  return requestJson(
    `/api/projects/${encodeURIComponent(projectId)}/development-data-source/initialize`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ forceRefresh }),
    }
  );
}

export function listChapters(projectId: string): Promise<ChapterRecord[]> {
  return requestJson<ChapterRecord[]>(
    `/api/projects/${encodeURIComponent(projectId)}/chapters`
  );
}

export function createChapter(
  projectId: string,
  request: {
    chapterCode: string;
    title: string;
    parentId?: string | null;
    sortKey?: number;
  }
): Promise<ChapterRecord> {
  return requestJson<ChapterRecord>(
    `/api/projects/${encodeURIComponent(projectId)}/chapters`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function getChapter(chapterId: string): Promise<ChapterRecord> {
  return requestJson<ChapterRecord>(
    `/api/chapters/${encodeURIComponent(chapterId)}`
  );
}

export function updateChapter(
  chapterId: string,
  request: {
    chapterCode: string;
    title: string;
    rowVersion: number;
  }
): Promise<ChapterRecord> {
  return requestJson<ChapterRecord>(
    `/api/chapters/${encodeURIComponent(chapterId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    }
  );
}

export function deleteChapter(
  chapterId: string,
  rowVersion: number
): Promise<{ deleted: boolean }> {
  return requestJson<{ deleted: boolean }>(
    `/api/chapters/${encodeURIComponent(chapterId)}`,
    {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rowVersion }),
    }
  );
}

export function reorderChapters(
  projectId: string,
  items: Array<{ chapterId: string; parentId: string | null; sortKey: number }>
): Promise<{ reordered: number }> {
  return requestJson<{ reordered: number }>(
    `/api/projects/${encodeURIComponent(projectId)}/chapters/order`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(items),
    }
  );
}

export function listDataSources(projectId: string): Promise<DataSourceRecord[]> {
  const url = new URL("/api/data-sources", window.location.origin);
  url.searchParams.set("projectId", projectId);
  return requestJson<DataSourceRecord[]>(url);
}

export function refreshDataSource(
  dataSourceId: string
): Promise<{ id: string; snapshotStatus: string }> {
  return requestJson<{ id: string; snapshotStatus: string }>(
    `/api/data-sources/${encodeURIComponent(dataSourceId)}/refresh`,
    { method: "POST" }
  );
}

export function listDataFields(
  dataSourceId: string,
  query = ""
): Promise<DataFieldRecord[]> {
  const url = new URL(
    `/api/data-sources/${encodeURIComponent(dataSourceId)}/fields`,
    window.location.origin
  );
  url.searchParams.set("limit", "1000");
  if (query.trim()) url.searchParams.set("query", query.trim());
  return requestJson<DataFieldRecord[]>(url);
}

export async function getPersistentSchema(
  dataSourceId: string,
  query = ""
): Promise<DataSchemaResponse> {
  const fields = await listDataFields(dataSourceId, query);
  const nodes: DataFieldNode[] = fields.map((field) => ({
    name: field.comment || field.fieldName,
    path: field.fieldPath,
    type: field.dataType,
    isCollection: field.isArray,
    isLeaf: true,
    isBindable: field.isBindable,
    children: [],
  }));
  return {
    query: query.trim() || null,
    totalLeafCount: fields.length,
    matchCount: fields.length,
    isTruncated: false,
    nodes,
  };
}

export function getOrCreateBindingSet(
  chapterId: string,
  templateVersionId: string
): Promise<BindingSetRecord> {
  return requestJson<BindingSetRecord>("/api/binding-sets", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ chapterId, templateVersionId }),
  });
}

export function listBindingItems(
  bindingSetId: string
): Promise<BindingItemRecord[]> {
  return requestJson<BindingItemRecord[]>(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/items`
  );
}

export function upsertPersistentBinding(
  bindingSetId: string,
  templateElementId: string,
  dataSourceId: string,
  sourcePath: string,
  formatConfigJson?: string | null
): Promise<BindingItemRecord> {
  return requestJson<BindingItemRecord>(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/items/${encodeURIComponent(templateElementId)}`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        dataSourceId,
        sourcePath,
        targetProperty: "$",
        sourceKind: "DATA_SOURCE",
        formatConfigJson: formatConfigJson || null,
      }),
    }
  );
}

export function deletePersistentBinding(
  bindingSetId: string,
  templateElementId: string
): Promise<{ deleted: boolean }> {
  return requestJson<{ deleted: boolean }>(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/items/${encodeURIComponent(templateElementId)}`,
    { method: "DELETE" }
  );
}

export function validateBindingSet(bindingSetId: string): Promise<{
  status: string;
  summary: Record<string, number>;
  items: Array<{ code: string; level: string; message: string }>;
}> {
  return requestJson(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/validate`,
    { method: "POST" }
  );
}

export function getBindingPreview(
  bindingSetId: string,
  templateElementId: string
): Promise<{
  formattedValue: string | null;
  sourcePath: string;
  dataType: string;
}> {
  return requestJson(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/preview/${encodeURIComponent(templateElementId)}`
  );
}

export function getBindingSuggestions(
  templateElementId: string,
  dataSourceId: string
): Promise<Array<{ fieldPath: string; score: number; reasons: string[] }>> {
  const url = new URL(
    `/api/template-elements/${encodeURIComponent(templateElementId)}/suggestions`,
    window.location.origin
  );
  url.searchParams.set("dataSourceId", dataSourceId);
  return requestJson(url);
}

export async function downloadBindingSetReport(
  bindingSetId: string
): Promise<string> {
  return downloadDocx(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/reports`,
    { method: "POST" },
    "report_generated.docx"
  );
}

export async function downloadBindingSetReusableTemplate(
  bindingSetId: string
): Promise<string> {
  return downloadDocx(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/export-reusable`,
    { method: "POST" },
    "template-template.docx"
  );
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
  const mockItems = scan.mockItems.map((item) => {
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
  const charts = scan.charts.map((chart) => {
    const element = elementsByLocator.get(chart.locatorId);
    const binding = element ? bindingsByElement.get(element.id) : undefined;
    return {
      ...chart,
      templateElementId: element?.id,
      isBound: Boolean(binding),
      boundDataPath: binding?.sourcePath || null,
      boundDataType: null,
      chartMapping: null,
    };
  });
  return {
    templateId: view.template.id,
    fileName: view.file.originalName,
    contentHash: scan.contentHash,
    mockItemCount: mockItems.length,
    chartCount: charts.length,
    bindingCount: bindingItems.length,
    mockItems,
    charts,
    preview: scan.preview,
    importSummary: view.parseResult.importSummary || {
      textBindingsRestored: 0,
      chartBindingsRestored: 0,
      unresolvedPlaceholders: [],
      warnings: [],
    },
    createdAt: view.template.createdAt,
    updatedAt: view.template.updatedAt,
  };
}
