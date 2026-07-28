import type { DataValueType } from "./chart";

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

