import type { DataFieldNode, DataFieldRecord, DataSchemaResponse, DataSourceRecord } from "./types";
import { requestJson } from "./httpClient";

export function getSchema(query = ""): Promise<DataSchemaResponse> {
  const url = new URL("/api/data-schema", window.location.origin);
  if (query.trim()) {
    url.searchParams.set("query", query.trim());
  }

  return requestJson<DataSchemaResponse>(url);
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

