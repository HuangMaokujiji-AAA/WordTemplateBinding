<script setup lang="ts">
import {
  nextTick,
  onBeforeUnmount,
  ref,
  watch,
} from "vue";
import { getTemplateSegmentPreview } from "../../../api/client";
import type {
  ChartItem,
  MockItem,
  TemplateElementRecord,
  TemplateSegmentRecord,
} from "../../../api/types";
import DocxViewer from "../../../components/DocxViewer.vue";
import {
  decorateRenderedDocument,
  focusBindingTarget,
} from "../../binding/renderedDocumentBindings";
import {
  decorateRenderedCharts,
  focusChartTarget,
} from "../../binding/renderedChartBindings";
import { processDocx } from "../../docx/processDocx";
import { chartInstanceManager } from "../../docx/rendering/chartInstanceManager";

const props = defineProps<{
  segment: TemplateSegmentRecord | null;
  mockItems?: MockItem[];
  charts?: ChartItem[];
  elements?: TemplateElementRecord[];
  selectedLocatorId?: string;
}>();

const emit = defineEmits<{
  "select-target": [locatorId: string];
}>();

const viewer = ref<InstanceType<typeof DocxViewer> | null>(null);
const loading = ref(false);
const message = ref("请选择一个片段查看预览。");
const visible = ref(false);
const chartCount = ref(0);
const markedCount = ref(0);
let taskId = 0;

function decorateTargets(container: HTMLElement): void {
  const textResult = decorateRenderedDocument(container, props.mockItems || [], {
    onSelect: (item) => emit("select-target", item.locatorId),
    onBind: () => {
      message.value = "请在第 6 步完成字段绑定。";
    },
    onError: (value) => {
      message.value = value;
    },
  });
  const chartResult = decorateRenderedCharts(container, props.charts || [], {
    onSelect: (chart) => emit("select-target", chart.locatorId),
    onBind: () => {
      message.value = "请在第 6 步完成图表字段绑定。";
    },
    onError: (value) => {
      message.value = value;
    },
  });
  const tableCount = decorateTables(container);
  markedCount.value =
    textResult.renderedCount + chartResult.renderedCount + tableCount;
  applySelectedTarget();
}

function decorateTables(container: HTMLElement): number {
  const tableElements = (props.elements || [])
    .filter((element) => ["TABLE", "REPEAT_BLOCK"].includes(element.elementType))
    .sort((left, right) => left.segmentLocalOrder - right.segmentLocalOrder);
  const renderedTables = [
    ...container.querySelectorAll<HTMLElement>(".docx article table"),
  ];
  let decoratedCount = 0;
  const usedTables = new Set<HTMLElement>();

  tableElements.forEach((element) => {
    const expectedSignature = normalizeTableHeader(
      typeof element.locator.headerSignature === "string"
        ? element.locator.headerSignature
        : ""
    );
    const table = renderedTables.find((candidate) =>
      !usedTables.has(candidate) &&
      renderedTableHeader(candidate) === expectedSignature
    ) || renderedTables.find((candidate) => !usedTables.has(candidate));
    const locatorId = element.locator.locatorId;
    if (!table || typeof locatorId !== "string") return;
    usedTables.add(table);
    table.classList.add("template-table-target");
    table.dataset.locatorId = locatorId;
    table.addEventListener("click", (event) => {
      const clicked = event.target as HTMLElement;
      if (clicked.closest(".template-binding-target, .template-chart-target")) return;
      emit("select-target", locatorId);
    });
    decoratedCount += 1;
  });

  return decoratedCount;
}

function renderedTableHeader(table: HTMLElement): string {
  const firstRow = table.querySelector("tr");
  if (!firstRow) return "";
  return [...firstRow.children]
    .filter((cell) => cell.tagName === "TD" || cell.tagName === "TH")
    .map((cell) => normalizeTableHeader(String(cell.textContent || "")))
    .join("|");
}

function normalizeTableHeader(value: string): string {
  return value.replace(/\s+/g, "").replace(/[（）()]/g, "");
}

function applySelectedTarget(): void {
  const container = viewer.value?.getDocumentContainer();
  if (!container) return;
  container
    .querySelectorAll(".studio-marking-selected")
    .forEach((element) => element.classList.remove("studio-marking-selected"));
  if (!props.selectedLocatorId) return;

  const candidates = container.querySelectorAll<HTMLElement>(
    "[data-locator-id], [data-chart-locator-id]"
  );
  const target = [...candidates].find(
    (element) =>
      element.dataset.locatorId === props.selectedLocatorId
      || element.dataset.chartLocatorId === props.selectedLocatorId
  );
  target?.classList.add("studio-marking-selected");
}

function focusTarget(
  locatorId: string,
  targetType: "TEXT" | "CHART" | "TABLE"
): boolean {
  const container = viewer.value?.getDocumentContainer();
  if (!container) return false;
  if (targetType === "CHART") return focusChartTarget(container, locatorId);
  if (targetType === "TEXT") return focusBindingTarget(container, locatorId);
  const table = [...container.querySelectorAll<HTMLElement>(".template-table-target")]
    .find((element) => element.dataset.locatorId === locatorId);
  table?.scrollIntoView({ behavior: "smooth", block: "center" });
  return Boolean(table);
}

defineExpose({ focusTarget });

async function loadPreview(): Promise<void> {
  const segment = props.segment;
  const currentTask = ++taskId;
  cleanup();
  if (!segment) {
    message.value = "请选择一个片段查看预览。";
    return;
  }

  loading.value = true;
  message.value = "正在生成并加载片段预览…";
  try {
    const file = await getTemplateSegmentPreview(
      segment.id,
      `${segment.segmentKey}-preview.docx`
    );
    if (currentTask !== taskId) return;
    visible.value = true;
    await nextTick();
    const documentContainer = viewer.value?.getDocumentContainer();
    const styleContainer = viewer.value?.getStyleContainer();
    if (!documentContainer || !styleContainer) {
      throw new Error("片段预览容器未就绪。");
    }

    const result = await processDocx(file, {
      documentContainer,
      styleContainer,
      onProgress: (progress) => {
        message.value = progress.message;
      },
    });
    if (currentTask !== taskId) return;
    chartCount.value = result.renderedCharts;
    decorateTargets(documentContainer);
    message.value = `预览完成，已标记 ${markedCount.value} 个可定位目标，并渲染 ${result.renderedCharts}/${result.totalCharts} 个 Word 图表。`;
  } catch (error) {
    if (currentTask === taskId) {
      message.value =
        error instanceof Error ? error.message : "加载片段预览失败。";
      visible.value = false;
    }
  } finally {
    if (currentTask === taskId) loading.value = false;
  }
}

function cleanup(): void {
  chartInstanceManager.disposeAll();
  visible.value = false;
  chartCount.value = 0;
  markedCount.value = 0;
  const documentContainer = viewer.value?.getDocumentContainer();
  const styleContainer = viewer.value?.getStyleContainer();
  documentContainer?.replaceChildren();
  styleContainer?.replaceChildren();
}

watch(
  () => props.segment?.id,
  () => void loadPreview(),
  { immediate: true }
);

watch(() => props.selectedLocatorId, applySelectedTarget);

onBeforeUnmount(() => {
  taskId += 1;
  cleanup();
});
</script>

<template>
  <div class="segment-preview">
    <div class="preview-status" :class="{ loading }">
      <span>{{ message }}</span>
      <span class="preview-counts">
        <small v-if="markedCount > 0">标记 {{ markedCount }}</small>
        <small v-if="chartCount > 0">图表 {{ chartCount }}</small>
      </span>
    </div>
    <div class="preview-canvas">
      <DocxViewer ref="viewer" :visible="visible" />
    </div>
  </div>
</template>

<style scoped>
.segment-preview {
  min-width: 0;
  border: 1px solid #d9e1ec;
  border-radius: 10px;
  background: #dfe5ed;
}

.preview-status {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 9px 12px;
  border-bottom: 1px solid #d3dbe7;
  background: #fff;
  color: #667085;
  font-size: 10px;
}

.preview-status.loading {
  color: #3157d5;
}

.preview-counts {
  display: flex;
  flex: none;
  gap: 8px;
}

.preview-canvas {
  max-height: 680px;
  overflow: auto;
  padding: 18px;
}

:deep(.docx-viewer-wrapper),
:deep(.docx-viewer__document),
:deep(.docx-wrapper) {
  width: 100%;
  min-width: 0;
  max-width: none;
}

:deep(.docx-wrapper > section.docx) {
  margin-right: auto !important;
  margin-left: auto !important;
}

:deep(.studio-marking-selected) {
  box-shadow: 0 0 0 5px rgb(49 87 213 / 28%) !important;
}

:deep(.template-table-target) {
  outline: 3px solid rgb(22 128 92 / 70%) !important;
  outline-offset: 2px;
  cursor: pointer;
}
</style>
