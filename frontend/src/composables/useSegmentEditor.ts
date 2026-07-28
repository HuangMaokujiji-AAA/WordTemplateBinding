import { computed, ref, watch } from "vue";
import type {
  TemplateElementRecord,
  TemplateOutlineBlock,
  TemplateSegmentOutline,
  TemplateSegmentRecord,
} from "../api/types";

export function useSegmentEditor() {
  const segments = ref<TemplateSegmentRecord[]>([]);
  const selectedSegmentId = ref("");
  const segmentElements = ref<TemplateElementRecord[]>([]);
  const segmentOutline = ref<TemplateSegmentOutline | null>(null);
  const boundaryManagerVisible = ref(false);
  const boundaryKey = ref("");
  const boundaryName = ref("");
  const boundaryStartBlockId = ref("");
  const boundaryEndBlockId = ref("");

  const outlineBlocks = computed(() => {
    const flatten = (items: TemplateOutlineBlock[]): TemplateOutlineBlock[] =>
      items.flatMap((item) => [item, ...flatten(item.children)]);
    return flatten(segmentOutline.value?.blocks ?? []);
  });

  const selectableOutlineBlocks = computed(() =>
    outlineBlocks.value.filter((item) => item.canSelect)
  );

  const boundaryEndBlocks = computed(() => {
    const start = selectableOutlineBlocks.value.find(
      (item) => item.blockId === boundaryStartBlockId.value
    );
    if (!start) return selectableOutlineBlocks.value;
    const parentPath = start.blockId.slice(0, start.blockId.lastIndexOf("/"));
    return selectableOutlineBlocks.value.filter(
      (item) =>
        item.blockId.slice(0, item.blockId.lastIndexOf("/")) === parentPath
    );
  });

  watch(boundaryStartBlockId, () => {
    if (
      !boundaryEndBlocks.value.some(
        (item) => item.blockId === boundaryEndBlockId.value
      )
    ) {
      boundaryEndBlockId.value = boundaryEndBlocks.value[0]?.blockId || "";
    }
  });

  function resetSegmentEditor(): void {
    segments.value = [];
    selectedSegmentId.value = "";
    segmentElements.value = [];
    segmentOutline.value = null;
    boundaryManagerVisible.value = false;
    boundaryKey.value = "";
    boundaryName.value = "";
    boundaryStartBlockId.value = "";
    boundaryEndBlockId.value = "";
  }

  function segmentIndent(segment: TemplateSegmentRecord): string {
    let depth = 0;
    let parentId = segment.parentSegmentId;
    const visited = new Set<string>();
    while (parentId && !visited.has(parentId)) {
      visited.add(parentId);
      depth += 1;
      parentId =
        segments.value.find((item) => item.id === parentId)?.parentSegmentId ||
        null;
    }
    return `${0.75 + depth}rem`;
  }

  function outlineLabel(block: TemplateOutlineBlock): string {
    return `${"　".repeat(block.depth)}${block.displayText}`;
  }

  return {
    segments,
    selectedSegmentId,
    segmentElements,
    segmentOutline,
    boundaryManagerVisible,
    boundaryKey,
    boundaryName,
    boundaryStartBlockId,
    boundaryEndBlockId,
    outlineBlocks,
    selectableOutlineBlocks,
    boundaryEndBlocks,
    resetSegmentEditor,
    segmentIndent,
    outlineLabel,
  };
}
