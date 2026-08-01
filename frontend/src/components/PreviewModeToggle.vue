<script setup lang="ts">
import { computed } from "vue";

export type PreviewMode = "web" | "wps";

interface Props {
  currentMode: PreviewMode;
  wpsAvailable: boolean;
  disabled?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
  disabled: false,
});

const emit = defineEmits<{
  "update:mode": [mode: PreviewMode];
}>();

const modes: { value: PreviewMode; label: string; description: string }[] = [
  {
    value: "web",
    label: "网页预览",
    description: "使用 docx-preview 渲染，适合快速预览",
  },
  {
    value: "wps",
    label: "WPS 真实预览",
    description: "使用 WPS 导出 PDF，保留真实分页",
  },
];

function selectMode(mode: PreviewMode): void {
  if (mode === "wps" && !props.wpsAvailable) {
    return;
  }
  emit("update:mode", mode);
}

const isWebSelected = computed(() => props.currentMode === "web");
const isWpsSelected = computed(() => props.currentMode === "wps");
</script>

<template>
  <div class="preview-mode-toggle" :class="{ disabled }">
    <div class="toggle-label">预览模式</div>
    <div class="toggle-buttons">
      <button
        v-for="mode in modes"
        :key="mode.value"
        type="button"
        class="mode-btn"
        :class="{
          selected: currentMode === mode.value,
          unavailable: mode.value === 'wps' && !wpsAvailable,
        }"
        :disabled="disabled || (mode.value === 'wps' && !wpsAvailable)"
        :title="mode.value === 'wps' && !wpsAvailable ? 'WPS 不可用，请安装 Windows 桌面版 WPS' : mode.description"
        @click="selectMode(mode.value)"
      >
        <span class="mode-icon">
          {{ mode.value === "web" ? "🌐" : "📄" }}
        </span>
        <span class="mode-label">{{ mode.label }}</span>
        <span v-if="mode.value === 'wps' && !wpsAvailable" class="unavailable-badge">
          不可用
        </span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.preview-mode-toggle {
  display: flex;
  align-items: center;
  gap: 12px;
}

.preview-mode-toggle.disabled {
  opacity: 0.6;
}

.toggle-label {
  font-size: 13px;
  color: #64748b;
  font-weight: 500;
}

.toggle-buttons {
  display: flex;
  gap: 8px;
}

.mode-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  color: #475569;
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s ease;
  position: relative;
}

.mode-btn:hover:not(:disabled) {
  background: #f8fafc;
  border-color: #94a3b8;
}

.mode-btn.selected {
  background: #eff6ff;
  border-color: #2563eb;
  color: #1d4ed8;
  font-weight: 500;
}

.mode-btn:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.mode-btn.unavailable {
  border-color: #e2e8f0;
  color: #94a3b8;
}

.mode-icon {
  font-size: 14px;
}

.mode-label {
  white-space: nowrap;
}

.unavailable-badge {
  font-size: 10px;
  padding: 2px 4px;
  background: #f1f5f9;
  border-radius: 3px;
  color: #94a3b8;
}

.mode-btn.selected .unavailable-badge {
  background: #dbeafe;
  color: #2563eb;
}
</style>
