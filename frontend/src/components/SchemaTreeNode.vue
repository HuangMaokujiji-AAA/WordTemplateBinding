<script setup lang="ts">
import { computed, ref } from "vue";
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
const tooltipId = computed(
  () => `schema-detail-${props.node.path.replace(/[^a-zA-Z0-9_-]/g, "-")}`
);
const descendantCount = computed(() => countDescendants(props.node.children));
const structureFields = computed(() =>
  flattenStructure(props.node.children).slice(0, 16)
);
const sampleValue = computed(() => formatSampleValue(props.node.sampleValueJson));

interface StructureField {
  node: DataFieldNode;
  depth: number;
}

function countDescendants(nodes: DataFieldNode[]): number {
  return nodes.reduce(
    (total, node) => total + 1 + countDescendants(node.children),
    0
  );
}

function flattenStructure(
  nodes: DataFieldNode[],
  depth = 0
): StructureField[] {
  return nodes.flatMap((node) => [
    { node, depth },
    ...flattenStructure(node.children, depth + 1),
  ]);
}

function formatSampleValue(value: string | null | undefined): string {
  if (!value) return "—";
  try {
    const parsed = JSON.parse(value) as unknown;
    const formatted = typeof parsed === "string"
      ? parsed
      : JSON.stringify(parsed, null, 2);
    return truncateSample(formatted || value);
  } catch {
    return truncateSample(value);
  }
}

function truncateSample(value: string): string {
  const normalized = value.replace(/\r\n?/g, "\n");
  return normalized.length > 1200
    ? `${normalized.slice(0, 1200)}\n…（示例值内容已截断）`
    : normalized;
}

function toggle(): void {
  if (props.node.children.length > 0) {
    expanded.value = !expanded.value;
  }
}

function selectField(): void {
  if (props.node.isBindable) {
    emit("field-selected", props.node);
  } else if (props.node.children.length > 0) {
    toggle();
  }
}

function handleDragStart(event: DragEvent): void {
  if (!props.node.isBindable || !event.dataTransfer) {
    event.preventDefault();
    return;
  }

  event.dataTransfer.effectAllowed = "copyLink";
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
        :draggable="node.isBindable"
        :class="{ 'is-bindable': node.isBindable }"
        :aria-describedby="tooltipId"
        :title="
          node.isBindable
            ? node.type === 'Array'
              ? '点击绑定到当前选中图表或表格；表格还需确认列映射'
              : '点击绑定到当前选中值，或拖拽到文档高亮处；拖到已绑定高亮可直接改绑'
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

    <aside :id="tooltipId" class="schema-field-tooltip" role="tooltip">
      <header>
        <strong>{{ node.name }}</strong>
        <span>{{ node.type }}{{ node.isCollection ? " · 集合" : "" }}</span>
      </header>
      <dl>
        <div><dt>完整路径</dt><dd>{{ node.path }}</dd></div>
        <div><dt>允许为空</dt><dd>{{ node.isNullable == null ? "—" : node.isNullable ? "是" : "否" }}</dd></div>
        <div><dt>可绑定</dt><dd>{{ node.isBindable ? "是" : "否" }}</dd></div>
        <div v-if="node.comment"><dt>字段说明</dt><dd>{{ node.comment }}</dd></div>
        <div><dt>示例值</dt><dd class="schema-sample-value">{{ sampleValue }}</dd></div>
      </dl>
      <section v-if="descendantCount > 0">
        <strong>
          {{ node.isCollection ? "数组单条记录字段" : "内部字段结构" }}（{{ descendantCount }} 项）
        </strong>
        <ul>
          <li
            v-for="field in structureFields"
            :key="field.node.path"
            :style="{ paddingLeft: `${field.depth * 12}px` }"
          >
            <span>{{ field.node.name }}</span>
            <small>{{ field.node.type }} · {{ field.node.path }}</small>
          </li>
        </ul>
        <p v-if="descendantCount > structureFields.length">
          另有 {{ descendantCount - structureFields.length }} 项，请展开字段树查看。
        </p>
      </section>
      <p v-else class="schema-leaf-note">叶子字段，无下级数据结构。</p>
    </aside>

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

<style scoped>
.schema-node {
  position: relative;
}

.schema-field-tooltip {
  position: absolute;
  z-index: 30;
  top: 34px;
  right: 4px;
  left: 4px;
  display: grid;
  gap: 10px;
  padding: 12px;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 12px 30px rgb(15 23 42 / 18%);
  color: #1e293b;
  font-size: 10px;
  line-height: 1.45;
  opacity: 0;
  visibility: hidden;
  pointer-events: auto;
  transform: translateY(-3px);
  transition: opacity 120ms ease, transform 120ms ease, visibility 120ms ease;
}

.schema-row:hover + .schema-field-tooltip,
.schema-row:focus-within + .schema-field-tooltip,
.schema-field-tooltip:hover {
  opacity: 1;
  visibility: visible;
  transform: translateY(0);
}

.schema-field-tooltip header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid #e2e8f0;
}

.schema-field-tooltip header span,
.schema-field-tooltip dt,
.schema-field-tooltip small,
.schema-field-tooltip p {
  color: #64748b;
}

.schema-field-tooltip dl,
.schema-field-tooltip section,
.schema-field-tooltip ul {
  display: grid;
  gap: 6px;
  margin: 0;
}

.schema-field-tooltip dl > div {
  display: grid;
  grid-template-columns: 62px minmax(0, 1fr);
  gap: 8px;
}

.schema-field-tooltip dt,
.schema-field-tooltip dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.schema-sample-value {
  min-width: 0;
  max-height: 160px;
  overflow: auto;
  padding: 7px 8px;
  border-radius: 5px;
  background: #f8fafc;
  font-family: Consolas, "Courier New", monospace;
  line-height: 1.5;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.schema-field-tooltip ul {
  max-height: 190px;
  overflow: hidden;
  padding: 0;
  list-style: none;
}

.schema-field-tooltip li {
  display: grid;
  grid-template-columns: minmax(80px, 0.7fr) minmax(0, 1.3fr);
  gap: 6px;
}

.schema-field-tooltip li span,
.schema-field-tooltip li small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.schema-field-tooltip p,
.schema-leaf-note {
  margin: 0;
}
</style>
