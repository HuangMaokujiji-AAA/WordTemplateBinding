import type {
  DataFieldNode,
  DataFieldRecord,
  DataSchemaResponse,
  DataSourceRecord,
  HigherEducationDataSourceResult,
  HigherEducationSchool,
} from "./types";
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

export function listHigherEducationYears(): Promise<string[]> {
  return requestJson<string[]>("/api/higher-education/years");
}

export function listHigherEducationSchools(
  collectionYear: string
): Promise<HigherEducationSchool[]> {
  const url = new URL("/api/higher-education/schools", window.location.origin);
  url.searchParams.set("collectionYear", collectionYear);
  return requestJson<HigherEducationSchool[]>(url);
}

export function createHigherEducationDataSource(input: {
  projectId: string;
  collectionYear: string;
  schoolCode: string;
  sourceCode?: string;
  sourceName?: string;
}): Promise<HigherEducationDataSourceResult> {
  return requestJson<HigherEducationDataSourceResult>(
    "/api/data-sources/higher-education",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
    }
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
  const normalizedQuery = query.trim();
  const path = `/api/data-sources/${encodeURIComponent(dataSourceId)}/schema`;
  const url = normalizedQuery
    ? `${path}?query=${encodeURIComponent(normalizedQuery)}`
    : path;

  if (normalizedQuery) {
    const result = await requestJson<{
      nodes: DataFieldNode[];
      matchCount: number;
      isTruncated: boolean;
    }>(url);
    return {
      query: normalizedQuery,
      totalLeafCount: result.matchCount,
      matchCount: result.matchCount,
      isTruncated: result.isTruncated,
      nodes: result.nodes,
    };
  }

  const nodes = await requestJson<DataFieldNode[]>(url);
  const totalLeafCount = countLeaves(nodes);
  return {
    query: null,
    totalLeafCount,
    matchCount: totalLeafCount,
    isTruncated: false,
    nodes,
  };
}

function countLeaves(nodes: DataFieldNode[]): number {
  return nodes.reduce(
    (total, node) =>
      total + (node.isLeaf ? 1 : countLeaves(node.children)),
    0
  );
}
