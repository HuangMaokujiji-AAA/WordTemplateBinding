<script setup lang="ts">
import { ref } from "vue";

defineProps<{
  visible: boolean;
}>();

const styleContainerRef = ref<HTMLDivElement | null>(null);
const documentContainerRef = ref<HTMLDivElement | null>(null);

defineExpose({
  getStyleContainer(): HTMLDivElement | null {
    return styleContainerRef.value;
  },
  getDocumentContainer(): HTMLDivElement | null {
    return documentContainerRef.value;
  },
});
</script>

<template>
  <div class="docx-viewer-wrapper">
    <div ref="styleContainerRef" class="docx-viewer__styles"></div>
    <div
      ref="documentContainerRef"
      class="docx-viewer__document"
      :class="{ 'is-empty': !visible }"
    ></div>
    <div v-if="!visible" class="docx-viewer__placeholder">
      <span>DOCX</span>
      <h2>上传模板后开始绑定</h2>
      <p>正文、表格和图片由 docx-preview 渲染，Word 原生图表由 ECharts 近似重绘。</p>
    </div>
  </div>
</template>

<style scoped>
.docx-viewer-wrapper {
  position: relative;
}

.docx-viewer__styles {
  display: none;
}

.docx-viewer__document {
  position: relative;
}

.docx-viewer__document.is-empty {
  min-height: 760px;
  background: white;
}

.docx-viewer__placeholder {
  position: absolute;
  inset: 110px 24px auto;
  display: grid;
  place-items: center;
  color: #667085;
  text-align: center;
}

.docx-viewer__placeholder span {
  display: grid;
  width: 82px;
  height: 104px;
  place-items: center;
  border: 2px solid #9eabc0;
  border-radius: 9px;
  font-weight: 700;
}

.docx-viewer__placeholder h2 {
  margin: 18px 0 5px;
  color: #344054;
  font-size: 18px;
}

.docx-viewer__placeholder p {
  max-width: 460px;
  margin: 0;
  font-size: 12px;
}
</style>
