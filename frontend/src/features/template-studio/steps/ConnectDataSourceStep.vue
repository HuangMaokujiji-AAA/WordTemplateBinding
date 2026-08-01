<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import {
  createHigherEducationDataSource,
  getOrCreateBindingSet,
  listChapters,
  listDataFields,
  listDataSources,
  listHigherEducationSchools,
  listHigherEducationYears,
  listProjects,
  refreshDataSource,
  resolveBindingCandidates,
} from "../../../api/client";
import type {
  ChapterRecord,
  DataFieldRecord,
  DataSourceRecord,
  HigherEducationSchool,
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
const higherEducationYears = ref<string[]>([]);
const higherEducationSchools = ref<HigherEducationSchool[]>([]);
const higherEducationYear = ref("2024");
const higherEducationSchoolCode = ref("");
const creatingHigherEducation = ref(false);
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

async function loadHigherEducationCatalog(): Promise<void> {
  try {
    higherEducationYears.value = await listHigherEducationYears();
    if (
      higherEducationYears.value.length > 0 &&
      !higherEducationYears.value.includes(higherEducationYear.value)
    ) {
      higherEducationYear.value = higherEducationYears.value[0];
    }
    await loadHigherEducationSchools();
  } catch {
    higherEducationYears.value = [];
    higherEducationSchools.value = [];
  }
}

async function loadHigherEducationSchools(): Promise<void> {
  higherEducationSchools.value = higherEducationYear.value
    ? await listHigherEducationSchools(higherEducationYear.value)
    : [];
  if (
    !higherEducationSchools.value.some(
      (item) => item.schoolCode === higherEducationSchoolCode.value
    )
  ) {
    higherEducationSchoolCode.value =
      higherEducationSchools.value[0]?.schoolCode || "";
  }
}

async function createMonitoringDataSource(): Promise<void> {
  if (!projectId.value || !higherEducationSchoolCode.value) return;
  creatingHigherEducation.value = true;
  try {
    const result = await createHigherEducationDataSource({
      projectId: projectId.value,
      collectionYear: higherEducationYear.value,
      schoolCode: higherEducationSchoolCode.value,
    });
    const existingIndex = sources.value.findIndex(
      (item) => item.id === result.source.id
    );
    if (existingIndex >= 0) sources.value.splice(existingIndex, 1, result.source);
    else sources.value.unshift(result.source);
    dataSourceId.value = result.source.id;
    message.value = `已构造 ${result.source.sourceName}，共读取 ${result.snapshot.rowCount ?? 0} 行监测数据。`;
    isError.value = false;
  } catch (error) {
    showError(error, "构造高校监测数据源失败。");
  } finally {
    creatingHigherEducation.value = false;
  }
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

async function finish(): Promise<void> {
  if (!chapterId.value || !dataSourceId.value) return;
  loading.value = true;
  try {
    if (props.context.versionId) {
      const bindingSet = await getOrCreateBindingSet(
        chapterId.value,
        props.context.versionId
      );
      const summary = await resolveBindingCandidates(
        bindingSet.id,
        dataSourceId.value
      );
      const tableCount = summary.tableBindingsRestored || 0;
      message.value = `自动绑定完成：${summary.textBindingsRestored} 个文本、${summary.chartBindingsRestored} 个图表、${tableCount} 个表格。`;
      isError.value = false;
    }
    emit("complete", {
      projectId: projectId.value,
      chapterId: chapterId.value,
      dataSourceId: dataSourceId.value,
    });
  } catch (error) {
    showError(error, "自动绑定数据源失败。");
  } finally {
    loading.value = false;
  }
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

watch(higherEducationYear, async () => {
  try {
    await loadHigherEducationSchools();
  } catch (error) {
    showError(error, "加载高校名单失败。");
  }
});

onMounted(() => {
  void loadProjects();
  void loadHigherEducationCatalog();
});
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

      <section v-if="higherEducationYears.length" class="monitoring-source-panel">
        <div>
          <strong>高校本科专业教学质量监测数据</strong>
          <span>从已导入的 9 张英文数据表构造统一快照，供文本、图表和表格自动绑定。</span>
        </div>
        <label class="studio-field compact">
          <span>年度</span>
          <select v-model="higherEducationYear" :disabled="creatingHigherEducation">
            <option v-for="year in higherEducationYears" :key="year" :value="year">
              {{ year }}
            </option>
          </select>
        </label>
        <label class="studio-field school-select">
          <span>学校</span>
          <select
            v-model="higherEducationSchoolCode"
            :disabled="creatingHigherEducation || !higherEducationSchools.length"
          >
            <option
              v-for="school in higherEducationSchools"
              :key="`${school.collectionYear}-${school.schoolCode}`"
              :value="school.schoolCode"
            >
              {{ school.schoolCode }} · {{ school.schoolName }}
            </option>
          </select>
        </label>
        <button
          type="button"
          class="studio-button primary"
          :disabled="creatingHigherEducation || !projectId || !higherEducationSchoolCode"
          @click="createMonitoringDataSource"
        >
          {{ creatingHigherEducation ? "正在构造…" : "构造报告数据源" }}
        </button>
      </section>

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
          数据上下文已确认，自动绑定并继续
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

.monitoring-source-panel {
  display: grid;
  grid-template-columns: minmax(260px, 1fr) 110px minmax(260px, 0.8fr) auto;
  gap: 12px;
  align-items: end;
  margin-top: 18px;
  padding: 14px;
  border: 1px solid #bfd7ff;
  border-radius: 10px;
  background: #f5f9ff;
}

.monitoring-source-panel > div {
  display: grid;
  gap: 5px;
  align-self: center;
  color: #344054;
}

.monitoring-source-panel > div span {
  color: #667085;
  font-size: 11px;
}

@media (max-width: 980px) {
  .monitoring-source-panel {
    grid-template-columns: 1fr 1fr;
  }

  .monitoring-source-panel > div {
    grid-column: 1 / -1;
  }
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
