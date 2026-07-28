import { ref } from "vue";
import { describe, expect, it } from "vitest";
import type { TemplateResponse } from "../api/types";
import { useBindingEditor } from "../composables/useBindingEditor";

describe("useBindingEditor", () => {
  it("derives the current target and bound navigation from template state", () => {
    const template = ref({
      mockItems: [
        { locatorId: "text-1", isBound: true },
        { locatorId: "text-2", isBound: false },
      ],
      charts: [{ locatorId: "chart-1", isBound: true }],
    } as TemplateResponse);
    const editor = useBindingEditor(template);
    editor.selectedLocatorId.value = "text-1";

    expect(editor.selectedItem.value?.locatorId).toBe("text-1");
    expect(editor.boundItems.value.map((item) => item.locatorId)).toEqual([
      "text-1",
    ]);
    expect(editor.boundCharts.value.map((item) => item.locatorId)).toEqual([
      "chart-1",
    ]);
  });

  it("clears stale selection and resets the editable binding state", () => {
    const template = ref({
      mockItems: [],
      charts: [],
    } as unknown as TemplateResponse);
    const editor = useBindingEditor(template);
    editor.bindingSetId.value = "8";
    editor.bindingPreview.value = "preview";
    editor.selectedLocatorId.value = "missing";
    editor.activeTab.value = "properties";

    editor.syncSelection();
    expect(editor.selectedLocatorId.value).toBeNull();

    editor.resetBindingEditor();
    expect(editor.bindingSetId.value).toBe("");
    expect(editor.bindingPreview.value).toBe("");
    expect(editor.activeTab.value).toBe("schema");
  });
});
