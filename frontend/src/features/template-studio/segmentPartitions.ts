import type {
  TemplateOutlineBlock,
  TemplateSegmentBoundaryDraft,
} from "../../api/types";

export interface SegmentPartition {
  startIndex: number;
  endIndex: number;
  startBlock: TemplateOutlineBlock;
  endBlock: TemplateOutlineBlock;
}

export interface SegmentMetadata {
  segmentName: string;
  segmentKey: string;
}

export type SegmentMetadataField = "segmentName" | "segmentKey";

export interface SegmentMetadataValidationIssue {
  message: string;
  startIndex: number | null;
  field: SegmentMetadataField | null;
}

/**
 * Treats every split index as the boundary immediately before that block.
 * The returned ranges are closed ranges because the DOCX boundary API uses
 * inclusive start/end block IDs.
 */
export function buildContiguousPartitions(
  blocks: TemplateOutlineBlock[],
  splitIndexes: Iterable<number>
): SegmentPartition[] {
  if (blocks.length === 0) return [];

  const starts = [
    0,
    ...new Set(
      [...splitIndexes].filter(
        (index) => Number.isInteger(index) && index > 0 && index < blocks.length
      )
    ),
  ].sort((left, right) => left - right);

  return starts.map((startIndex, partitionIndex) => {
    const nextStart = starts[partitionIndex + 1] ?? blocks.length;
    const endIndex = nextStart - 1;
    return {
      startIndex,
      endIndex,
      startBlock: blocks[startIndex],
      endBlock: blocks[endIndex],
    };
  });
}

export function buildBoundaryDrafts(
  partitions: SegmentPartition[],
  metadataByStart: Record<number, SegmentMetadata>
): TemplateSegmentBoundaryDraft[] {
  return partitions.map((partition) => {
    const metadata = metadataByStart[partition.startIndex];
    return {
      segmentName: metadata?.segmentName.trim() ?? "",
      segmentKey: metadata?.segmentKey.trim() ?? "",
      startBlockId: partition.startBlock.blockId,
      endBlockId: partition.endBlock.blockId,
    };
  });
}

export function buildSegmentMetadataDefaults(
  partitions: SegmentPartition[],
  metadataByStart: Record<number, SegmentMetadata>
): Record<number, SegmentMetadata> {
  const reservedKeys = new Set(
    partitions
      .map((partition) => metadataByStart[partition.startIndex]?.segmentKey.trim())
      .filter((key): key is string => !!key)
  );
  let generatedKeyNumber = 1;

  const nextGeneratedKey = (): string => {
    let key = `segment-${generatedKeyNumber}`;
    while (reservedKeys.has(key)) {
      generatedKeyNumber += 1;
      key = `segment-${generatedKeyNumber}`;
    }
    reservedKeys.add(key);
    generatedKeyNumber += 1;
    return key;
  };

  return Object.fromEntries(
    partitions.map((partition, index) => {
      const existing = metadataByStart[partition.startIndex];
      return [
        partition.startIndex,
        {
          segmentName:
            existing && !/^划分块\d+$/.test(existing.segmentName)
              ? existing.segmentName
              : `划分块${index + 1}`,
          segmentKey: existing?.segmentKey || nextGeneratedKey(),
        },
      ];
    })
  );
}

export function findSegmentMetadataValidationIssue(
  partitions: SegmentPartition[],
  metadataByStart: Record<number, SegmentMetadata>
): SegmentMetadataValidationIssue | null {
  if (partitions.length === 0) {
    return {
      message: "文档中没有可划分的正文块。",
      startIndex: null,
      field: null,
    };
  }

  const keys = new Set<string>();
  for (const [index, partition] of partitions.entries()) {
    const metadata = metadataByStart[partition.startIndex];
    if (!metadata?.segmentName.trim()) {
      return {
        message: `请填写划分块${index + 1}的片段名称。`,
        startIndex: partition.startIndex,
        field: "segmentName",
      };
    }

    const key = metadata.segmentKey.trim();
    if (!key) {
      return {
        message: `请填写划分块${index + 1}的片段键。`,
        startIndex: partition.startIndex,
        field: "segmentKey",
      };
    }
    if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(key)) {
      return {
        message: `划分块${index + 1}的片段键只能包含小写字母、数字和短横线。`,
        startIndex: partition.startIndex,
        field: "segmentKey",
      };
    }
    if (keys.has(key)) {
      return {
        message: `片段键“${key}”重复，请修改后再继续。`,
        startIndex: partition.startIndex,
        field: "segmentKey",
      };
    }
    keys.add(key);
  }

  return null;
}

export function validateSegmentMetadata(
  partitions: SegmentPartition[],
  metadataByStart: Record<number, SegmentMetadata>
): string | null {
  return findSegmentMetadataValidationIssue(partitions, metadataByStart)?.message || null;
}
