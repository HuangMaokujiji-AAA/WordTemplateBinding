import type {
  DataFieldNode,
  DataFieldRecord,
  DataValueType,
} from "../../api/types";

export interface TableFieldOption {
  label: string;
  value: string;
}

/**
 * 为表格列映射生成稳定的相对字段选项。
 * 数据源字段树、原始字段记录和 Array 示例值按此顺序互相兜底。
 */
export function buildTableFieldOptions(
  dataPath: string,
  collectionNode: DataFieldNode | null,
  fieldRecords: DataFieldRecord[] = []
): TableFieldOption[] {
  const options = new Map<string, TableFieldOption>();

  for (const node of flattenNodes(collectionNode?.children || [])) {
    if (!node.isLeaf || !node.isBindable || node.type === "Array") continue;
    addOption(options, relativePath(node.path, dataPath), node.name, node.type);
  }

  for (const field of fieldRecords) {
    if (!field.isBindable || field.isArray) continue;
    addOption(
      options,
      relativePath(field.fieldPath, dataPath),
      field.fieldName,
      field.dataType
    );
  }

  const sample = readCollectionSample(collectionNode, fieldRecords, dataPath);
  if (Array.isArray(sample)) {
    for (const item of sample.slice(0, 50)) {
      collectSampleFields(item, "", options);
    }
  }

  return [...options.values()];
}

function flattenNodes(nodes: DataFieldNode[]): DataFieldNode[] {
  return nodes.flatMap((node) => [node, ...flattenNodes(node.children)]);
}

function relativePath(path: string, dataPath: string): string | null {
  const prefixes = [`${dataPath}[].`, `${dataPath}.`];
  if (dataPath === "rows") prefixes.push("row.");
  const prefix = prefixes.find((candidate) => path.startsWith(candidate));
  if (prefix) return path.slice(prefix.length);
  return null;
}

function addOption(
  options: Map<string, TableFieldOption>,
  value: string | null,
  name: string,
  type: DataValueType
): void {
  const normalized = value?.trim();
  if (!normalized || options.has(normalized)) return;
  options.set(normalized, {
    label: `${name || normalized} · ${type}`,
    value: normalized,
  });
}

function readCollectionSample(
  node: DataFieldNode | null,
  records: DataFieldRecord[],
  dataPath: string
): unknown {
  if (node?.sampleValueJson) {
    try {
      return JSON.parse(node.sampleValueJson) as unknown;
    } catch {
      // 继续使用原始字段接口中的结构化示例值。
    }
  }
  return records.find((field) => field.fieldPath === dataPath && field.isArray)
    ?.sampleValue;
}

function collectSampleFields(
  value: unknown,
  prefix: string,
  options: Map<string, TableFieldOption>
): void {
  if (!isRecord(value)) return;
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (isRecord(child)) {
      collectSampleFields(child, path, options);
      continue;
    }
    if (Array.isArray(child)) continue;
    addOption(options, path, key, inferType(child));
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function inferType(value: unknown): DataValueType {
  if (typeof value === "boolean") return "Boolean";
  if (typeof value === "number") return Number.isInteger(value) ? "Integer" : "Decimal";
  if (value && typeof value === "object") return "Object";
  return "String";
}
