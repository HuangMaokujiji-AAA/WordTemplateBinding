<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import {
  listChapters,
  listDataFields,
  listDataSources,
  listProjects,
  refreshDataSource,
} from "../../../api/client";
import type {
  ChapterRecord,
  DataFieldRecord,
  DataSourceRecord,
  ProjectRecord,
} from "../../../api/types";
import type {
  TemplateStudioContext,
  TemplateStudioContextPatch,
} from "../types";

const props = defineProps<{ context: TemplateStudioContext }>();
const emit = defineEmits<{
  "update-context": [patch: TemplateStudioContextPatch];
  complete: [patch?: TemplateStudioContextPatch];
}>();

const projects = ref<ProjectRecord[]>([]);
const chapters = ref<ChapterRecord[]>([]);
const sources = ref<DataSourceRecord[]>([]);
const fields = ref<DataFieldRecord[]>([]);
const projectId = ref(props.context.projectId);
const chapterId = ref(props.context.chapterId);
const dataSourceId = ref(props.context.dataSourceId);
const query = ref("");
const loading = ref(false);
const message = ref("");
const isError = ref(false);
let initializing = true;

const visibleFields = computed(() => {
  const keyword = query.value.trim().toLowerCase();
  if (!keyword) return fields.value;
  return fields.value.filter((field) =>
    [field.fieldPath, field.fieldName, field.comment || ""]
      .join(" ")
      .toLowerCase()
      .includes(keyword)
  );
});

async function loadProjects(): Promise<void> {
  loading.value = true;
  try {
    const response = await listProjects({ pageSize: 100 });
    projects.value = response.items;
    projectId.value = response.items.some(
      (item) => item.projectId === props.context.projectId
    )
      ? props.context.projectId
      : response.items[0]?.projectId || "";
    await loadProjectContext();
  } catch (error) {
    showError(error, "加载项目与数据源失败。");
  } finally {
    initializing = false;
    loading.value = false;
  }
}

async function loadProjectContext(): Promise<void> {
  chapters.value = [];
  sources.value = [];
  fields.value = [];
  if (!projectId.value) return;
  loading.value = true;
  try {
    const [chapterItems, sourceItems] = await Promise.all([
      listChapters(projectId.value),
      listDataSources(projectId.value),
    ]);
    chapters.value = chapterItems;
    sources.value = sourceItems;
    chapterId.value = chapterItems.some(
      (item) => item.id === props.context.chapterId
    )
      ? props.context.chapterId
      : chapterItems[0]?.id || "";
    dataSourceId.value = sourceItems.some(
      (item) => item.id === props.context.dataSourceId
    )
      ? props.context.dataSourceId
      : sourceItems[0]?.id || "";
    await loadFields();
    syncContext();
  } catch (error) {
    showError(error, "加载项目上下文失败。");
  } finally {
    loading.value = false;
  }
}

async function loadFields(): Promise<void> {
  fields.value = dataSourceId.value
    ? await listDataFields(dataSourceId.value)
    : [];
}

async function refresh(): Promise<void> {
  if (!dataSourceId.value) return;
  loading.value = true;
  try {
    const result = await refreshDataSource(dataSourceId.value);
    await loadFields();
    message.value = `数据快照已刷新，状态：${result.snapshotStatus}。`;
    isError.value = false;
  } catch (error) {
    showError(error, "刷新数据快照失败。");
  } finally {
    loading.value = false;
  }
}

function syncContext(): void {
  emit("update-context", {
    projectId: projectId.value,
    chapterId: chapterId.value,
    dataSourceId: dataSourceId.value,
  });
}

function finish(): void {
  emit("complete", {
    projectId: projectId.value,
    chapterId: chapterId.value,
    dataSourceId: dataSourceId.value,
  });
}

function showError(error: unknown, fallback: string): void {
  message.value = error instanceof Error ? error.message : fallback;
  isError.value = true;
}

watch(projectId, () => {
  if (!initializing) void loadProjectContext();
});

watch(chapterId, syncContext);

watch(dataSourceId, async () => {
  if (initializing) return;
  loading.value = true;
  try {
    await loadFields();
    syncContext();
  } catch (error) {
    showError(error, "加载字段快照失败。");
  } finally {
    loading.value = false;
  }
});

onMounted(() => void loadProjects());
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>连接数据源</h2>
        <p>选择项目、章节和数据源快照。这里保存的是后续绑定所需的工作上下文，不会修改模板 DOCX。</p>
      </div>
      <button
        type="button"
        class="studio-button"
        :disabled="loading || !dataSourceId"
        @click="refresh"
      >
        刷新快照
      </button>
    </header>

    <div class="studio-step-body">
      <div class="studio-form-grid">
        <label class="studio-field">
          <span>项目</span>
          <select v-model="projectId" :disabled="loading">
            <option value="">请选择项目</option>
            <option
              v-for="item in projects"
              :key="item.projectId"
              :value="item.projectId"
            >
              {{ item.projectCode }} · {{ item.projectName }}
            </option>
          </select>
        </label>
        <label class="studio-field">
          <span>章节</span>
          <select v-model="chapterId" :disabled="loading || !projectId">
            <option value="">请选择章节</option>
            <option v-for="item in chapters" :key="item.id" :value="item.id">
              {{ item.chapterCode }} · {{ item.title }}
            </option>
          </select>
        </label>
        <label class="studio-field">
          <span>数据源</span>
          <select v-model="dataSourceId" :disabled="loading || !projectId">
            <option value="">请选择数据源</option>
            <option v-for="item in sources" :key="item.id" :value="item.id">
              {{ item.sourceCode }} · {{ item.sourceName }}
            </option>
          </select>
        </label>
        <label class="studio-field">
          <span>搜索字段</span>
          <input v-model="query" placeholder="路径、字段名或说明" />
        </label>
      </div>

      <div v-if="dataSourceId" class="field-panel">
        <div class="field-panel-heading">
          <strong>可绑定字段</strong>
          <span>{{ visibleFields.length }} / {{ fields.length }}</span>
        </div>
        <div v-if="visibleFields.length" class="field-list">
          <article v-for="field in visibleFields" :key="field.id">
            <div>
              <strong>{{ field.comment || field.fieldName }}</strong>
              <code>{{ field.fieldPath }}</code>
            </div>
            <span>{{ field.dataType }}{{ field.isArray ? "[]" : "" }}</span>
            <small>{{ field.sampleValue ?? "无样例" }}</small>
          </article>
        </div>
        <div v-else class="studio-empty">当前快照没有匹配的可绑定字段。</div>
      </div>

      <div v-if="message" class="studio-message" :class="{ error: isError }">
        {{ message }}
      </div>
      <div class="studio-actions">
        <button
          type="button"
          class="studio-button primary"
          :disabled="loading || !projectId || !chapterId || !dataSourceId"
          @click="finish"
        >
          数据上下文已确认，开始绑定
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.field-panel {
  margin-top: 18px;
  border: 1px solid #e0e6ef;
  border-radius: 10px;
  overflow: hidden;
}

.field-panel-heading {
  display: flex;
  justify-content: space-between;
  padding: 11px 14px;
  background: #f6f8fb;
  color: #475467;
  font-size: 12px;
}

.field-list {
  max-height: 330px;
  overflow: auto;
}

.field-list article {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 90px 140px;
  gap: 12px;
  align-items: center;
  padding: 10px 14px;
  border-top: 1px solid #edf0f5;
  color: #475467;
  font-size: 11px;
}

.field-list article div {
  display: grid;
  gap: 3px;
}

.field-list code,
.field-list small {
  overflow: hidden;
  color: #7b8799;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
