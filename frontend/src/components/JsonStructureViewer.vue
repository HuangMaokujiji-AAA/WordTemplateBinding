<script setup lang="ts">
import { computed, ref } from "vue";

const props = defineProps<{
  data: unknown;
  title?: string;
}>();

const expanded = ref(false);
const copyLabel = ref("复制");

// Text-only formatting — never dangerouslySetInnerHTML/v-html. Very long
// string values inside the payload are truncated so a pathological cache
// dump can't blow up the DOM.
const formatted = computed(() => {
  try {
    return JSON.stringify(props.data, truncatingReplacer, 2);
  } catch {
    return "(无法序列化为 JSON)";
  }
});

const MAX_STRING_LENGTH = 500;

function truncatingReplacer(_key: string, value: unknown): unknown {
  if (typeof value === "string" && value.length > MAX_STRING_LENGTH) {
    return `${value.slice(0, MAX_STRING_LENGTH)}…(截断，共 ${value.length} 字符)`;
  }
  return value;
}

function toggle(): void {
  expanded.value = !expanded.value;
}

async function copy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(formatted.value);
    copyLabel.value = "已复制";
    setTimeout(() => (copyLabel.value = "复制"), 1500);
  } catch {
    copyLabel.value = "复制失败";
    setTimeout(() => (copyLabel.value = "复制"), 1500);
  }
}
</script>

<template>
  <div class="json-viewer">
    <button type="button" class="json-viewer__toggle" @click="toggle">
      {{ expanded ? "收起" : "展开" }} {{ props.title ?? "结构化 JSON" }}
    </button>
    <div v-if="expanded" class="json-viewer__body">
      <button type="button" class="json-viewer__copy" @click="copy">{{ copyLabel }}</button>
      <pre class="json-viewer__pre">{{ formatted }}</pre>
    </div>
  </div>
</template>

<style scoped>
.json-viewer {
  margin-top: 8px;
}

.json-viewer__toggle {
  background: none;
  border: 1px solid #ddd;
  border-radius: 4px;
  padding: 4px 10px;
  font-size: 12px;
  color: #4472c4;
  cursor: pointer;
}

.json-viewer__body {
  position: relative;
  margin-top: 6px;
}

.json-viewer__copy {
  position: absolute;
  top: 6px;
  right: 6px;
  background: white;
  border: 1px solid #ddd;
  border-radius: 4px;
  padding: 2px 8px;
  font-size: 11px;
  cursor: pointer;
}

.json-viewer__pre {
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 12px;
  border-radius: 6px;
  font-size: 11px;
  line-height: 1.5;
  max-height: 360px;
  overflow: auto;
  white-space: pre;
}
</style>
