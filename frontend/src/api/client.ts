// Compatibility barrel: existing imports from "./api/client" remain valid.
export * from "./templatesApi";
export * from "./projectsApi";
export * from "./dataSourcesApi";
export * from "./bindingsApi";
export * from "./reportsApi";
export type { PagedResponse } from "./httpClient";
export type { DataFieldNode, TemplateResponse } from "./types";
