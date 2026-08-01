<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";

interface Anchor {
  anchorName: string;
  pageNumber: number;
  bounds: { x: number; y: number; width: number; height: number };
  targetType: "placeholder" | "table" | "chart";
  targetId: string;
  boundDataPath?: string;
  displayText?: string;
}

interface Props {
  pdfUrl?: string;
  pdfData?: ArrayBuffer;
  anchors?: Anchor[];
  scale?: number;
  scrollMode?: "single" | "continuous";
  isLoading?: boolean;
  errorMessage?: string;
}

const props = withDefaults(defineProps<Props>(), {
  anchors: () => [],
  scale: 1.0,
  scrollMode: "single",
  isLoading: false,
  errorMessage: "",
});

const emit = defineEmits<{
  anchorClick: [anchor: Anchor];
  pageChange: [page: number, total: number];
  scaleChange: [scale: number];
}>();

const containerRef = ref<HTMLDivElement | null>(null);
const canvasContainerRef = ref<HTMLDivElement | null>(null);
const currentPage = ref(1);
const totalPages = ref(0);
const currentScale = ref(props.scale);
const internalScrollMode = ref<"single" | "continuous">(props.scrollMode);
const isLoading = computed(() => props.isLoading);
const errorMessage = computed(() => props.errorMessage);

const visibleAnchors = computed(() =>
  props.anchors.filter((a) => a.pageNumber === currentPage.value)
);

function getPageAnchors(pageNum: number): Anchor[] {
  return props.anchors.filter((a) => a.pageNumber === pageNum);
}

function handleAnchorClick(anchor: Anchor): void {
  emit("anchorClick", anchor);
}

function goToPage(page: number): void {
  if (page >= 1 && page <= totalPages.value) {
    currentPage.value = page;
    emit("pageChange", page, totalPages.value);
    scrollToPage(page);
  }
}

function previousPage(): void {
  goToPage(currentPage.value - 1);
}

function nextPage(): void {
  goToPage(currentPage.value + 1);
}

function zoomIn(): void {
  const newScale = Math.min(currentScale.value + 0.25, 3.0);
  setScale(newScale);
}

function zoomOut(): void {
  const newScale = Math.max(currentScale.value - 0.25, 0.5);
  setScale(newScale);
}

function fitToWidth(): void {
  if (!containerRef.value) return;
  const containerWidth = containerRef.value.clientWidth - 48;
  const baseWidth = 595 * 72;
  const newScale = containerWidth / baseWidth;
  setScale(Math.min(Math.max(newScale, 0.5), 3.0));
}

function setScale(scale: number): void {
  currentScale.value = Math.min(Math.max(scale, 0.5), 3.0);
  emit("scaleChange", currentScale.value);
}

function toggleScrollMode(): void {
  internalScrollMode.value =
    internalScrollMode.value === "single" ? "continuous" : "single";
}

function scrollToPage(page: number): void {
  const pages = canvasContainerRef.value?.querySelectorAll(".pdf-page");
  if (pages && pages[page - 1]) {
    (pages[page - 1] as HTMLElement).scrollIntoView({
      behavior: "smooth",
      block: "start",
    });
  }
}

onMounted(() => {
  console.log("WpsPdfPreview mounted, waiting for PDF data...");
});

onUnmounted(() => {
  console.log("WpsPdfPreview unmounted");
});

watch(
  () => props.scale,
  (newScale) => {
    currentScale.value = newScale;
  }
);
</script>

<template>
  <div class="wps-pdf-preview" ref="containerRef">
    <div class="preview-toolbar">
      <div class="toolbar-left">
        <span class="page-info">
          第 {{ currentPage }} / {{ totalPages || "?" }} 页
        </span>
      </div>

      <div class="toolbar-center">
        <button
          type="button"
          class="toolbar-btn"
          :disabled="currentPage <= 1"
          @click="previousPage"
          title="上一页"
        >
          ◀
        </button>
        <input
          type="number"
          class="page-input"
          :value="currentPage"
          min="1"
          :max="totalPages || 1"
          @change="(e) => goToPage(Number((e.target as HTMLInputElement).value))"
        />
        <button
          type="button"
          class="toolbar-btn"
          :disabled="currentPage >= totalPages"
          @click="nextPage"
          title="下一页"
        >
          ▶
        </button>
      </div>

      <div class="toolbar-right">
        <button
          type="button"
          class="toolbar-btn"
          @click="zoomOut"
          title="缩小"
        >
          −
        </button>
        <span class="scale-display">{{ Math.round(currentScale * 100) }}%</span>
        <button
          type="button"
          class="toolbar-btn"
          @click="zoomIn"
          title="放大"
        >
          +
        </button>
        <button
          type="button"
          class="toolbar-btn"
          @click="fitToWidth"
          title="适合宽度"
        >
          ↔
        </button>
        <button
          type="button"
          class="toolbar-btn mode-toggle"
          :class="{ active: internalScrollMode === 'continuous' }"
          @click="toggleScrollMode"
          :title="internalScrollMode === 'single' ? '切换到连续模式' : '切换到单页模式'"
        >
          {{ internalScrollMode === "single" ? "单页" : "连续" }}
        </button>
      </div>
    </div>

    <div
      class="preview-content"
      :class="{
        'scroll-continuous': internalScrollMode === 'continuous',
        'scroll-single': internalScrollMode === 'single',
      }"
      ref="canvasContainerRef"
    >
      <div v-if="isLoading" class="loading-state">
        <span class="loading-spinner"></span>
        正在加载 PDF 预览...
      </div>

      <div v-else-if="errorMessage" class="error-state">
        {{ errorMessage }}
      </div>

      <div v-else-if="!pdfUrl && !pdfData" class="empty-state">
        <span class="placeholder-icon">📄</span>
        <h3>WPS 真实预览</h3>
        <p>加载模板后将显示 WPS 生成的真实 PDF 预览</p>
        <p class="hint">
          此预览模式需要 WPS Office 支持，可保留 Word 的真实分页、图表和排版。
        </p>
      </div>

      <template v-else>
        <div
          class="pdf-page"
          :style="{ transform: `scale(${currentScale})`, transformOrigin: 'top center' }"
        >
          <div class="page-canvas-placeholder">
            <span>PDF 页面 {{ currentPage }}</span>
          </div>

          <div
            v-for="anchor in visibleAnchors"
            :key="anchor.anchorName"
            class="anchor-overlay"
            :style="{
              left: `${anchor.bounds.x}px`,
              top: `${anchor.bounds.y}px`,
              width: `${anchor.bounds.width}px`,
              height: `${anchor.bounds.height}px`,
            }"
            :class="{ bound: !!anchor.boundDataPath }"
            :title="`${anchor.displayText || anchor.anchorName}${anchor.boundDataPath ? ` → ${anchor.boundDataPath}` : ''}`"
            @click.stop="handleAnchorClick(anchor)"
          >
            <span class="anchor-label">
              {{ anchor.displayText || anchor.anchorName }}
            </span>
          </div>
        </div>
      </template>
    </div>

    <div class="preview-legend">
      <span class="legend-item">
        <span class="legend-box unbound"></span>
        未绑定
      </span>
      <span class="legend-item">
        <span class="legend-box bound"></span>
        已绑定
      </span>
    </div>
  </div>
</template>

<style scoped>
.wps-pdf-preview {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #f8f9fa;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  overflow: hidden;
}

.preview-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 16px;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  gap: 16px;
  flex-wrap: wrap;
}

.toolbar-left,
.toolbar-center,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-info {
  font-size: 14px;
  color: #475569;
  font-weight: 500;
}

.toolbar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  height: 32px;
  padding: 0 8px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  color: #475569;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.toolbar-btn:hover:not(:disabled) {
  background: #f1f5f9;
  border-color: #94a3b8;
}

.toolbar-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.toolbar-btn.mode-toggle {
  font-size: 12px;
  padding: 0 12px;
}

.toolbar-btn.mode-toggle.active {
  background: #2563eb;
  border-color: #2563eb;
  color: #ffffff;
}

.page-input {
  width: 60px;
  height: 32px;
  padding: 0 8px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  text-align: center;
  font-size: 14px;
}

.page-input:focus {
  outline: none;
  border-color: #2563eb;
}

.scale-display {
  min-width: 50px;
  text-align: center;
  font-size: 13px;
  color: #64748b;
}

.preview-content {
  flex: 1;
  overflow: auto;
  padding: 24px;
}

.preview-content.scroll-continuous {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.preview-content.scroll-single {
  display: flex;
  justify-content: center;
}

.pdf-page {
  position: relative;
  width: 595px;
  min-height: 842px;
  background: #ffffff;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  margin-bottom: 16px;
}

.scroll-continuous .pdf-page {
  margin-bottom: 0;
}

.page-canvas-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 842px;
  color: #94a3b8;
  font-size: 14px;
}

.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 64px 24px;
  text-align: center;
  color: #64748b;
}

.loading-spinner {
  display: inline-block;
  width: 32px;
  height: 32px;
  border: 3px solid #e2e8f0;
  border-top-color: #2563eb;
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 16px;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.placeholder-icon {
  font-size: 48px;
  margin-bottom: 16px;
}

.empty-state h3 {
  margin: 0 0 8px;
  color: #334155;
  font-size: 18px;
}

.empty-state p {
  margin: 0 0 8px;
  font-size: 14px;
}

.empty-state .hint {
  font-size: 12px;
  color: #94a3b8;
  max-width: 400px;
}

.error-state {
  color: #dc2626;
}

.anchor-overlay {
  position: absolute;
  background: rgba(255, 235, 59, 0.3);
  border: 2px solid #ffc107;
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s ease;
  z-index: 10;
}

.anchor-overlay:hover {
  background: rgba(255, 235, 59, 0.5);
  border-color: #ff9800;
  transform: scale(1.02);
}

.anchor-overlay.bound {
  background: rgba(76, 175, 80, 0.2);
  border-color: #4caf50;
}

.anchor-overlay.bound:hover {
  background: rgba(76, 175, 80, 0.4);
  border-color: #2e7d32;
}

.anchor-label {
  font-size: 11px;
  color: #5d4037;
  background: rgba(255, 255, 255, 0.9);
  padding: 2px 6px;
  border-radius: 3px;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-legend {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 24px;
  padding: 8px 16px;
  background: #ffffff;
  border-top: 1px solid #e2e8f0;
  font-size: 12px;
  color: #64748b;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.legend-box {
  width: 16px;
  height: 16px;
  border-radius: 3px;
  border: 2px solid;
}

.legend-box.unbound {
  background: rgba(255, 235, 59, 0.3);
  border-color: #ffc107;
}

.legend-box.bound {
  background: rgba(76, 175, 80, 0.2);
  border-color: #4caf50;
}
</style>
