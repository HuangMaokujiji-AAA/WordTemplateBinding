<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import {
  getTemplateStudioWorkspace,
  saveTemplateSegmentBoundaries,
} from "../../../api/client";
import type {
  TemplateOutlineBlock,
  TemplateStudioWorkspace,
} from "../../../api/types";
import StudioStructurePreview from "../components/StudioStructurePreview.vue";
import {
  buildBoundaryDrafts,
  buildContiguousPartitions,
  buildSegmentMetadataDefaults,
  findSegmentMetadataValidationIssue,
  validateSegmentMetadata,
} from "../segmentPartitions";
import type { SegmentMetadata } from "../segmentPartitions";
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
const splitIndexes = ref<number[]>([]);
const pendingIndex = ref<number | null>(null);
const selectedStartIndex = ref(0);
const previewMode = ref<"document" | "partition">("document");
const metadataByStart = ref<Record<number, SegmentMetadata>>({});
const initialized = ref(false);
const stepRoot = ref<HTMLElement | null>(null);

const rootBlocks = computed(() =>
  (workspace.value?.outline.blocks || []).filter((block) => block.canSelect)
);

const partitions = computed(() =>
  buildContiguousPartitions(rootBlocks.value, splitIndexes.value)
);

const selectedPartition = computed(
  () =>
    partitions.value.find(
      (partition) => partition.startIndex === selectedStartIndex.value
    ) || partitions.value[0] || null
);

const selectedMetadata = computed<SegmentMetadata | null>(() => {
  const partition = selectedPartition.value;
  return partition ? metadataByStart.value[partition.startIndex] || null : null;
});

const previewRange = computed(() => {
  if (previewMode.value !== "partition") return null;
  const partition = selectedPartition.value;
  return partition
    ? { startIndex: partition.startIndex, endIndex: partition.endIndex }
    : null;
});

const validationMessage = computed(() =>
  validateSegmentMetadata(partitions.value, metadataByStart.value)
);

const validationIssue = computed(() =>
  findSegmentMetadataValidationIssue(partitions.value, metadataByStart.value)
);

const hasSavedPartitions = computed(() =>
  (workspace.value?.segments || []).some(
    (segment) => segment.anchorType === "CONTENT_CONTROL"
  )
);

const pendingIsSaved = computed(
  () =>
    pendingIndex.value !== null && splitIndexes.value.includes(pendingIndex.value)
);

watch(
  [splitIndexes, metadataByStart],
  () => {
    if (initialized.value && !hasSavedPartitions.value) emit("dirty-change", true);
  },
  { deep: true }
);

watch(validationMessage, (value, previousValue) => {
  if (isError.value && message.value === previousValue && value !== previousValue) {
    message.value = "";
    isError.value = false;
  }
});

watch(
  partitions,
  (value) => {
    const activeStarts = new Set(value.map((partition) => partition.startIndex));
    const next = buildSegmentMetadataDefaults(value, metadataByStart.value);
    if (
      Object.keys(metadataByStart.value).some(
        (start) => !activeStarts.has(Number(start))
      ) ||
      value.some((partition) => !metadataByStart.value[partition.startIndex])
    ) {
      metadataByStart.value = next;
    } else {
      value.forEach((partition) => {
        Object.assign(metadataByStart.value[partition.startIndex], next[partition.startIndex]);
      });
    }
    if (!activeStarts.has(selectedStartIndex.value)) {
      selectedStartIndex.value = value[0]?.startIndex || 0;
    }
  },
  { immediate: true }
);

async function loadWorkspace(versionId = props.context.versionId): Promise<void> {
  if (!props.context.templateId) return;
  initialized.value = false;
  loading.value = true;
  message.value = "";
  isError.value = false;
  try {
    workspace.value = await getTemplateStudioWorkspace(
      props.context.templateId,
      { versionId: versionId || undefined }
    );
    splitIndexes.value = [];
    pendingIndex.value = null;
    selectedStartIndex.value = 0;
    previewMode.value = "document";
    metadataByStart.value = {
      0: { segmentName: "划分块1", segmentKey: "segment-1" },
    };
    const resolvedVersionId = workspace.value.versionView.version.id;
    emit("update-context", {
      versionId: resolvedVersionId,
      segmentId: workspace.value.segments[0]?.id || "",
    });
    emit("dirty-change", false);
    initialized.value = true;
  } catch (error) {
    showError(error instanceof Error ? error.message : "加载模板结构失败。");
  } finally {
    loading.value = false;
  }
}

function selectNode(index: number): void {
  if (index <= 0 || index >= rootBlocks.value.length) return;
  pendingIndex.value = index;
  const action = splitIndexes.value.includes(index) ? "移除" : "添加";
  message.value = `已选择“${blockLabel(rootBlocks.value[index])}”前的节点，确认后将${action}该划分节点。`;
  isError.value = false;
}

function confirmNode(): void {
  const index = pendingIndex.value;
  if (index === null) return;
  if (pendingIsSaved.value) {
    splitIndexes.value = splitIndexes.value.filter((item) => item !== index);
    selectedStartIndex.value =
      [...splitIndexes.value, 0]
        .filter((item) => item < index)
        .sort((left, right) => right - left)[0] || 0;
    message.value = "已移除划分节点，相邻两个划分块已合并。";
  } else {
    if (partitions.value.length >= 50) {
      showError("一次最多可以创建 50 个划分块。");
      return;
    }
    splitIndexes.value = [...splitIndexes.value, index].sort(
      (left, right) => left - right
    );
    selectedStartIndex.value = index;
    message.value = `已添加划分节点，当前共有 ${partitions.value.length} 个划分块。`;
  }
  pendingIndex.value = null;
  isError.value = false;
}

function selectPartition(startIndex: number): void {
  selectedStartIndex.value = startIndex;
  previewMode.value = "partition";
}

function selectDocumentPreview(): void {
  previewMode.value = "document";
}

async function saveAndContinue(): Promise<void> {
  if (!workspace.value) return;
  const issue = validationIssue.value;
  if (issue) {
    if (issue.startIndex !== null) {
      selectedStartIndex.value = issue.startIndex;
      previewMode.value = "partition";
    }
    showError(issue.message);
    await nextTick();
    if (issue.field) {
      stepRoot.value
        ?.querySelector<HTMLInputElement>(`[data-segment-field="${issue.field}"]`)
        ?.focus();
    }
    return;
  }

  const drafts = buildBoundaryDrafts(
    partitions.value,
    metadataByStart.value
  );
  loading.value = true;
  message.value = "正在保存全部划分块并写入一个新的 DOCX 版本…";
  isError.value = false;
  try {
    const result = await saveTemplateSegmentBoundaries(
      workspace.value.versionView.version.id,
      {
        expectedContentHash: workspace.value.outline.contentHash,
        boundaries: drafts,
      }
    );
    emit("dirty-change", false);
    emit("complete", {
      versionId: result.version.id,
      segmentId: "",
    });
  } catch (error) {
    showError(
      error instanceof Error ? error.message : "保存文档划分失败。"
    );
  } finally {
    loading.value = false;
  }
}

function continueSavedStructure(): void {
  emit("complete", {
    versionId: workspace.value?.versionView.version.id,
    segmentId: workspace.value?.segments[0]?.id || "",
  });
}

function showError(value: string): void {
  message.value = value;
  isError.value = true;
}

function blockLabel(block: TemplateOutlineBlock | undefined): string {
  if (!block) return "未知位置";
  const text = block.displayText.trim() || "空白块";
  return text.length > 42 ? `${text.slice(0, 42)}…` : text;
}

function formatFileSize(value: number | undefined): string {
  if (!value || value < 1024) return `${value || 0} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

onMounted(() => void loadWorkspace());
</script>

<template>
  <section ref="stepRoot" class="studio-step-card structure-step">
    <header class="studio-step-header">
      <div>
        <h2>确认报告结构</h2>
        <p>
          在 Word 预览中选择块与块之间的划分节点。文档开头和结尾固定，
          所有内容会按节点顺序完整且不重复地归入一个划分块。
        </p>
      </div>
    </header>

    <div v-if="!context.templateId" class="studio-step-body">
      <div class="studio-empty">请先完成第 1 步创建模板。</div>
    </div>

    <div v-else-if="hasSavedPartitions" class="studio-step-body">
      <div class="saved-structure">
        <div>
          <strong>当前版本已经完成结构划分</strong>
          <span>共 {{ workspace?.segments.length || 0 }} 个片段，片段名称和片段键已写入 Word。</span>
        </div>
        <div class="saved-segments">
          <span v-for="segment in workspace?.segments || []" :key="segment.id">
            <strong>{{ segment.segmentName }}</strong>
            <code>{{ segment.segmentKey }}</code>
          </span>
        </div>
        <button
          type="button"
          class="studio-button primary"
          :disabled="loading"
          @click="continueSavedStructure"
        >
          结构确认完成，继续
        </button>
      </div>
    </div>

    <div v-else class="structure-grid">
      <aside class="segment-column">
        <div class="column-heading">
          <strong>当前划分文档</strong>
          <span>完整预览</span>
        </div>
        <button
          type="button"
          class="document-item"
          :class="{ active: previewMode === 'document' }"
          @click="selectDocumentPreview"
        >
          <strong>{{ workspace?.versionView.file.originalName || "未命名文档" }}</strong>
          <span>{{ workspace?.versionView.template.templateName }}</span>
          <dl>
            <div>
              <dt>版本</dt>
              <dd>V{{ workspace?.versionView.version.versionNo || 1 }}</dd>
            </div>
            <div>
              <dt>大小</dt>
              <dd>{{ formatFileSize(workspace?.versionView.file.fileSize) }}</dd>
            </div>
            <div>
              <dt>正文块</dt>
              <dd>{{ rootBlocks.length }}</dd>
            </div>
            <div>
              <dt>划分块</dt>
              <dd>{{ partitions.length }}</dd>
            </div>
          </dl>
          <small>点击查看完整文档及全部划分节点</small>
        </button>

        <div class="column-heading">
          <strong>当前划分块</strong>
          <span>{{ partitions.length }} 个</span>
        </div>
        <button
          v-for="(partition, index) in partitions"
          :key="partition.startIndex"
          type="button"
          class="segment-item"
          :class="{
            active:
              previewMode === 'partition' &&
              partition.startIndex === selectedPartition?.startIndex,
          }"
          @click="selectPartition(partition.startIndex)"
        >
          <strong>
            {{ metadataByStart[partition.startIndex]?.segmentName || `划分块${index + 1}` }}
          </strong>
          <span>起始位置：{{ blockLabel(partition.startBlock) }}</span>
          <small>
            第 {{ partition.startIndex + 1 }}～{{ partition.endIndex + 1 }} 个正文块
          </small>
        </button>
      </aside>

      <StudioStructurePreview
        :version-view="workspace?.versionView || null"
        :blocks="rootBlocks"
        :split-indexes="splitIndexes"
        :pending-index="pendingIndex"
        :preview-range="previewRange"
        @select-node="selectNode"
      />

      <aside class="boundary-column">
        <div class="column-heading">
          <strong>划分节点</strong>
          <span>先选择，再确认</span>
        </div>

        <div class="node-panel">
          <p v-if="pendingIndex === null">
            请在 Word 预览中点击一个内部划分节点。开头和结尾节点无需添加。
          </p>
          <template v-else>
            <span>当前节点位于</span>
            <strong>{{ blockLabel(rootBlocks[pendingIndex]) }} 之前</strong>
            <button type="button" class="studio-button" @click="confirmNode">
              {{ pendingIsSaved ? "确定移除划分节点" : "确定添加划分节点" }}
            </button>
          </template>
        </div>

        <div v-if="selectedPartition && selectedMetadata" class="fragment-editor">
          <div class="column-heading">
            <strong>片段信息</strong>
            <span>划分块 {{ partitions.indexOf(selectedPartition) + 1 }}</span>
          </div>
          <label class="studio-field">
            <span>片段名称</span>
            <input
              v-model="selectedMetadata.segmentName"
              data-segment-field="segmentName"
              :aria-invalid="
                validationIssue?.startIndex === selectedPartition.startIndex &&
                validationIssue?.field === 'segmentName'
              "
              maxlength="255"
              placeholder="请输入片段名称"
            />
          </label>
          <label class="studio-field">
            <span>片段键</span>
            <input
              v-model="selectedMetadata.segmentKey"
              data-segment-field="segmentKey"
              :aria-invalid="
                validationIssue?.startIndex === selectedPartition.startIndex &&
                validationIssue?.field === 'segmentKey'
              "
              maxlength="64"
              placeholder="例如：overview"
            />
            <small>仅支持小写字母、数字和短横线，且不可重复。</small>
          </label>
          <dl>
            <div>
              <dt>起始位置</dt>
              <dd>{{ blockLabel(selectedPartition.startBlock) }}</dd>
            </div>
            <div>
              <dt>结束位置</dt>
              <dd>{{ blockLabel(selectedPartition.endBlock) }}</dd>
            </div>
          </dl>
        </div>

        <div v-if="message" class="studio-message" :class="{ error: isError }">
          {{ message }}
        </div>
        <div
          v-if="validationMessage && validationMessage !== message"
          class="studio-message error"
        >
          {{ validationMessage }}
        </div>

        <button
          type="button"
          class="studio-button primary continue-button"
          :disabled="loading"
          @click="saveAndContinue"
        >
          保存划分并进入下一步
        </button>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.structure-grid {
  display: grid;
  grid-template-columns: 230px minmax(520px, 1fr) 310px;
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

.document-item {
  display: grid;
  gap: 6px;
  width: 100%;
  margin-bottom: 16px;
  padding: 11px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.document-item > strong,
.document-item > span,
.document-item > small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.document-item > span,
.document-item > small {
  color: #7b8799;
  font-size: 9px;
}

.document-item dl {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 5px;
  margin: 2px 0;
}

.document-item dl div {
  display: flex;
  justify-content: space-between;
  gap: 5px;
  padding: 5px 6px;
  border-radius: 5px;
  background: #f5f7fb;
}

.document-item dt {
  color: #8a95a6;
  font-size: 8px;
}

.document-item dd {
  margin: 0;
  color: #475467;
  font-size: 9px;
  font-weight: 600;
}

.segment-item {
  display: grid;
  gap: 4px;
  width: 100%;
  margin-bottom: 6px;
  padding: 10px;
  border: 1px solid transparent;
  border-radius: 7px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.segment-item span,
.segment-item small {
  overflow: hidden;
  color: #7b8799;
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.segment-item.active,
.document-item.active {
  border-color: #b8c7f7;
  background: #eef2ff;
  color: #2949b8;
}

.node-panel,
.fragment-editor {
  display: grid;
  gap: 9px;
  margin-bottom: 14px;
  padding: 11px;
  border: 1px solid #dbe3ef;
  border-radius: 8px;
  background: #fff;
  color: #667085;
  font-size: 10px;
}

.node-panel p { margin: 0; line-height: 1.6; }
.node-panel strong { color: #344054; }

.fragment-editor .studio-field { display: grid; gap: 5px; }
.fragment-editor .studio-field small { color: #8a95a6; font-size: 9px; }

.fragment-editor dl {
  display: grid;
  gap: 7px;
  margin: 3px 0 0;
  padding-top: 10px;
  border-top: 1px solid #eaecf0;
}

.fragment-editor dl div { display: grid; gap: 2px; }
.fragment-editor dt { color: #8a95a6; font-size: 9px; }
.fragment-editor dd { margin: 0; color: #475467; }

.continue-button {
  width: 100%;
  margin-top: 14px;
}

.saved-structure { display: grid; gap: 16px; }
.saved-structure > div:first-child { display: grid; gap: 5px; }
.saved-structure span { color: #667085; font-size: 11px; }
.saved-segments { display: flex; flex-wrap: wrap; gap: 8px; }
.saved-segments span {
  display: grid;
  gap: 3px;
  padding: 9px 12px;
  border: 1px solid #dbe3ef;
  border-radius: 8px;
  background: #fff;
}
.saved-segments code { color: #3157a8; }

@media (max-width: 1180px) {
  .structure-grid { grid-template-columns: 210px minmax(430px, 1fr); }
  .boundary-column { grid-column: 1 / -1; }
}
</style>
