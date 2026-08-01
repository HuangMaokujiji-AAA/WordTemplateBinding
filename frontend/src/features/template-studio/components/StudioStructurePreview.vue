<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from "vue";
import {
  getTemplateVersionFile,
} from "../../../api/client";
import type {
  TemplateOutlineBlock,
  TemplateVersionView,
} from "../../../api/types";
import DocxViewer from "../../../components/DocxViewer.vue";
import { processDocx } from "../../docx/processDocx";
import { chartInstanceManager } from "../../docx/rendering/chartInstanceManager";
import {
  applyStructurePreviewRange,
  configureSplitNodeLane,
  findSafePageEndSplitCandidates,
  findRenderedBlockForMarker,
  placeSplitNodeInOwnRow,
  shouldShowSplitNode,
  type StructurePreviewRange,
} from "../structureSplitNodes";

const props = defineProps<{
  versionView: TemplateVersionView | null;
  blocks: TemplateOutlineBlock[];
  splitIndexes: number[];
  pendingIndex: number | null;
  previewRange?: StructurePreviewRange | null;
}>();

const emit = defineEmits<{
  "select-node": [index: number];
}>();

const viewer = ref<InstanceType<typeof DocxViewer> | null>(null);
const previewCanvas = ref<HTMLDivElement | null>(null);
const loading = ref(false);
const visible = ref(false);
const message = ref("正在准备 Word 预览…");
const zoomPercent = ref(100);
const zoomOptions = Array.from({ length: 11 }, (_, index) => 50 + index * 10);
const blockMarkerIds = ref<Record<string, string>>({});
const blockEndMarkerIds = ref<Record<string, string>>({});
let taskId = 0;

function setZoom(value: number): void {
  zoomPercent.value = Math.min(150, Math.max(50, value));
}

function zoomOut(): void {
  setZoom(zoomPercent.value - 10);
}

function zoomIn(): void {
  setZoom(zoomPercent.value + 10);
}

async function centerPreview(): Promise<void> {
  await nextTick();
  const canvas = previewCanvas.value;
  if (!canvas) return;
  canvas.scrollLeft = Math.max(0, (canvas.scrollWidth - canvas.clientWidth) / 2);
}

function fitWidth(allowEnlarge = true): void {
  const canvas = previewCanvas.value;
  const container = viewer.value?.getDocumentContainer();
  if (!canvas || !container) return;
  if (canvas.clientWidth <= 36) return;

  const pages = [...container.querySelectorAll<HTMLElement>("section.docx")];
  const renderedWidth = Math.max(
    0,
    ...pages.map((page) => page.getBoundingClientRect().width)
  );
  if (renderedWidth <= 0) return;

  const naturalWidth = renderedWidth / (zoomPercent.value / 100);
  const availableWidth = Math.max(1, canvas.clientWidth - 36);
  const fittedPercent =
    Math.floor((availableWidth / naturalWidth) * 100 / 10) * 10;
  setZoom(allowEnlarge ? fittedPercent : Math.min(100, fittedPercent));
  void centerPreview();
}

function createNode(index: number, label: string, fixed = false): HTMLElement {
  const wrapper = document.createElement("div");
  wrapper.className = "structure-node-host";
  const button = document.createElement("button");
  button.type = "button";
  button.className = "structure-node";
  button.dataset.nodeIndex = String(index);
  button.textContent = label;
  button.disabled = fixed;
  if (fixed) button.classList.add("is-fixed");
  if (props.splitIndexes.includes(index)) button.classList.add("is-split");
  if (props.pendingIndex === index) button.classList.add("is-pending");
  if (!fixed) button.addEventListener("click", () => emit("select-node", index));
  configureSplitNodeLane(wrapper, button);
  wrapper.append(button);
  return wrapper;
}

function renderedBlocks(container: HTMLElement): HTMLElement[] {
  const articles = [...container.querySelectorAll<HTMLElement>(".docx article")];
  return articles.flatMap((article) =>
    [...article.children].filter(
      (child): child is HTMLElement =>
        child instanceof HTMLElement &&
        !child.classList.contains("structure-node-host")
    )
  );
}

function placeNode(
  renderedElement: HTMLElement,
  node: HTMLElement,
  edge: "before" | "after" = "before"
): void {
  if (
    !placeSplitNodeInOwnRow(
      node,
      renderedElement,
      edge
    )
  ) {
    if (edge === "before") renderedElement.before(node);
    else renderedElement.after(node);
  }
}

function renderNodes(): number {
  const container = viewer.value?.getDocumentContainer();
  if (!container || !visible.value) return 0;
  container.querySelectorAll(".structure-node-host").forEach((item) => item.remove());

  const elements = renderedBlocks(container);
  const hasStableMarkers = Object.keys(blockMarkerIds.value).length > 0;
  const mappedElements = props.blocks.map((block, index) =>
    hasStableMarkers
      ? findRenderedBlockForMarker(
          container,
          blockMarkerIds.value[block.blockId] || ""
        )
      : elements[index] || null
  );
  const mappedEndElements = props.blocks.map((block) =>
    findRenderedBlockForMarker(
      container,
      blockEndMarkerIds.value[block.blockId] || ""
    )
  );
  const mappedCount = mappedElements.filter(Boolean).length;
  const visibleElements = applyStructurePreviewRange(
    container,
    elements,
    mappedElements,
    props.previewRange || null
  );

  if (props.previewRange) {
    const first = visibleElements[0];
    const last = visibleElements[visibleElements.length - 1];
    if (first && last) {
      placeNode(first, createNode(props.previewRange.startIndex, "片段开始", true));
      placeNode(
        last,
        createNode(props.previewRange.endIndex + 1, "片段结束", true),
        "after"
      );
      message.value = `正在预览第 ${props.previewRange.startIndex + 1}～${props.previewRange.endIndex + 1} 个正文块。`;
    } else {
      message.value = "当前划分块没有可显示的预览内容。";
    }
    return 0;
  }

  const pageEndCandidates = findSafePageEndSplitCandidates(
    container,
    mappedElements,
    mappedEndElements
  );
  const pageEndSplitIndexes = new Set(
    pageEndCandidates.map((candidate) => candidate.splitIndex)
  );
  let selectableNodeCount = 0;
  for (let index = 0; index < props.blocks.length; index += 1) {
    const renderedElement = mappedElements[index];
    if (!renderedElement) continue;
    if (pageEndSplitIndexes.has(index)) {
      selectableNodeCount += 1;
      continue;
    }
    if (
      index > 0 &&
      !shouldShowSplitNode(props.blocks[index], renderedElement)
    ) {
      continue;
    }
    const label = index === 0
      ? "文档开头（固定）"
      : props.splitIndexes.includes(index)
        ? `划分节点 ${index}（已添加）`
        : `在此划分 · ${props.blocks[index].displayText}`;
    placeNode(renderedElement, createNode(index, label, index === 0));
    if (index > 0) selectableNodeCount += 1;
  }

  pageEndCandidates.forEach((candidate) => {
    const node = createNode(
      candidate.splitIndex,
      `在页尾划分 · ${props.blocks[candidate.splitIndex].displayText}`
    );
    node.classList.add("is-page-end");
    placeNode(candidate.anchor, node, "after");
  });

  const last = elements[elements.length - 1];
  if (last) {
    placeNode(
      last,
      createNode(props.blocks.length, "文档结尾（固定）", true),
      "after"
    );
  }
  if (mappedCount < props.blocks.length) {
    message.value = `Word 预览已加载；其中 ${mappedCount}/${props.blocks.length} 个节点已定位到页面。`;
  }
  return selectableNodeCount;
}

async function loadPreview(): Promise<void> {
  const versionView = props.versionView;
  const currentTask = ++taskId;
  cleanup();
  if (!versionView) return;

  loading.value = true;
  message.value = "正在加载完整 Word 文档并定位划分节点…";
  try {
    const file = await getTemplateVersionFile(
      versionView.version.id,
      versionView.file.originalName
    );
    if (currentTask !== taskId) return;
    visible.value = true;
    await nextTick();
    const documentContainer = viewer.value?.getDocumentContainer();
    const styleContainer = viewer.value?.getStyleContainer();
    if (!documentContainer || !styleContainer) {
      throw new Error("Word 预览容器未就绪。");
    }

    const result = await processDocx(file, {
      documentContainer,
      styleContainer,
      outlineBlockIds: props.blocks.map((block) => block.blockId),
      onProgress: (progress) => {
        message.value = progress.message;
      },
    });
    if (currentTask !== taskId) return;
    blockMarkerIds.value = result.blockMarkerIds || {};
    blockEndMarkerIds.value = result.blockEndMarkerIds || {};
    const selectableNodeCount = renderNodes();
    fitWidth(false);
    if (props.previewRange) {
      message.value = `正在预览第 ${props.previewRange.startIndex + 1}～${props.previewRange.endIndex + 1} 个正文块。`;
    } else if (!message.value.includes("个节点已定位")) {
      message.value = `Word 预览已加载，可选择 ${selectableNodeCount} 个内部划分节点。识别并渲染 ${result.renderedCharts}/${result.totalCharts} 个图表。`;
    }
  } catch (error) {
    if (currentTask === taskId) {
      message.value = error instanceof Error ? error.message : "加载 Word 预览失败。";
      visible.value = false;
    }
  } finally {
    if (currentTask === taskId) loading.value = false;
  }
}

function cleanup(): void {
  chartInstanceManager.disposeAll();
  visible.value = false;
  blockMarkerIds.value = {};
  blockEndMarkerIds.value = {};
  viewer.value?.getDocumentContainer()?.replaceChildren();
  viewer.value?.getStyleContainer()?.replaceChildren();
}

watch(() => props.versionView?.version.id, () => void loadPreview(), { immediate: true });
watch(
  [() => props.splitIndexes, () => props.pendingIndex, () => props.previewRange],
  () => renderNodes(),
  { deep: true }
);
watch(zoomPercent, () => void centerPreview());

onBeforeUnmount(() => {
  taskId += 1;
  cleanup();
});
</script>

<template>
  <div class="structure-preview">
    <div class="preview-status" :class="{ loading }">
      <span>{{ message }}</span>
      <div class="preview-zoom" aria-label="Word 预览缩放">
        <button
          type="button"
          title="缩小"
          aria-label="缩小 Word 预览"
          :disabled="zoomPercent <= 50"
          @click="zoomOut"
        >
          −
        </button>
        <select
          v-model.number="zoomPercent"
          title="选择缩放比例"
          aria-label="Word 预览缩放比例"
        >
          <option v-for="option in zoomOptions" :key="option" :value="option">
            {{ option }}%
          </option>
        </select>
        <button
          type="button"
          title="放大"
          aria-label="放大 Word 预览"
          :disabled="zoomPercent >= 150"
          @click="zoomIn"
        >
          +
        </button>
        <button type="button" title="恢复原始大小" @click="setZoom(100)">
          100%
        </button>
        <button type="button" title="按预览区域宽度缩放" @click="fitWidth()">
          适应宽度
        </button>
      </div>
    </div>
    <div ref="previewCanvas" class="preview-canvas">
      <div
        class="preview-zoom-content"
        :style="{ '--preview-zoom': zoomPercent / 100 }"
      >
        <DocxViewer ref="viewer" :visible="visible" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.structure-preview {
  min-width: 0;
  border: 1px solid #d9e1ec;
  border-radius: 10px;
  background: #dfe5ed;
}

.preview-status {
  position: sticky;
  top: 0;
  z-index: 8;
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  padding: 9px 12px;
  border-bottom: 1px solid #d3dbe7;
  background: #fff;
  color: #667085;
  font-size: 10px;
}

.preview-status.loading { color: #3157d5; }

.preview-status > span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-zoom {
  display: flex;
  flex: none;
  gap: 4px;
  align-items: center;
}

.preview-zoom button,
.preview-zoom select {
  height: 26px;
  border: 1px solid #cbd5e1;
  border-radius: 5px;
  background: #fff;
  color: #475467;
  font: 10px/1 system-ui, sans-serif;
}

.preview-zoom button {
  min-width: 27px;
  padding: 0 7px;
  cursor: pointer;
}

.preview-zoom button:hover:not(:disabled),
.preview-zoom select:hover {
  border-color: #7890d8;
  color: #2949b8;
}

.preview-zoom button:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.preview-zoom select {
  padding: 0 4px;
}

.preview-canvas {
  max-height: 720px;
  overflow: auto;
  padding: 18px;
}

.preview-zoom-content {
  --preview-zoom: 1;
  zoom: var(--preview-zoom);
}

:deep(.docx-viewer-wrapper) {
  box-sizing: border-box;
  width: max-content;
  min-width: 100%;
}

:deep(.docx-viewer__document),
:deep(.docx-wrapper) {
  width: max-content;
  min-width: 100%;
  max-width: none;
  margin-inline: auto;
}

:deep(.docx-wrapper) {
  display: flex !important;
  flex-direction: column;
  align-items: center;
}

:deep(.docx-wrapper > section.docx) {
  flex: none;
  margin-right: auto !important;
  margin-left: auto !important;
}

@supports not (zoom: 1) {
  .preview-zoom-content {
    transform: scale(var(--preview-zoom));
    transform-origin: top left;
  }
}

:deep(.structure-node-host) {
  position: relative;
  z-index: 4;
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-height: 28px;
  margin: 8px 0;
  padding: 0;
  overflow: visible;
  pointer-events: none;
}

:deep(.structure-node-host::before),
:deep(.structure-node-host::after) {
  height: 1px;
  flex: 1;
  background: #b7c3d6;
  content: "";
}

:deep(.structure-node) {
  max-width: 72%;
  overflow: hidden;
  padding: 4px 9px;
  border: 1px solid #9aabd0;
  border-radius: 999px;
  background: #f7f9ff;
  color: #3157a8;
  font: 11px/1.3 system-ui, sans-serif;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
  pointer-events: auto;
}

:deep(.structure-node:hover),
:deep(.structure-node.is-pending) {
  border-color: #3157d5;
  background: #e7edff;
}

:deep(.structure-node.is-split) {
  border-color: #16865b;
  background: #e8f7f0;
  color: #08734a;
}

:deep(.structure-node.is-fixed) {
  border-color: #9aa4b2;
  background: #f2f4f7;
  color: #667085;
  cursor: default;
}

:deep(.structure-preview-hidden),
:deep(.structure-preview-page-hidden) {
  display: none !important;
}
</style>
