import type {
  DataFieldNode,
  DataSchemaResponse,
  TemplateResponse,
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
  dataPath: string
): Promise<{ success: boolean }> {
  return requestJson<{ success: boolean }>("/api/bindings", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ templateId, locatorId, dataPath }),
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

export async function downloadReport(templateId: string): Promise<string> {
  return downloadDocx(
    "/api/reports/generate",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ templateId }),
    },
    "report_generated.docx"
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
