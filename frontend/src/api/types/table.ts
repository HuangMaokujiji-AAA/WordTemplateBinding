export interface TableLocator {
  partKey: string;
  tableIndex: number;
  firstParagraphIndex: number;
  headerSignature: string;
}

export interface TableColumnDefinition {
  columnIndex: number;
  header: string;
  suggestedField: string | null;
}

export interface TableColumnBinding {
  columnIndex: number;
  header: string | null;
  sourceField: string;
  fallbackValue: string | null;
}

export interface TableBindingMapping {
  headerRowCount: number;
  columns: TableColumnBinding[];
  filterField: string | null;
  filterValue: string | null;
}

export interface TableItem {
  templateElementId: string;
  locatorId: string;
  title: string;
  locator: TableLocator;
  columns: TableColumnDefinition[];
  headerRowCount: number;
  templateRowCount: number;
  suggestedSourcePath: string | null;
  contextLabel: string | null;
  defaultMapping: TableBindingMapping;
  tableMapping: TableBindingMapping | null;
  isBindable: boolean;
  isBound: boolean;
  boundDataPath: string | null;
  parseMessage: string | null;
  segmentLocalOrder: number;
}
