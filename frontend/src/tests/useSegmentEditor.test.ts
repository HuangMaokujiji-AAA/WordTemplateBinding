import { nextTick } from "vue";
import { describe, expect, it } from "vitest";
import { useSegmentEditor } from "../composables/useSegmentEditor";

describe("useSegmentEditor", () => {
  it("flattens selectable blocks and keeps boundary ends on the same level", async () => {
    const editor = useSegmentEditor();
    editor.segmentOutline.value = {
      templateVersionId: "9",
      contentHash: "hash",
      blocks: [
        {
          blockId: "body/1",
          blockType: "PARAGRAPH",
          displayText: "开始",
          segmentKey: null,
          canSelect: true,
          depth: 0,
          children: [],
        },
        {
          blockId: "body/2",
          blockType: "PARAGRAPH",
          displayText: "结束",
          segmentKey: null,
          canSelect: true,
          depth: 0,
          children: [],
        },
        {
          blockId: "header/1",
          blockType: "PARAGRAPH",
          displayText: "页眉",
          segmentKey: null,
          canSelect: true,
          depth: 0,
          children: [],
        },
      ],
    };
    editor.boundaryStartBlockId.value = "body/1";
    await nextTick();

    expect(editor.selectableOutlineBlocks.value).toHaveLength(3);
    expect(editor.boundaryEndBlocks.value.map((item) => item.blockId)).toEqual([
      "body/1",
      "body/2",
    ]);
  });

  it("resets segment and boundary draft state together", () => {
    const editor = useSegmentEditor();
    editor.selectedSegmentId.value = "2";
    editor.boundaryKey.value = "major";
    editor.boundaryManagerVisible.value = true;

    editor.resetSegmentEditor();

    expect(editor.selectedSegmentId.value).toBe("");
    expect(editor.boundaryKey.value).toBe("");
    expect(editor.boundaryManagerVisible.value).toBe(false);
  });
});
