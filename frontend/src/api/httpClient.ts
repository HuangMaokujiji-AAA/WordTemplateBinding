export const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

export async function parseError(response: Response): Promise<never> {
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

export async function requestJson<T>(
  input: RequestInfo | URL,
  init?: RequestInit
): Promise<T> {
  const response = await fetch(input, init);
  if (!response.ok) {
    return parseError(response);
  }

  return (await response.json()) as T;
}

export async function downloadDocx(
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

export function getDownloadFileName(
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

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

