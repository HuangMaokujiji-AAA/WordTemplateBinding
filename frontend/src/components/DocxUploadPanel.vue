<script setup lang="ts">
import { ref } from "vue";

const emit = defineEmits<{
  (e: "file-selected", file: File): void;
  (e: "load-sample"): void;
  (e: "clear"): void;
  (e: "rescan"): void;
  (e: "export-reusable"): void;
  (e: "generate"): void;
}>();

defineProps<{
  fileName: string;
  fileSize: string;
  statusMessage: string;
  loading: boolean;
  hasTemplate: boolean;
  bindingCount: number;
}>();

const fileInput = ref<HTMLInputElement | null>(null);
const dragActive = ref(false);

function handleFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (file) {
    emit("file-selected", file);
  }
}

function handleDragOver(event: DragEvent) {
  event.preventDefault();
  dragActive.value = true;
}

function handleDrop(event: DragEvent) {
  event.preventDefault();
  dragActive.value = false;
  const file = event.dataTransfer?.files?.[0];
  if (file) {
    emit("file-selected", file);
  }
}

function handleClear() {
  if (fileInput.value) {
    fileInput.value.value = "";
  }
  emit("clear");
}

function triggerFileInput() {
  fileInput.value?.click();
}
</script>

<template>
  <div
    class="upload-panel"
    :class="{ 'is-drag-active': dragActive }"
    @dragover="handleDragOver"
    @dragleave="dragActive = false"
    @drop="handleDrop"
  >
    <div class="upload-panel__toolbar">
      <div class="upload-panel__brand">
        <span class="upload-panel__mark">W</span>
        <div>
          <h1 class="upload-panel__title">Word 模板可视化数据绑定</h1>
          <p>Vue 文档预览 · ECharts 原生图表近似渲染 · C# 报告生成</p>
        </div>
      </div>

      <div class="upload-panel__actions">
        <button
          class="btn btn--primary"
          @click="triggerFileInput"
          :disabled="loading"
        >
          上传 DOCX
        </button>

        <button
          class="btn btn--secondary"
          @click="$emit('load-sample')"
          :disabled="loading"
        >
          示例
        </button>

        <button
          class="btn btn--secondary"
          @click="$emit('rescan')"
          :disabled="loading || !hasTemplate"
        >
          重新扫描
        </button>

        <button
          class="btn btn--template"
          @click="$emit('export-reusable')"
          :disabled="loading || !hasTemplate || bindingCount === 0"
        >
          导出复用模板
        </button>

        <button
          class="btn btn--success"
          @click="$emit('generate')"
          :disabled="loading || !hasTemplate || bindingCount === 0"
        >
          生成报告
        </button>

        <button
          class="btn btn--ghost"
          @click="handleClear"
          :disabled="loading || !fileName"
        >
          清空
        </button>

        <input
          ref="fileInput"
          type="file"
          accept=".docx"
          class="upload-panel__input"
          @change="handleFileChange"
        />
      </div>
    </div>

    <div v-if="fileName" class="upload-panel__info">
      <span class="upload-panel__info-label">文件：</span>
      <span class="upload-panel__info-value">{{ fileName }}</span>
      <span class="upload-panel__info-separator">|</span>
      <span class="upload-panel__info-label">大小：</span>
      <span class="upload-panel__info-value">{{ fileSize }}</span>
      <span class="upload-panel__info-separator">|</span>
      <span class="upload-panel__info-label">状态：</span>
      <span
        class="upload-panel__info-value"
        :class="{ 'text--error': statusMessage.includes('失败') || statusMessage.includes('错误') }"
      >
        {{ statusMessage }}
      </span>
    </div>

    <div v-else class="upload-panel__hint">
      将 .docx 拖到这里。浏览器只负责近似预览与绑定操作，最终替换由后端在原始 DOCX 副本上完成。
    </div>
  </div>
</template>

<style scoped>
.upload-panel {
  position: relative;
  z-index: 20;
  padding: 13px 20px;
  border-bottom: 1px solid #34415c;
  background: #17213a;
  color: white;
  box-shadow: 0 2px 12px rgb(16 24 40 / 18%);
  transition: background 0.15s;
}

.upload-panel.is-drag-active {
  background: #22345d;
}

.upload-panel__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}

.upload-panel__brand {
  display: flex;
  align-items: center;
  gap: 11px;
}

.upload-panel__mark {
  display: grid;
  width: 39px;
  height: 39px;
  place-items: center;
  border-radius: 9px;
  background: #3157d5;
  font: 700 22px Georgia, serif;
}

.upload-panel__title {
  font-size: 17px;
  font-weight: 600;
  margin: 0;
  color: white;
  white-space: nowrap;
}

.upload-panel__brand p {
  margin: 2px 0 0;
  color: #b9c2d5;
  font-size: 11px;
}

.upload-panel__actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.upload-panel__input {
  display: none;
}

.upload-panel__info {
  margin-top: 9px;
  font-size: 12px;
  color: #b9c2d5;
  display: flex;
  flex-wrap: wrap;
  gap: 4px 0;
}

.upload-panel__info-label {
  color: #8f9bb2;
}

.upload-panel__info-value {
  color: #e9edf5;
  font-weight: 500;
}

.upload-panel__info-separator {
  margin: 0 8px;
  color: #52617f;
}

.upload-panel__hint {
  margin-top: 8px;
  font-size: 11px;
  color: #b9c2d5;
  text-align: center;
}

.text--error {
  color: #e74c3c;
}

.btn {
  padding: 8px 16px;
  border: none;
  border-radius: 6px;
  font-size: 13px;
  cursor: pointer;
  font-weight: 500;
  white-space: nowrap;
  transition: opacity 0.15s;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn--primary {
  background: #4472c4;
  color: white;
}

.btn--primary:hover:not(:disabled) {
  background: #3462b4;
}

.btn--secondary {
  background: #6c757d;
  color: white;
}

.btn--secondary:hover:not(:disabled) {
  background: #5a6268;
}

.btn--danger {
  background: #e74c3c;
  color: white;
}

.btn--danger:hover:not(:disabled) {
  background: #c0392b;
}

.btn--success {
  background: #16805c;
  color: white;
}

.btn--success:hover:not(:disabled) {
  background: #0f6a4b;
}

.btn--template {
  background: #8b5a2b;
  color: white;
}

.btn--template:hover:not(:disabled) {
  background: #74491f;
}

.btn--ghost {
  border: 1px solid #52617f;
  background: transparent;
  color: #dbe2ef;
}

.btn--ghost:hover:not(:disabled) {
  background: #26334f;
}

@media (max-width: 1180px) {
  .upload-panel__brand p {
    display: none;
  }
}
</style>
