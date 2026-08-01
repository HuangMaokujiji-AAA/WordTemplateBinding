<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import WpsPdfPreview from "./WpsPdfPreview.vue";
import PreviewModeToggle from "./PreviewModeToggle.vue";
import {
  getWpsStatus,
  type WpsStatus,
  type AnchorInfo,
} from "../api/client";

type PreviewMode = "web" | "wps";

interface Props {
  templateVersionId?: string;
  segmentId?: string;
  wpsPreviewUrl?: string;
  wpsAnchors?: AnchorInfo[];
  wpsLoading?: boolean;
  wpsError?: string;
  webPreviewReady?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
  wpsAnchors: () => [],
  wpsLoading: false,
  wpsError: "",
  webPreviewReady: false,
});

const emit = defineEmits<{
  wpsPreviewLoad: [];
  modeChange: [mode: PreviewMode];
}>();

const previewMode = ref<PreviewMode>("web");
const wpsStatus = ref<WpsStatus | null>(null);
const currentScale = ref(1.0);
const scrollMode = ref<"single" | "continuous">("continuous");

const wpsAvailable = computed(() => wpsStatus.value?.isAvailable ?? false);

function handleModeChange(mode: PreviewMode): void {
  previewMode.value = mode;
  emit("modeChange", mode);
  if (mode === "wps") {
    emit("wpsPreviewLoad");
  }
}

function handleAnchorClick(anchor: AnchorInfo): void {
  console.log("锚点点击:", anchor);
}

async function loadWpsStatus(): Promise<void> {
  try {
    wpsStatus.value = await getWpsStatus();
  } catch (error) {
    wpsStatus.value = {
      isWindows: true,
      isAvailable: false,
      message: `检测失败: ${error}`,
    };
  }
}

onMounted(() => {
  void loadWpsStatus();
});
</script>

<template>
  <div class="workspace-preview">
    <div class="preview-header">
      <div class="preview-title">
        <span v-if="previewMode === 'web'">网页预览</span>
        <span v-else>WPS 真实预览</span>
      </div>
      <PreviewModeToggle
        :current-mode="previewMode"
        :wps-available="wpsAvailable"
        @update:mode="handleModeChange"
      />
    </div>

    <div class="preview-content">
      <div v-if="previewMode === 'web'" class="web-preview-placeholder">
        <div class="placeholder-inner" v-if="!webPreviewReady">
          <span class="placeholder-icon">🌐</span>
          <h3>网页预览模式</h3>
          <p>在此模式下使用 docx-preview 渲染文档</p>
        </div>
        <slot v-else name="web-preview"></slot>
      </div>

      <WpsPdfPreview
        v-else
        :pdf-url="wpsPreviewUrl"
        :anchors="wpsAnchors"
        :scale="currentScale"
        :scroll-mode="scrollMode"
        :is-loading="wpsLoading"
        :error-message="wpsError"
        @anchor-click="handleAnchorClick"
        @scale-change="(s) => (currentScale = s)"
      />
    </div>

    <div class="preview-footer">
      <div class="preview-mode-hint">
        <template v-if="previewMode === 'web'">
          网页预览：快速渲染，适合布局查看
        </template>
        <template v-else>
          WPS 预览：真实分页，保留 Word 排版
        </template>
      </div>
      <div class="wps-status-indicator" v-if="previewMode === 'wps'">
        <span
          class="status-dot"
          :class="{ available: wpsAvailable, unavailable: !wpsAvailable }"
        ></span>
        <span>{{
          wpsAvailable
            ? `WPS ${wpsStatus?.progId || "可用"}`
            : "WPS 不可用"
        }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.workspace-preview {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #f8fafc;
  border-radius: 8px;
  overflow: hidden;
}

.preview-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
}

.preview-title span {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
}

.preview-content {
  flex: 1;
  overflow: hidden;
  position: relative;
}

.web-preview-placeholder {
  width: 100%;
  height: 100%;
  overflow: auto;
  display: flex;
  align-items: center;
  justify-content: center;
}

.placeholder-inner {
  text-align: center;
  color: #64748b;
}

.placeholder-icon {
  font-size: 48px;
  display: block;
  margin-bottom: 16px;
}

.placeholder-inner h3 {
  margin: 0 0 8px;
  font-size: 18px;
  color: #334155;
}

.placeholder-inner p {
  margin: 0;
  font-size: 14px;
}

.preview-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 16px;
  background: #ffffff;
  border-top: 1px solid #e2e8f0;
  font-size: 12px;
  color: #64748b;
}

.preview-mode-hint {
  font-style: italic;
}

.wps-status-indicator {
  display: flex;
  align-items: center;
  gap: 6px;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.status-dot.available {
  background: #22c55e;
}

.status-dot.unavailable {
  background: #ef4444;
}
</style>
