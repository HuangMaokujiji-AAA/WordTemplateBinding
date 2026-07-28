<script setup lang="ts">
import {
  computed,
  onMounted,
  ref,
  watch,
} from "vue";
import {
  getTemplateStudioWorkspace,
  removeTemplateSegmentBoundary,
  saveTemplateSegmentBoundaries,
} from "../../../api/client";
import type {
  TemplateOutlineBlock,
  TemplateSegmentBoundaryDraft,
  TemplateSegmentRecord,
  TemplateStudioWorkspace,
} from "../../../api/types";
import StudioSegmentPreview from "../components/StudioSegmentPreview.vue";
import type {
  TemplateStudioContext,
  TemplateStudioContextPatch,
} from "../types";

const props = defineProps<{
  context: TemplateStudioContext;
}>();

const emit = defineEmits<{
  "update-context": [patch: TemplateStudioContextPatch];
  "dirty-change": [dirty: boolean];
  complete: [patch?: TemplateStudioContextPatch];
}>();

const workspace = ref<TemplateStudioWorkspace | null>(null);
const loading = ref(false);
const message = ref("");
const isError = ref(false);
const selectedSegmentId = ref("");
const drafts = ref<TemplateSegmentBoundaryDraft[]>([]);
const form = ref<TemplateSegmentBoundaryDraft>({
  segmentKey: "",
  segmentName: "",
  startBlockId: "",
  endBlockId: "",
});

const selectedSegment = computed<TemplateSegmentRecord | null>(
  () =>
    workspace.value?.segments.find(
      (segment) => segment.id === selectedSegmentId.value
    ) || null
);

const outlineBlocks = computed(() => {
  const flatten = (items: TemplateOutlineBlock[]): TemplateOutlineBlock[] =>
    items.flatMap((item) => [item, ...flatten(item.children)]);
  return flatten(workspace.value?.outline.blocks || []).filter(
    (item) => item.canSelect
  );
});

const endBlocks = computed(() => {
  const start = outlineBlocks.value.find(
    (item) => item.blockId === form.value.startBlockId
  );
  if (!start) return outlineBlocks.value;
  const parent = parentPath(start.blockId);
  return outlineBlocks.value.filter(
    (item) => parentPath(item.blockId) === parent
  );
});

watch(
  drafts,
  (value) => emit("dirty-change", value.length > 0),
  { deep: true }
);

watch(
  () => form.value.startBlockId,
  () => {
    if (
      !endBlocks.value.some(
        (item) => item.blockId === form.value.endBlockId
      )
    ) {
      form.value.endBlockId = endBlocks.value[0]?.blockId || "";
    }
  }
);

async function loadWorkspace(versionId = props.context.versionId): Promise<void> {
  if (!props.context.templateId) return;
  loading.value = true;
  message.value = "";
  isError.value = false;
  try {
    workspace.value = await getTemplateStudioWorkspace(
      props.context.templateId,
      { versionId: versionId || undefined }
    );
    const resolvedVersionId = workspace.value.versionView.version.id;
    selectedSegmentId.value =
      workspace.value.segments.find(
        (segment) => segment.id === props.context.segmentId
      )?.id ||
      workspace.value.segments[0]?.id ||
      "";
    form.value.startBlockId =
      outlineBlocks.value.find((item) => !item.segmentKey)?.blockId ||
      outlineBlocks.value[0]?.blockId ||
      "";
    form.value.endBlockId = form.value.startBlockId;
    emit("update-context", {
      versionId: resolvedVersionId,
      segmentId: selectedSegmentId.value,
    });
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "加载模板结构失败。";
    isError.value = true;
  } finally {
    loading.value = false;
  }
}

function selectSegment(segmentId: string): void {
  selectedSegmentId.value = segmentId;
  emit("update-context", { segmentId });
}

function addDraft(): void {
  const draft = {
    ...form.value,
    segmentKey: form.value.segmentKey.trim(),
    segmentName: form.value.segmentName.trim(),
  };
  if (
    !draft.segmentKey ||
    !draft.segmentName ||
    !draft.startBlockId ||
    !draft.endBlockId
  ) {
    showError("请填写片段名称、片段键并选择起止块。");
    return;
  }
  if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(draft.segmentKey)) {
    showError("片段键只能包含小写字母、数字和短横线。");
    return;
  }
  if (
    drafts.value.some((item) => item.segmentKey === draft.segmentKey) ||
    workspace.value?.segments.some(
      (item) => item.segmentKey === draft.segmentKey
    )
  ) {
    showError(`片段键“${draft.segmentKey}”已经存在。`);
    return;
  }

  drafts.value.push(draft);
  form.value = {
    segmentKey: "",
    segmentName: "",
    startBlockId: draft.endBlockId,
    endBlockId: draft.endBlockId,
  };
  message.value = "已加入待保存划分；可以继续添加其他范围。";
  isError.value = false;
}

async function saveDrafts(): Promise<void> {
  if (!workspace.value || drafts.value.length === 0) return;
  loading.value = true;
  message.value = "正在把全部边界写入一个 DOCX 副本…";
  isError.value = false;
  try {
    const result = await saveTemplateSegmentBoundaries(
      workspace.value.versionView.version.id,
      {
        expectedContentHash: workspace.value.outline.contentHash,
        boundaries: drafts.value,
      }
    );
    drafts.value = [];
    emit("dirty-change", false);
    await loadWorkspace(result.version.id);
    message.value = `全部边界已写入新模板版本 v${result.version.versionNo}。`;
  } catch (error) {
    showError(
      error instanceof Error ? error.message : "批量保存片段边界失败。"
    );
  } finally {
    loading.value = false;
  }
}

async function removeBoundary(segment: TemplateSegmentRecord): Promise<void> {
  if (
    !workspace.value ||
    segment.anchorType !== "CONTENT_CONTROL" ||
    !window.confirm(
      `确定删除“${segment.segmentName}”的边界吗？正文会保留，并创建一个新模板版本。`
    )
  ) {
    return;
  }
  loading.value = true;
  try {
    const result = await removeTemplateSegmentBoundary(
      workspace.value.versionView.version.id,
      segment.segmentKey,
      workspace.value.outline.contentHash
    );
    await loadWorkspace(result.version.id);
    message.value = `边界已删除，正文保留在 v${result.version.versionNo}。`;
    isError.value = false;
  } catch (error) {
    showError(
      error instanceof Error ? error.message : "删除片段边界失败。"
    );
  } finally {
    loading.value = false;
  }
}

function showError(value: string): void {
  message.value = value;
  isError.value = true;
}

function parentPath(blockId: string): string {
  return blockId.slice(0, blockId.lastIndexOf("/"));
}

function blockLabel(block: TemplateOutlineBlock): string {
  return `${"　".repeat(block.depth)}${block.displayText}`;
}

onMounted(() => void loadWorkspace());
</script>

<template>
  <section class="studio-step-card structure-step">
    <header class="studio-step-header">
      <div>
        <h2>确认报告结构</h2>
        <p>
          标题只用于显示和建议；保存后的稳定边界是写入 DOCX 的
          <code>wtb:segment:片段键</code> 内容控件。
        </p>
      </div>
      <button
        type="button"
        class="studio-button primary"
        :disabled="loading || drafts.length === 0"
        @click="saveDrafts"
      >
        保存并写回（{{ drafts.length }}）
      </button>
    </header>

    <div v-if="!context.templateId" class="studio-step-body">
      <div class="studio-empty">请先完成第 1 步创建模板。</div>
    </div>
    <div v-else class="structure-grid">
      <aside class="segment-column">
        <div class="column-heading">
          <strong>报告片段</strong>
          <span>{{ workspace?.segments.length || 0 }} 个</span>
        </div>
        <button
          v-for="segment in workspace?.segments || []"
          :key="segment.id"
          type="button"
          class="segment-item"
          :class="{ active: segment.id === selectedSegmentId }"
          @click="selectSegment(segment.id)"
        >
          <strong>{{ segment.segmentName }}</strong>
          <span>{{ segment.segmentKey }} · {{ segment.elementCount }} 元素</span>
        </button>
      </aside>

      <StudioSegmentPreview :segment="selectedSegment" />

      <aside class="boundary-column">
        <div class="column-heading">
          <strong>边界管理</strong>
          <span>批量创建一个版本</span>
        </div>
        <div class="boundary-form">
          <label class="studio-field">
            <span>片段名称</span>
            <input
              v-model="form.segmentName"
              maxlength="255"
              placeholder="例如：专业监测结果"
            />
          </label>
          <label class="studio-field">
            <span>片段键</span>
            <input
              v-model="form.segmentKey"
              maxlength="64"
              placeholder="例如：major-results"
            />
          </label>
          <label class="studio-field">
            <span>起始块</span>
            <select v-model="form.startBlockId">
              <option
                v-for="block in outlineBlocks"
                :key="block.blockId"
                :value="block.blockId"
              >
                {{ blockLabel(block) }}
              </option>
            </select>
          </label>
          <label class="studio-field">
            <span>结束块</span>
            <select v-model="form.endBlockId">
              <option
                v-for="block in endBlocks"
                :key="block.blockId"
                :value="block.blockId"
              >
                {{ blockLabel(block) }}
              </option>
            </select>
          </label>
          <button
            type="button"
            class="studio-button"
            :disabled="loading"
            @click="addDraft"
          >
            加入待保存划分
          </button>
        </div>

        <div v-if="drafts.length > 0" class="draft-list">
          <div v-for="(draft, index) in drafts" :key="draft.segmentKey">
            <span>
              <strong>{{ draft.segmentName }}</strong>
              {{ draft.startBlockId }} → {{ draft.endBlockId }}
            </span>
            <button type="button" @click="drafts.splice(index, 1)">撤销</button>
          </div>
        </div>

        <div
          v-if="message"
          class="studio-message"
          :class="{ error: isError }"
        >
          {{ message }}
        </div>

        <div
          v-if="selectedSegment?.anchorType === 'CONTENT_CONTROL'"
          class="saved-boundary"
        >
          <strong>已保存边界</strong>
          <span>{{ selectedSegment.segmentKey }}</span>
          <button
            type="button"
            class="studio-button danger"
            @click="removeBoundary(selectedSegment)"
          >
            删除边界并创建新版本
          </button>
        </div>
        <button
          type="button"
          class="studio-button primary continue-button"
          :disabled="loading || drafts.length > 0"
          @click="emit('complete')"
        >
          结构确认完成，继续
        </button>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.structure-grid {
  display: grid;
  grid-template-columns: 210px minmax(480px, 1fr) 300px;
  gap: 12px;
  padding: 14px;
}

.segment-column,
.boundary-column {
  min-width: 0;
  padding: 12px;
  border: 1px solid #dde4ee;
  border-radius: 10px;
  background: #f9fbfd;
}

.column-heading {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
  margin-bottom: 10px;
  color: #344054;
  font-size: 11px;
}

.column-heading span {
  color: #8a95a6;
  font-size: 9px;
}

.segment-item {
  display: grid;
  gap: 2px;
  width: 100%;
  margin-bottom: 5px;
  padding: 9px;
  border: 1px solid transparent;
  border-radius: 7px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.segment-item span {
  color: #7b8799;
  font-size: 9px;
}

.segment-item.active {
  border-color: #b8c7f7;
  background: #eef2ff;
  color: #2949b8;
}

.boundary-form {
  display: grid;
  gap: 10px;
}

.draft-list {
  display: grid;
  gap: 6px;
  margin-top: 14px;
}

.draft-list > div {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding: 8px;
  border: 1px solid #dbe3ef;
  border-radius: 7px;
  background: #fff;
  color: #667085;
  font-size: 9px;
}

.draft-list strong,
.draft-list span {
  display: block;
}

.draft-list button {
  border: 0;
  background: transparent;
  color: #b42318;
  font-size: 9px;
}

.saved-boundary {
  display: grid;
  gap: 7px;
  margin-top: 14px;
  padding-top: 13px;
  border-top: 1px solid #dfe5ee;
  color: #667085;
  font-size: 10px;
}

.continue-button {
  width: 100%;
  margin-top: 14px;
}

code {
  color: #2949b8;
}
</style>
