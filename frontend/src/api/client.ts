// Compatibility barrel: existing imports from "./api/client" remain valid.
import { requestJson } from "./httpClient";

export * from "./templatesApi";
export * from "./projectsApi";
export * from "./dataSourcesApi";
export * from "./bindingsApi";
export * from "./reportsApi";
export type { PagedResponse } from "./httpClient";
export type { DataFieldNode, TemplateResponse } from "./types";
export interface WpsStatus {
  isWindows: boolean;
  isAvailable: boolean;
  progId?: string | null;
  message?: string | null;
}

export interface AnchorInfo {
  anchorName: string;
  pageNumber: number;
  bounds: { x: number; y: number; width: number; height: number };
  targetType: "placeholder" | "table" | "chart";
  targetId: string;
  boundDataPath?: string;
  displayText?: string;
}

export async function getWpsStatus(): Promise<WpsStatus> {
  return requestJson<WpsStatus>("/api/wps/status");
}
