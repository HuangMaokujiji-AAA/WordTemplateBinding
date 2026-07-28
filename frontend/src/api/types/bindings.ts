export interface BindingSetRecord {
  id: string;
  chapterId: string;
  versionNo: number;
  templateVersionId: string;
  bindingStatus: string;
  validationStatus: string;
  validationResult: unknown;
}

export interface BindingItemRecord {
  id: string;
  bindingSetId: string;
  templateElementId: string;
  targetProperty: string;
  sourceKind: string;
  dataSourceId: string | null;
  sourcePath: string | null;
  transformConfig: unknown;
  formatConfig: unknown;
  fallbackValue: unknown;
  isRequired: boolean;
}

