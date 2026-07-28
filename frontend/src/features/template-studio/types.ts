export interface TemplateStudioContext {
  templateId: string;
  versionId: string;
  projectId: string;
  chapterId: string;
  dataSourceId: string;
  segmentId: string;
}

export type TemplateStudioContextPatch = Partial<TemplateStudioContext>;
