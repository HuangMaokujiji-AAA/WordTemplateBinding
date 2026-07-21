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
  | "Array";

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
  locatorId: string;
  mockValue: string;
  dataType: MockDataType;
  locator: TextLocator;
  paragraphText: string;
  previewParagraphIndex: number;
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
  createdAt: string;
  updatedAt: string;
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
