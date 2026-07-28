import { computed, ref, type Ref } from "vue";
import type {
  ChartItem,
  MockItem,
  TemplateResponse,
} from "../api/types";

export type WorkspaceTab =
  | "schema"
  | "bindings"
  | "properties"
  | "chart-structure";

export const workspaceTabs: ReadonlyArray<readonly [WorkspaceTab, string]> = [
  ["schema", "数据源"],
  ["bindings", "已绑定"],
  ["properties", "属性"],
  ["chart-structure", "图表结构"],
];

export function useBindingEditor(
  template: Ref<TemplateResponse | null>
) {
  const bindingSetId = ref("");
  const bindingPreview = ref("");
  const activeTab = ref<WorkspaceTab>("schema");
  const selectedLocatorId = ref<string | null>(null);

  const selectedItem = computed<MockItem | null>(
    () =>
      template.value?.mockItems.find(
        (item) => item.locatorId === selectedLocatorId.value
      ) || null
  );
  const selectedChart = computed<ChartItem | null>(
    () =>
      template.value?.charts.find(
        (chart) => chart.locatorId === selectedLocatorId.value
      ) || null
  );
  const boundItems = computed(
    () => template.value?.mockItems.filter((item) => item.isBound) || []
  );
  const boundCharts = computed(
    () => template.value?.charts.filter((chart) => chart.isBound) || []
  );

  function syncSelection(): void {
    if (!selectedItem.value && !selectedChart.value) {
      selectedLocatorId.value = null;
    }
  }

  function resetBindingEditor(): void {
    bindingSetId.value = "";
    bindingPreview.value = "";
    selectedLocatorId.value = null;
    activeTab.value = "schema";
  }

  return {
    bindingSetId,
    bindingPreview,
    activeTab,
    selectedLocatorId,
    selectedItem,
    selectedChart,
    boundItems,
    boundCharts,
    syncSelection,
    resetBindingEditor,
  };
}
