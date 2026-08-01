import { describe, expect, it } from "vitest";
import type { TemplateOutlineBlock } from "../api/types";
import {
  buildBoundaryDrafts,
  buildContiguousPartitions,
  buildSegmentMetadataDefaults,
  findSegmentMetadataValidationIssue,
  validateSegmentMetadata,
} from "../features/template-studio/segmentPartitions";

function block(index: number): TemplateOutlineBlock {
  return {
    blockId: `body/${index}`,
    blockType: "PARAGRAPH",
    displayText: `第 ${index + 1} 段`,
    segmentKey: null,
    canSelect: true,
    depth: 0,
    children: [],
  };
}

describe("segmentPartitions", () => {
  it("partitions every block exactly once with inclusive API ranges", () => {
    const blocks = [block(0), block(1), block(2), block(3), block(4)];
    const partitions = buildContiguousPartitions(blocks, [3, 1, 3]);

    expect(
      partitions.map(({ startIndex, endIndex }) => [startIndex, endIndex])
    ).toEqual([
      [0, 0],
      [1, 2],
      [3, 4],
    ]);

    const ownership = partitions.flatMap(({ startIndex, endIndex }, owner) =>
      Array.from({ length: endIndex - startIndex + 1 }, (_, offset) => ({
        block: startIndex + offset,
        owner,
      }))
    );
    expect(ownership.map(({ block: index }) => index)).toEqual([0, 1, 2, 3, 4]);
    expect(new Set(ownership.map(({ block: index }) => index)).size).toBe(
      blocks.length
    );
  });

  it("uses document start and end when no internal node is selected", () => {
    const blocks = [block(0), block(1), block(2)];
    const partitions = buildContiguousPartitions(blocks, []);

    expect(partitions).toHaveLength(1);
    expect(partitions[0].startBlock.blockId).toBe("body/0");
    expect(partitions[0].endBlock.blockId).toBe("body/2");
  });

  it("builds non-overlapping boundary drafts and validates names and keys", () => {
    const partitions = buildContiguousPartitions(
      [block(0), block(1), block(2), block(3)],
      [2]
    );
    const metadata = {
      0: { segmentName: "概况", segmentKey: "overview" },
      2: { segmentName: "结果", segmentKey: "results" },
    };

    expect(validateSegmentMetadata(partitions, metadata)).toBeNull();
    expect(buildBoundaryDrafts(partitions, metadata)).toEqual([
      {
        segmentName: "概况",
        segmentKey: "overview",
        startBlockId: "body/0",
        endBlockId: "body/1",
      },
      {
        segmentName: "结果",
        segmentKey: "results",
        startBlockId: "body/2",
        endBlockId: "body/3",
      },
    ]);
  });

  it("blocks empty, invalid, and duplicate fragment keys", () => {
    const partitions = buildContiguousPartitions([block(0), block(1)], [1]);

    expect(
      validateSegmentMetadata(partitions, {
        0: { segmentName: "划分块1", segmentKey: "same" },
        1: { segmentName: "划分块2", segmentKey: "same" },
      })
    ).toContain("重复");
    expect(
      validateSegmentMetadata(partitions, {
        0: { segmentName: "划分块1", segmentKey: "Invalid_Key" },
        1: { segmentName: "划分块2", segmentKey: "second" },
      })
    ).toContain("小写字母");
  });

  it("locates the first invalid partition and field for the editor", () => {
    const partitions = buildContiguousPartitions(
      [block(0), block(1), block(2)],
      [1, 2]
    );

    expect(
      findSegmentMetadataValidationIssue(partitions, {
        0: { segmentName: "划分块1", segmentKey: "first" },
        1: { segmentName: "划分块2", segmentKey: "second" },
        2: { segmentName: "划分块3", segmentKey: "second" },
      })
    ).toEqual({
      message: "片段键“second”重复，请修改后再继续。",
      startIndex: 2,
      field: "segmentKey",
    });
  });

  it("generates unique default keys without changing existing keys", () => {
    const blocks = [block(0), block(1), block(2), block(3)];
    const initialPartitions = buildContiguousPartitions(blocks, [2]);
    const initialMetadata = buildSegmentMetadataDefaults(initialPartitions, {});

    expect(initialMetadata).toEqual({
      0: { segmentName: "划分块1", segmentKey: "segment-1" },
      2: { segmentName: "划分块2", segmentKey: "segment-2" },
    });

    const partitionsWithMiddleInsertion = buildContiguousPartitions(
      blocks,
      [1, 2]
    );
    expect(
      buildSegmentMetadataDefaults(partitionsWithMiddleInsertion, initialMetadata)
    ).toEqual({
      0: { segmentName: "划分块1", segmentKey: "segment-1" },
      1: { segmentName: "划分块2", segmentKey: "segment-3" },
      2: { segmentName: "划分块3", segmentKey: "segment-2" },
    });
  });
});
