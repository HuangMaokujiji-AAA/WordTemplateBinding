export type MockDataType = "Decimal" | "Integer" | "String";

export type DocumentPartKind =
  | "MainDocument"
  | "Header"
  | "Footer"
  | "Footnote"
  | "Endnote"
  | "TextBox";

export type DataValueType =
  | "String"
  | "Integer"
  | "Decimal"
  | "Boolean"
  | "Date"
  | "Array"
  | "Object"
  | "Binary";

export type BindingTargetKind = "Text" | "Chart";

export interface TextLocator {
  partKind: DocumentPartKind;
  partKey: string;
  paragraphIndex: number;
  startOffset: number;
  length: number;
  occurrenceIndex: number;
  originalValue: string;
  contextHash: string;
}

export interface MockItem {
  templateElementId?: string;
  locatorId: string;
  mockValue: string;
  dataType: MockDataType;
  locator: TextLocator;
  paragraphText: string;
  previewParagraphIndex: number;
  placeholderCandidatePath: string | null;
  isBound: boolean;
  boundDataPath: string | null;
  boundDataType: DataValueType | null;
}

export interface ChartLocator {
  partKey: string;
  relationshipId: string;
  documentOrder: number;
}

export interface ChartSeriesItem {
  seriesIndex: number;
  name: string;
  values: Array<number | null>;
}

export interface ChartItem {
  templateElementId?: string;
  locatorId: string;
  locator: ChartLocator;
  chartType: string;
  title: string;
  categories: string[];
  series: ChartSeriesItem[];
  isBindable: boolean;
  isBound: boolean;
  boundDataPath: string | null;
  boundDataType: DataValueType | null;
  analysis: ChartAnalysisSnapshot | null;
  dataDefinition: ChartDataDefinition | null;
  chartMapping: ChartBindingMappingResponse | null;
}

export interface ChartDataDefinition {
  schemaVersion: string;
  locatorId: string;
  partKey: string;
  relationshipId: string;
  documentOrder: number;
  chartType: string;
  dataMode: string;
  category: ChartCategoryDef;
  series: ChartSeriesDef[];
  currentData: ChartDataRowSnapshot[];
  writeCapability: string;
  diagnostics: ChartDiagnosticItem[];
}

export interface ChartCategoryDef {
  name: string;
  formula: string | null;
  sheetName: string | null;
  startCell: string | null;
  endCell: string | null;
  values: Array<string | null>;
}

export interface ChartSeriesDef {
  seriesIndex: number;
  seriesKey: string;
  name: string;
  nameFormula: string | null;
  nameCell: string | null;
  valueFormula: string | null;
  valueStartCell: string | null;
  valueEndCell: string | null;
  values: Array<number | null>;
  numberFormat: string | null;
}

export interface ChartBindingMappingResponse {
  mode: string;
  categoryField: string;
  seriesMappings: ChartSeriesFieldMappingResponse[];
}

export interface ChartSeriesFieldMappingResponse {
  seriesIndex: number;
  seriesKey: string;
  templateSeriesName: string;
  valueField: string;
  seriesNameField: string | null;
}

export interface ChartAnalysisSnapshot {
  schemaVersion: string;
  identity: ChartIdentitySnapshot;
  source: ChartSourceSnapshot;
  chart: ChartDefinitionSnapshot;
  plotGroups: ChartPlotGroupSnapshot[];
  axes: ChartAxisSnapshot[];
  categories: ChartCategorySnapshot[];
  series: ChartSeriesSnapshot[];
  dataTable: ChartDataTableSnapshot;
  bindingContract: ChartBindingContract;
  diagnostics: ChartAnalysisDiagnostics;
}

export interface ChartIdentitySnapshot {
  locatorId: string;
  partKey: string;
  relationshipId: string;
  documentOrder: number;
}

export interface ChartSourceSnapshot {
  chartPartPath: string;
  chartRelationshipPartPath: string | null;
  externalDataRelationshipId: string | null;
  embeddedWorkbookPath: string | null;
  embeddedWorkbookDetected: boolean;
  formulas: ChartFormulaSnapshot[];
  caches: ChartCacheSummary[];
}

export interface ChartFormulaSnapshot {
  role: string;
  seriesIndex: number | null;
  formula: string;
  sheetName: string | null;
  rangeAddress: string | null;
}

export interface ChartCacheSummary {
  location: string;
  pointCount: number;
  hasSparsePoints: boolean;
}

export interface ChartDefinitionSnapshot {
  type: string;
  typeLabel: string;
  title: string | null;
  supportedForBinding: boolean;
  widthEmu: number;
  heightEmu: number;
}

export interface ChartPlotGroupSnapshot {
  id: string;
  order: number;
  type: string;
  grouping: string | null;
  barDirection: string | null;
  seriesKeys: string[];
  axisIds: string[];
}

export interface ChartAxisSnapshot {
  id: string;
  type: string;
  role: string;
  position: string | null;
  title: string | null;
  min: number | null;
  max: number | null;
  majorUnit: number | null;
  minorUnit: number | null;
  numberFormat: string | null;
  reversed: boolean;
  visible: boolean;
  crossAxisId: string | null;
}

export interface ChartCategorySnapshot {
  index: number;
  value: string | null;
  displayValue: string;
  levels: string[];
  sourceFormula: string | null;
  numberFormat: string | null;
  isMissing: boolean;
}

export interface ChartSeriesSnapshot {
  key: string;
  seriesIndex: number;
  order: number;
  name: string;
  chartType: string;
  plotGroupId: string;
  axisRole: string;
  axisIds: string[];
  nameFormula: string | null;
  categoryFormula: string | null;
  valueFormula: string | null;
  xValueFormula: string | null;
  yValueFormula: string | null;
  bubbleSizeFormula: string | null;
  values: ChartDataPointSnapshot[];
  xValues: ChartDataPointSnapshot[];
  yValues: ChartDataPointSnapshot[];
  bubbleSizes: ChartDataPointSnapshot[];
  numberFormat: string | null;
  dataLabelFormula: string | null;
}

export interface ChartDataPointSnapshot {
  index: number;
  value: unknown;
  displayValue: string | null;
  numberFormat: string | null;
  isMissing: boolean;
}

export interface ChartDataTableSnapshot {
  orientation: string;
  columns: ChartDataColumnSnapshot[];
  rows: ChartDataRowSnapshot[];
  rowCount: number;
  columnCount: number;
}

export interface ChartDataColumnSnapshot {
  key: string;
  label: string;
  role: string;
  valueType: string;
  seriesKey: string | null;
}

export interface ChartDataRowSnapshot {
  index: number;
  cells: Record<string, unknown>;
  missing: Record<string, boolean>;
}

export interface ChartBindingContract {
  mode: string;
  categoryProperty: string;
  seriesFields: ChartBindingSeriesField[];
  sampleReplacementPayload: Record<string, unknown>[];
  reportRequestExample: ChartReportRequestExample;
}

export interface ChartBindingSeriesField {
  seriesKey: string;
  seriesIndex: number;
  originalName: string;
  payloadProperty: string;
  valueType: string;
  required: boolean;
}

export interface ChartReportRequestExample {
  templateId: string;
  boundDataPath: string | null;
  suggestedDataPath: string;
  values: Record<string, unknown>;
}

export interface ChartAnalysisDiagnostics {
  hasErrors: boolean;
  hasWarnings: boolean;
  completenessScore: number;
  items: ChartDiagnosticItem[];
}

export interface ChartDiagnosticItem {
  code: string;
  level: string;
  message: string;
  path: string | null;
  seriesIndex: number | null;
  recoverable: boolean;
}

export interface PreviewHighlight {
  locatorId: string;
  startOffset: number;
  length: number;
  mockValue: string;
}

export interface PreviewParagraph {
  paragraphIndex: number;
  text: string;
  highlights: PreviewHighlight[];
}

export interface TemplateResponse {
  templateId: string;
  fileName: string;
  contentHash: string;
  mockItemCount: number;
  chartCount: number;
  bindingCount: number;
  mockItems: MockItem[];
  charts: ChartItem[];
  preview: { paragraphs: PreviewParagraph[] };
  importSummary: TemplateImportSummary;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateImportSummary {
  textBindingsRestored: number;
  chartBindingsRestored: number;
  unresolvedPlaceholders: string[];
  warnings: string[];
}

export interface DataFieldNode {
  name: string;
  path: string;
  type: DataValueType;
  isCollection: boolean;
  isLeaf: boolean;
  isBindable: boolean;
  children: DataFieldNode[];
}

export interface DataSchemaResponse {
  query: string | null;
  totalLeafCount: number;
  matchCount: number;
  isTruncated: boolean;
  nodes: DataFieldNode[];
}

export interface TemplateRecord {
  id: string;
  templateCode: string;
  templateName: string;
  templateType: string;
  categoryCode: string | null;
  templateStatus: string;
  description: string | null;
  currentVersionNo: number;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateElementRecord {
  id: string;
  templateVersionId: string;
  elementKey: string;
  elementType: "TEXT" | "CHART" | string;
  locatorType: string;
  displayName: string | null;
  locator: Record<string, unknown> & { locatorId?: string };
  bindingSchema: unknown;
  defaultValue: unknown;
  isRequired: boolean;
  sortNo: number;
  parseStatus: string;
  parseMessage: string | null;
}

export interface TemplateVersionView {
  template: TemplateRecord;
  version: {
    id: string;
    templateId: string;
    versionNo: number;
    fileObjectId: string;
    versionStatus: string;
    elementCount: number;
    createdAt: string;
  };
  file: {
    id: string;
    originalName: string;
    mimeType: string;
    fileSize: number;
    sha256: string | null;
    objectStatus: string;
  };
  elements: TemplateElementRecord[];
  parseResult: {
    scanResult: {
      contentHash: string;
      mockItems: MockItem[];
      charts: ChartItem[];
      preview: { paragraphs: PreviewParagraph[] };
      warnings?: Array<{ code: string; message: string }>;
    };
    importSummary: TemplateImportSummary;
    warnings: Array<{ code: string; message: string }>;
  };
}

export interface ProjectRecord {
  id: string;
  projectCode: string;
  projectName: string;
  description: string | null;
  projectStatus: string;
}

export interface ChapterRecord {
  id: string;
  projectId: string;
  parentId: string | null;
  chapterCode: string;
  title: string;
  levelNo: number;
  sortKey: number;
  workflowStatus: string;
  isEnabled: boolean;
}

export interface DataSourceRecord {
  id: string;
  projectId: string;
  connectionId: string;
  sourceCode: string;
  sourceName: string;
  sourceType: string;
  sourceStatus: string;
  schemaName: string;
  objectType: string;
  objectName: string;
}

export interface DataFieldRecord {
  id: string;
  snapshotId: string;
  fieldPath: string;
  fieldName: string;
  comment: string | null;
  dataType: DataValueType;
  isArray: boolean;
  isNullable: boolean;
  isBindable: boolean;
  sampleValue: unknown;
  displayOrder: number;
}

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
