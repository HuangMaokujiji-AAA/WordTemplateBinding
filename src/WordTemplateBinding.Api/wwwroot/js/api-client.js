const docxContentType =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

async function parseError(response) {
  let message = `请求失败（HTTP ${response.status}）`;
  try {
    const problem = await response.json();
    message = problem.detail || problem.title || message;
  } catch {
    // 非 JSON 错误响应保持通用提示，避免向页面注入服务端原始内容。
  }
  throw new Error(message);
}

async function requestJson(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    await parseError(response);
  }
  return response.json();
}

export async function uploadTemplate(file) {
  const formData = new FormData();
  formData.append("file", file);
  return requestJson("/api/templates/upload", {
    method: "POST",
    body: formData,
  });
}

export function getTemplate(templateId) {
  return requestJson(`/api/templates/${encodeURIComponent(templateId)}`);
}

export function rescanTemplate(templateId) {
  return requestJson(`/api/templates/${encodeURIComponent(templateId)}/rescan`, {
    method: "POST",
  });
}

export function getSchema(query = "") {
  const url = new URL("/api/data-schema", window.location.origin);
  if (query.trim()) {
    url.searchParams.set("query", query.trim());
  }
  return requestJson(url);
}

export function upsertBinding(templateId, locatorId, dataPath) {
  return requestJson("/api/bindings", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ templateId, locatorId, dataPath }),
  });
}

export function deleteBinding(templateId, locatorId) {
  return requestJson(
    `/api/templates/${encodeURIComponent(templateId)}/bindings/${encodeURIComponent(locatorId)}`,
    { method: "DELETE" },
  );
}

export async function downloadReport(templateId, values) {
  const body = { templateId };
  if (values) {
    body.values = values;
  }

  const response = await fetch("/api/reports/generate", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    await parseError(response);
  }

  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes(docxContentType)) {
    throw new Error("服务器没有返回有效的 DOCX 文件。");
  }

  const blob = await response.blob();
  const fileName = getDownloadFileName(
    response.headers.get("content-disposition"),
  );
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = objectUrl;
  anchor.download = fileName;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(objectUrl);
  return fileName;
}

function getDownloadFileName(contentDisposition) {
  if (!contentDisposition) {
    return "report_generated.docx";
  }

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8Match) {
    return decodeURIComponent(utf8Match[1]);
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plainMatch ? plainMatch[1] : "report_generated.docx";
}
