import { describe, expect, it } from "vitest";
import type { DataFieldNode, DataFieldRecord } from "../api/types";
import { buildTableFieldOptions } from "../features/binding/tableFieldOptions";

function arrayNode(overrides: Partial<DataFieldNode> = {}): DataFieldNode {
  return {
    name: "专业列表",
    path: "items",
    type: "Array",
    isCollection: true,
    isLeaf: false,
    isBindable: true,
    children: [],
    ...overrides,
  };
}

function field(overrides: Partial<DataFieldRecord>): DataFieldRecord {
  return {
    id: "1",
    snapshotId: "1",
    fieldPath: "row.name",
    fieldName: "专业名称",
    comment: null,
    dataType: "String",
    isArray: false,
    isNullable: false,
    isBindable: true,
    sampleValue: "人工智能",
    displayOrder: 1,
    ...overrides,
  };
}

describe("buildTableFieldOptions", () => {
  it("uses the real children returned with a structured Array", () => {
    const node = arrayNode({
      children: [
        {
          name: "专业名称",
          path: "items.name",
          type: "String",
          isCollection: false,
          isLeaf: true,
          isBindable: true,
          children: [],
        },
        {
          name: "统计信息",
          path: "items.statistics",
          type: "Object",
          isCollection: false,
          isLeaf: false,
          isBindable: false,
          children: [
            {
              name: "学生人数",
              path: "items.statistics.studentCount",
              type: "Integer",
              isCollection: false,
              isLeaf: true,
              isBindable: true,
              children: [],
            },
          ],
        },
      ],
    });

    expect(buildTableFieldOptions("items", node)).toEqual([
      { label: "专业名称 · String", value: "name" },
      { label: "学生人数 · Integer", value: "statistics.studentCount" },
    ]);
  });

  it("falls back to relational database row fields when Array children are absent", () => {
    const records = [
      field({ fieldPath: "rows", fieldName: "样例行集合", dataType: "Array", isArray: true }),
      field({ fieldPath: "row.major_name", fieldName: "专业名称" }),
      field({
        fieldPath: "row.student_count",
        fieldName: "学生人数",
        dataType: "Integer",
      }),
      field({ fieldPath: "other.unrelated", fieldName: "无关字段" }),
    ];

    expect(buildTableFieldOptions("rows", arrayNode({ path: "rows" }), records)).toEqual([
      { label: "专业名称 · String", value: "major_name" },
      { label: "学生人数 · Integer", value: "student_count" },
    ]);
  });

  it("derives fields from Array sample rows for an old snapshot", () => {
    const node = arrayNode({
      sampleValueJson: JSON.stringify([
        { name: "人工智能", statistics: { count: 120 } },
        { name: "金融学", college: "金融学院" },
      ]),
    });

    expect(buildTableFieldOptions("items", node)).toEqual([
      { label: "name · String", value: "name" },
      { label: "count · Integer", value: "statistics.count" },
      { label: "college · String", value: "college" },
    ]);
  });
});
