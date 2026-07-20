<script setup lang="ts">
import { ref } from "vue";
import type { DataFieldNode } from "../api/types";
import { FIELD_MIME_TYPE } from "../features/binding/renderedDocumentBindings";

defineOptions({ name: "SchemaTreeNode" });

const props = withDefaults(
  defineProps<{
    node: DataFieldNode;
    depth?: number;
  }>(),
  { depth: 0 }
);

const emit = defineEmits<{
  (event: "field-selected", field: DataFieldNode): void;
}>();

const expanded = ref(false);

function toggle(): void {
  if (props.node.children.length > 0) {
    expanded.value = !expanded.value;
  }
}

function selectField(): void {
  if (props.node.isLeaf && props.node.isBindable) {
    emit("field-selected", props.node);
  } else if (props.node.children.length > 0) {
    toggle();
  }
}

function handleDragStart(event: DragEvent): void {
  if (!props.node.isLeaf || !props.node.isBindable || !event.dataTransfer) {
    event.preventDefault();
    return;
  }

  event.dataTransfer.effectAllowed = "copy";
  event.dataTransfer.setData(FIELD_MIME_TYPE, JSON.stringify(props.node));
}
</script>

<template>
  <div class="schema-node">
    <div class="schema-row" :style="{ paddingLeft: `${depth * 14 + 4}px` }">
      <button
        class="schema-toggle"
        type="button"
        :disabled="node.children.length === 0"
        :aria-label="`${expanded ? '折叠' : '展开'} ${node.name}`"
        :aria-expanded="expanded"
        @click="toggle"
      >
        {{ node.children.length ? (expanded ? "▾" : "▸") : "·" }}
      </button>

      <button
        class="schema-label"
        type="button"
        :draggable="node.isLeaf && node.isBindable"
        :class="{ 'is-bindable': node.isLeaf && node.isBindable }"
        :title="
          node.isLeaf && node.isBindable
            ? '点击绑定到当前选中值，或拖拽到文档高亮处'
            : node.isLeaf
              ? '该字段当前不可绑定'
              : '展开字段分组'
        "
        @click="selectField"
        @dragstart="handleDragStart"
      >
        <strong>{{ node.name }}</strong>
        <small>{{ node.path }}</small>
      </button>

      <span class="type-badge">{{ node.type }}</span>
    </div>

    <div v-if="expanded" class="schema-children">
      <SchemaTreeNode
        v-for="child in node.children"
        :key="child.path"
        :node="child"
        :depth="depth + 1"
        @field-selected="emit('field-selected', $event)"
      />
    </div>
  </div>
</template>
