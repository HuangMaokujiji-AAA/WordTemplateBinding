import type { BindingItemRecord, BindingSetRecord } from "./types";
import { requestJson } from "./httpClient";

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

