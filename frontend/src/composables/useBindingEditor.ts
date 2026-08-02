import { computed, ref, type Ref } from "vue";
import type {
  ChartItem,
  MockItem,
  TableItem,
  TemplateResponse,
} from "../api/types";

export type WorkspaceTab =
  | "schema"
  | "bindings"
  | "properties"
  | "chart-structure"
  | "table-structure";

export const workspaceTabs: ReadonlyArray<readonly [WorkspaceTab, string]> = [
  ["schema", "数据源"],
  ["bindings", "已绑定"],
  ["properties", "属性"],
  ["chart-structure", "图表结构"],
  ["table-structure", "表格映射"],
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
  const selectedTable = computed<TableItem | null>(
    () =>
      template.value?.tables?.find(
        (table) => table.locatorId === selectedLocatorId.value
      ) || null
  );
  const boundItems = computed(
    () => template.value?.mockItems.filter((item) => item.isBound) || []
  );
  const boundCharts = computed(
    () => template.value?.charts.filter((chart) => chart.isBound) || []
  );
  const boundTables = computed(
    () => template.value?.tables?.filter((table) => table.isBound) || []
  );

  function syncSelection(): void {
    if (!selectedItem.value && !selectedChart.value && !selectedTable.value) {
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
    selectedTable,
    boundItems,
    boundCharts,
    boundTables,
    syncSelection,
    resetBindingEditor,
  };
}
