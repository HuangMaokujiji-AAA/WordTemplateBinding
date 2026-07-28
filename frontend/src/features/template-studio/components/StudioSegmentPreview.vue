<script setup lang="ts">
import {
  nextTick,
  onBeforeUnmount,
  ref,
  watch,
} from "vue";
import { getTemplateSegmentPreview } from "../../../api/client";
import type { TemplateSegmentRecord } from "../../../api/types";
import DocxViewer from "../../../components/DocxViewer.vue";
import { processDocx } from "../../docx/processDocx";
import { chartInstanceManager } from "../../docx/rendering/chartInstanceManager";

const props = defineProps<{
  segment: TemplateSegmentRecord | null;
}>();

const viewer = ref<InstanceType<typeof DocxViewer> | null>(null);
const loading = ref(false);
const message = ref("请选择一个片段查看预览。");
const visible = ref(false);
const chartCount = ref(0);
let taskId = 0;

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
    message.value = `预览完成，识别并渲染 ${result.renderedCharts}/${result.totalCharts} 个 Word 图表。`;
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

onBeforeUnmount(() => {
  taskId += 1;
  cleanup();
});
</script>

<template>
  <div class="segment-preview">
    <div class="preview-status" :class="{ loading }">
      <span>{{ message }}</span>
      <small v-if="chartCount > 0">图表 {{ chartCount }}</small>
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

.preview-canvas {
  max-height: 680px;
  overflow: auto;
  padding: 18px;
}
</style>
