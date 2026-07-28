import type { ChapterRecord, ProjectRecord } from "./types";
import type { PagedResponse } from "./httpClient";
import { requestJson } from "./httpClient";

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

