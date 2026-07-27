<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  getProject,
  updateProject,
  archiveProject,
  restoreProject,
  listChapters,
  createChapter,
  updateChapter,
  deleteChapter,
  reorderChapters,
  initializeDevDataSource,
  listConnections,
  createConnection,
  testConnection,
  listSchemas,
  listObjects,
  listColumns,
  listDataSources,
  createDataSource,
  refreshDataSource,
  bulkImportScevl2024,
} from "../api/client";
import type {
  ProjectRecord,
  ChapterRecord,
  DataConnectionRecord,
  DatabaseObjectInfo,
  DataSourceRecord,
  BulkImportScevl2024Result,
} from "../api/types";

const route = useRoute();
const router = useRouter();

/* ── state ───────────────────────────────── */

const project = ref<ProjectRecord | null>(null);
const chapters = ref<ChapterRecord[]>([]);
const loading = ref(false);
const error = ref("");
const saving = ref(false);

const isEditing = ref(false);
const editForm = ref({
  projectName: "",
  description: "",
  projectStatus: "",
});

const showAddDialog = ref(false);
const showEditDialog = ref(false);
const selectedChapter = ref<ChapterRecord | null>(null);

const addForm = ref({
  chapterCode: "",
  title: "",
  parentId: null as string | null,
  sortKey: 0,
});

const editFormChapter = ref({
  chapterCode: "",
  title: "",
});

const dialogError = ref("");
const dialogSaving = ref(false);

const dsInitializing = ref(false);
const dsResult = ref<{
  dataSourceId: string;
  fieldCount: number;
  created: boolean;
  refreshed: boolean;
  error?: string;
} | null>(null);

const reordering = ref(false);

/* ── computed ────────────────────────────── */

const projectId = computed(() => route.params.projectId as string);

const statusConfig: Record<string, { label: string; color: string }> = {
  DRAFT: { label: "草稿", color: "#6b7280" },
  CONFIGURING: { label: "配置中", color: "#3b82f6" },
  READY: { label: "就绪", color: "#22c55e" },
  ARCHIVED: { label: "已归档", color: "#f97316" },
};

const workflowStatusConfig: Record<string, { label: string; color: string }> = {
  DRAFT: { label: "草稿", color: "#6b7280" },
  IN_REVIEW: { label: "审核中", color: "#3b82f6" },
  APPROVED: { label: "已通过", color: "#22c55e" },
  REJECTED: { label: "已驳回", color: "#ef4444" },
  PUBLISHED: { label: "已发布", color: "#22c55e" },
};

function getStatusConfig(s: string) {
  return statusConfig[s] ?? { label: s, color: "#6b7280" };
}

function getWorkflowStatusConfig(s: string) {
  return workflowStatusConfig[s] ?? { label: s, color: "#6b7280" };
}

const sortedChapters = computed(() => {
  return [...chapters.value].sort((a, b) => a.sortKey - b.sortKey);
});

/* ── project fetch ───────────────────────── */

async function fetchProject() {
  loading.value = true;
  error.value = "";
  try {
    project.value = await getProject(projectId.value);
  } catch (e: any) {
    error.value = e.message ?? "加载项目失败";
  } finally {
    loading.value = false;
  }
}

async function fetchChapters() {
  try {
    chapters.value = await listChapters(projectId.value);
  } catch (e: any) {
    error.value = e.message ?? "加载章节失败";
  }
}

/* ── project edit ────────────────────────── */

function startEditing() {
  if (!project.value) return;
  editForm.value = {
    projectName: project.value.projectName,
    description: project.value.description ?? "",
    projectStatus: project.value.projectStatus,
  };
  isEditing.value = true;
}

function cancelEditing() {
  isEditing.value = false;
}

async function saveProject() {
  if (!project.value) return;
  const name = editForm.value.projectName.trim();
  if (!name) {
    error.value = "项目名称不能为空";
    return;
  }
  saving.value = true;
  error.value = "";
  try {
    project.value = await updateProject(projectId.value, {
      projectName: name,
      description: editForm.value.description.trim() || null,
      projectStatus: editForm.value.projectStatus || null,
      rowVersion: project.value.rowVersion,
    });
    isEditing.value = false;
  } catch (e: any) {
    error.value = e.message ?? "保存项目失败";
  } finally {
    saving.value = false;
  }
}

/* ── archive / restore ───────────────────── */

async function handleArchive() {
  if (!project.value) return;
  if (!window.confirm("确定要归档此项目吗？")) return;
  try {
    project.value = await archiveProject(
      projectId.value,
      project.value.rowVersion
    );
  } catch (e: any) {
    error.value = e.message ?? "归档项目失败";
  }
}

async function handleRestore() {
  if (!project.value) return;
  if (!window.confirm("确定要恢复此项目吗？")) return;
  try {
    project.value = await restoreProject(
      projectId.value,
      project.value.rowVersion
    );
  } catch (e: any) {
    error.value = e.message ?? "恢复项目失败";
  }
}

/* ── chapter CRUD ────────────────────────── */

function openAddDialog(parentId: string | null = null) {
  addForm.value = {
    chapterCode: "",
    title: "",
    parentId,
    sortKey: chapters.value.length + 1,
  };
  dialogError.value = "";
  showAddDialog.value = true;
}

async function handleAddChapter() {
  const code = addForm.value.chapterCode.trim();
  const title = addForm.value.title.trim();
  if (!code || !title) {
    dialogError.value = "章节编码和标题为必填项";
    return;
  }
  dialogSaving.value = true;
  dialogError.value = "";
  try {
    await createChapter(projectId.value, {
      chapterCode: code,
      title,
      parentId: addForm.value.parentId,
      sortKey: addForm.value.sortKey || undefined,
    });
    showAddDialog.value = false;
    await fetchChapters();
  } catch (e: any) {
    dialogError.value = e.message ?? "创建章节失败";
  } finally {
    dialogSaving.value = false;
  }
}

function openEditDialog(chapter: ChapterRecord) {
  selectedChapter.value = chapter;
  editFormChapter.value = {
    chapterCode: chapter.chapterCode,
    title: chapter.title,
  };
  dialogError.value = "";
  showEditDialog.value = true;
}

async function handleEditChapter() {
  if (!selectedChapter.value) return;
  const code = editFormChapter.value.chapterCode.trim();
  const title = editFormChapter.value.title.trim();
  if (!code || !title) {
    dialogError.value = "章节编码和标题为必填项";
    return;
  }
  dialogSaving.value = true;
  dialogError.value = "";
  try {
    await updateChapter(selectedChapter.value.id, {
      chapterCode: code,
      title,
      rowVersion: selectedChapter.value.rowVersion,
    });
    showEditDialog.value = false;
    selectedChapter.value = null;
    await fetchChapters();
  } catch (e: any) {
    dialogError.value = e.message ?? "更新章节失败";
  } finally {
    dialogSaving.value = false;
  }
}

async function handleDeleteChapter(chapter: ChapterRecord) {
  if (!window.confirm(`确定要删除章节「${chapter.title}」吗？此操作不可撤销。`)) return;
  try {
    await deleteChapter(chapter.id, chapter.rowVersion);
    await fetchChapters();
  } catch (e: any) {
    error.value = e.message ?? "删除章节失败";
  }
}

/* ── chapter reorder ─────────────────────── */

async function moveChapter(chapter: ChapterRecord, direction: "up" | "down") {
  const sorted = sortedChapters.value;
  const idx = sorted.findIndex((c) => c.id === chapter.id);
  if (idx === -1) return;

  const targetIdx = direction === "up" ? idx - 1 : idx + 1;
  if (targetIdx < 0 || targetIdx >= sorted.length) return;

  reordering.value = true;
  try {
    const target = sorted[targetIdx];
    const swapSortKey = target.sortKey;
    const items = sorted.map((c) => ({
      chapterId: c.id,
      parentId: c.parentId,
      sortKey: c.id === chapter.id ? swapSortKey : c.id === target.id ? chapter.sortKey : c.sortKey,
    }));
    await reorderChapters(projectId.value, items);
    await fetchChapters();
  } catch (e: any) {
    error.value = e.message ?? "调整章节顺序失败";
  } finally {
    reordering.value = false;
  }
}

/* ── data source ─────────────────────────── */

async function handleInitializeDataSource(forceRefresh = false) {
  dsInitializing.value = true;
  dsResult.value = null;
  try {
    const result = await initializeDevDataSource(projectId.value, forceRefresh);
    dsResult.value = {
      dataSourceId: result.dataSourceId,
      fieldCount: result.fieldCount,
      created: result.created,
      refreshed: result.refreshed,
    };
  } catch (e: any) {
    dsResult.value = {
      dataSourceId: "",
      fieldCount: 0,
      created: false,
      refreshed: false,
      error: e.message ?? "初始化数据源失败",
    };
  } finally {
    dsInitializing.value = false;
  }
}

/* ── connections ─────────────────────────── */

const connections = ref<DataConnectionRecord[]>([]);
const connectionsLoading = ref(false);

const showConnectionDialog = ref(false);
const connectionForm = ref({
  connectionName: "",
  connectionType: "MYSQL" as "MYSQL",
  host: "",
  port: 3306,
  database: "scevl2024",
  sslMode: "Preferred",
  username: "",
  password: "",
  credentialRef: "",
});
const connectionSaving = ref(false);
const connectionError = ref("");

const showTestResult = ref<{
  connectionId: string;
  success: boolean;
  message: string;
} | null>(null);

async function fetchConnections() {
  connectionsLoading.value = true;
  try {
    connections.value = await listConnections(projectId.value);
  } catch (e: any) {
    error.value = e.message ?? "加载数据连接失败";
  } finally {
    connectionsLoading.value = false;
  }
}

function openConnectionDialog() {
  connectionForm.value = {
    connectionName: "",
    connectionType: "MYSQL",
    host: "",
    port: 3306,
    database: "scevl2024",
    sslMode: "Preferred",
    username: "",
    password: "",
    credentialRef: "",
  };
  connectionError.value = "";
  showConnectionDialog.value = true;
}

async function handleCreateConnection() {
  const f = connectionForm.value;
  if (!f.connectionName.trim() || !f.host.trim() || !f.database.trim()) {
    connectionError.value = "名称、主机、数据库为必填项";
    return;
  }
  const hasInlineCreds = Boolean(f.username || f.password);
  const hasCredRef = Boolean(f.credentialRef.trim());
  if (!hasInlineCreds && !hasCredRef) {
    connectionError.value = "请填写用户名/密码，或填写凭据引用（credentialRef）。";
    return;
  }
  if (hasCredRef && !f.credentialRef.startsWith("config:DataSourceCredentials:")) {
    connectionError.value = "凭据引用必须以 config:DataSourceCredentials: 开头";
    return;
  }
  connectionSaving.value = true;
  connectionError.value = "";
  try {
    const created = await createConnection({
      projectId: projectId.value,
      connectionName: f.connectionName.trim(),
      connectionType: "MYSQL",
      config: {
        host: f.host.trim(),
        port: Number(f.port) || 3306,
        database: f.database.trim(),
        sslMode: f.sslMode,
        username: f.username.trim() || null,
        password: f.password.trim() || null,
      },
      credentialRef: f.credentialRef.trim() || null,
    });
    await fetchConnections();
    showConnectionDialog.value = false;
    // 自动测试，缩短闭环
    showTestResult.value = {
      connectionId: created.id,
      success: false,
      message: "正在测试连接…",
    };
    try {
      const result = await testConnection(created.id);
      showTestResult.value = {
        connectionId: created.id,
        success: result.success,
        message: result.message,
      };
    } catch (e: any) {
      showTestResult.value = {
        connectionId: created.id,
        success: false,
        message: e.message ?? "测试连接失败",
      };
    }
  } catch (e: any) {
    connectionError.value = e.message ?? "创建数据连接失败";
  } finally {
    connectionSaving.value = false;
  }
}

async function handleTestConnection(connectionId: string) {
  showTestResult.value = {
    connectionId,
    success: false,
    message: "正在测试连接…",
  };
  try {
    const result = await testConnection(connectionId);
    showTestResult.value = {
      connectionId,
      success: result.success,
      message: result.message,
    };
  } catch (e: any) {
    showTestResult.value = {
      connectionId,
      success: false,
      message: e.message ?? "测试连接失败",
    };
  }
}

function lastTestResult(connectionId: string) {
  if (showTestResult.value && showTestResult.value.connectionId === connectionId) {
    return showTestResult.value;
  }
  const conn = connections.value.find((c) => c.id === connectionId);
  if (!conn?.lastTestResult) return null;
  return {
    connectionId,
    success: conn.lastTestResult.success,
    message: conn.lastTestResult.message,
  };
}

/* ── data sources (production) ───────────── */

const dataSources = ref<DataSourceRecord[]>([]);
const dataSourcesLoading = ref(false);
const refreshingSourceId = ref<string | null>(null);

const showSourceDialog = ref(false);
const sourceForm = ref({
  connectionId: "",
  sourceCode: "",
  sourceName: "",
  schemaName: "scevl2024",
  objectType: "TABLE" as "TABLE" | "VIEW",
  objectName: "",
});
const availableSchemas = ref<string[]>([]);
const availableObjects = ref<DatabaseObjectInfo[]>([]);
const sourceSaving = ref(false);
const sourceError = ref("");

async function fetchDataSources() {
  dataSourcesLoading.value = true;
  try {
    dataSources.value = await listDataSources(projectId.value);
  } catch (e: any) {
    error.value = e.message ?? "加载数据源失败";
  } finally {
    dataSourcesLoading.value = false;
  }
}

async function handleRefreshSource(source: DataSourceRecord) {
  refreshingSourceId.value = source.id;
  try {
    await refreshDataSource(source.id);
    await fetchDataSources();
  } catch (e: any) {
    error.value = e.message ?? `刷新数据源 ${source.sourceName} 失败`;
  } finally {
    refreshingSourceId.value = null;
  }
}

async function openSourceDialog() {
  if (connections.value.length === 0) {
    error.value = "请先创建至少一个数据连接。";
    return;
  }
  sourceForm.value = {
    connectionId: connections.value[0]?.id ?? "",
    sourceCode: "",
    sourceName: "",
    schemaName: "scevl2024",
    objectType: "TABLE",
    objectName: "",
  };
  sourceError.value = "";
  availableSchemas.value = [];
  availableObjects.value = [];
  showSourceDialog.value = true;
  await loadSchemas();
}

async function loadSchemas() {
  if (!sourceForm.value.connectionId) return;
  sourceError.value = "";
  try {
    availableSchemas.value = await listSchemas(sourceForm.value.connectionId);
    if (
      !sourceForm.value.schemaName &&
      availableSchemas.value.includes("scevl2024")
    ) {
      sourceForm.value.schemaName = "scevl2024";
    }
    await loadObjects();
  } catch (e: any) {
    sourceError.value = e.message ?? "加载数据库 Schema 失败";
  }
}

async function loadObjects() {
  if (!sourceForm.value.connectionId || !sourceForm.value.schemaName) return;
  sourceError.value = "";
  try {
    availableObjects.value = await listObjects(
      sourceForm.value.connectionId,
      sourceForm.value.schemaName
    );
  } catch (e: any) {
    sourceError.value = e.message ?? "加载数据表失败";
  }
}

function autoFillSourceCode() {
  if (sourceForm.value.objectName && !sourceForm.value.sourceCode) {
    const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, "");
    sourceForm.value.sourceCode = `${sourceForm.value.schemaName}_${sourceForm.value.objectName}_${stamp}`;
  }
  if (sourceForm.value.objectName && !sourceForm.value.sourceName) {
    sourceForm.value.sourceName = sourceForm.value.objectName;
  }
}

async function handleCreateSource() {
  const f = sourceForm.value;
  if (!f.connectionId || !f.sourceCode.trim() || !f.objectName.trim()) {
    sourceError.value = "编码、表名必填";
    return;
  }
  sourceSaving.value = true;
  sourceError.value = "";
  try {
    const created = await createDataSource({
      projectId: projectId.value,
      connectionId: f.connectionId,
      sourceCode: f.sourceCode.trim(),
      sourceName: f.sourceName.trim() || f.objectName.trim(),
      sourceType: "DATABASE",
      schemaName: f.schemaName.trim(),
      objectType: f.objectType,
      objectName: f.objectName.trim(),
    });
    showSourceDialog.value = false;
    await fetchDataSources();
    // 创建后立即刷新一次快照，把字段树跑出来
    await handleRefreshSource(created);
  } catch (e: any) {
    sourceError.value = e.message ?? "创建数据源失败";
  } finally {
    sourceSaving.value = false;
  }
}

/* ── bulk import scevl2024 ────────────────── */

const bulkImporting = ref(false);
const bulkImportResult = ref<BulkImportScevl2024Result | null>(null);
const bulkImportConnectionId = ref("");

async function handleBulkImport() {
  if (connections.value.length === 0) {
    error.value = "请先创建至少一个数据连接。";
    return;
  }
  const connectionId =
    bulkImportConnectionId.value || connections.value[0].id;
  if (!window.confirm(
    `将自动在所选连接对应的业务库中扫描 ${"data_专业监测_"} 开头的表并批量创建为数据源。是否继续？`
  )) {
    return;
  }
  bulkImporting.value = true;
  bulkImportResult.value = null;
  try {
    bulkImportResult.value = await bulkImportScevl2024(projectId.value, {
      connectionId,
      schemaName: "scevl2024",
      objectNamePrefix: "data_专业监测_",
      sourceCodePrefix: "scevl2024",
    });
    await fetchDataSources();
    // 创建成功后批量触发首次刷新快照
    for (const item of bulkImportResult.value.items) {
      if (item.status === "CREATED" && item.dataSourceId) {
        try {
          await refreshDataSource(item.dataSourceId);
        } catch {
          // 忽略单个失败，让 UI 列表显示实际状态
        }
      }
    }
    await fetchDataSources();
  } catch (e: any) {
    error.value = e.message ?? "批量导入失败";
  } finally {
    bulkImporting.value = false;
  }
}

/* ── lifecycle ───────────────────────────── */

onMounted(async () => {
  await fetchProject();
  if (!error.value) {
    await fetchChapters();
  }
  await fetchConnections();
  await fetchDataSources();
});
</script>

<template>
  <div class="project-detail-view">
    <!-- back navigation -->
    <div class="back-nav">
      <button class="btn btn-link" @click="router.push('/projects')">
        &larr; 返回项目列表
      </button>
    </div>

    <!-- loading -->
    <div v-if="loading" class="loading-state">加载中&hellip;</div>

    <!-- error (project not loaded) -->
    <div v-else-if="error && !project" class="error-banner">{{ error }}</div>

    <!-- main content -->
    <template v-else-if="project">
      <!-- project info card -->
      <div class="card project-card">
        <div class="card-header">
          <h2>项目信息</h2>
          <div class="card-header-actions">
            <button
              v-if="!isEditing"
              class="btn btn-sm"
              @click="startEditing"
            >
              编辑
            </button>
            <button
              v-if="project.projectStatus !== 'ARCHIVED'"
              class="btn btn-sm btn-outline-danger"
              @click="handleArchive"
            >
              归档
            </button>
            <button
              v-else
              class="btn btn-sm btn-outline"
              @click="handleRestore"
            >
              恢复
            </button>
            <button
              class="btn btn-sm btn-primary"
              @click="router.push(`/workspace?projectId=${project.projectId}`)"
            >
              进入绑定工作区
            </button>
          </div>
        </div>

        <div v-if="error && project" class="error-banner page-error">{{ error }}</div>

        <div class="card-body">
          <!-- display mode -->
          <template v-if="!isEditing">
            <div class="info-grid">
              <div class="info-item">
                <span class="info-label">项目编码</span>
                <span class="info-value mono">{{ project.projectCode }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">项目名称</span>
                <span class="info-value">{{ project.projectName }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">状态</span>
                <span
                  class="status-tag"
                  :style="{
                    background: getStatusConfig(project.projectStatus).color + '18',
                    color: getStatusConfig(project.projectStatus).color,
                    borderColor: getStatusConfig(project.projectStatus).color + '30',
                  }"
                >
                  {{ getStatusConfig(project.projectStatus).label }}
                </span>
              </div>
              <div class="info-item">
                <span class="info-label">描述</span>
                <span class="info-value">{{ project.description || "无" }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">创建时间</span>
                <span class="info-value date">{{ project.createdAt }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">更新时间</span>
                <span class="info-value date">{{ project.updatedAt }}</span>
              </div>
            </div>
          </template>

          <!-- edit mode -->
          <template v-else>
            <div class="form-grid">
              <div class="form-group">
                <label>项目编码</label>
                <input type="text" disabled :value="project.projectCode" />
              </div>
              <div class="form-group">
                <label>项目名称 <span class="required">*</span></label>
                <input
                  v-model="editForm.projectName"
                  type="text"
                  placeholder="请输入项目名称"
                />
              </div>
              <div class="form-group">
                <label>状态</label>
                <select v-model="editForm.projectStatus">
                  <option value="DRAFT">草稿</option>
                  <option value="CONFIGURING">配置中</option>
                  <option value="READY">就绪</option>
                  <option value="ARCHIVED">已归档</option>
                </select>
              </div>
              <div class="form-group">
                <label>描述</label>
                <textarea
                  v-model="editForm.description"
                  placeholder="可选，输入项目描述"
                  rows="3"
                ></textarea>
              </div>
            </div>
            <div class="edit-actions">
              <button class="btn" @click="cancelEditing">取消</button>
              <button
                class="btn btn-primary"
                :disabled="saving"
                @click="saveProject"
              >
                {{ saving ? "保存中&hellip;" : "保存" }}
              </button>
            </div>
          </template>
        </div>
      </div>

      <!-- test data source card -->
      <div class="card">
        <div class="card-header">
          <h2>测试数据源</h2>
          <div class="card-header-actions">
            <button
              class="btn btn-sm"
              :disabled="dsInitializing"
              @click="handleInitializeDataSource(false)"
            >
              {{ dsInitializing ? "初始化中&hellip;" : "初始化数据源" }}
            </button>
            <button
              class="btn btn-sm btn-outline"
              :disabled="dsInitializing"
              @click="handleInitializeDataSource(true)"
            >
              强制刷新
            </button>
          </div>
        </div>
        <div class="card-body">
          <div v-if="dsInitializing" class="loading-state small">初始化中&hellip;</div>
          <div v-else-if="dsResult" class="ds-result">
            <div v-if="dsResult.error" class="error-banner">{{ dsResult.error }}</div>
            <div v-else class="info-grid">
              <div class="info-item">
                <span class="info-label">数据源ID</span>
                <span class="info-value mono">{{ dsResult.dataSourceId }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">字段数量</span>
                <span class="info-value">{{ dsResult.fieldCount }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">已创建</span>
                <span class="info-value">{{ dsResult.created ? "是" : "否" }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">已刷新</span>
                <span class="info-value">{{ dsResult.refreshed ? "是" : "否" }}</span>
              </div>
            </div>
          </div>
          <div v-else class="empty-hint">
            点击「初始化数据源」创建或刷新项目的测试数据。
          </div>
        </div>
      </div>

      <!-- connections card -->
      <div class="card">
        <div class="card-header">
          <h2>数据连接</h2>
          <div class="card-header-actions">
            <button
              class="btn btn-sm"
              :disabled="connectionsLoading"
              @click="fetchConnections"
            >
              {{ connectionsLoading ? "刷新中&hellip;" : "刷新列表" }}
            </button>
            <button class="btn btn-sm btn-primary" @click="openConnectionDialog">
              新建连接
            </button>
          </div>
        </div>
        <div class="card-body card-body-nopad">
          <div
            v-if="!connectionsLoading && connections.length === 0"
            class="empty-hint pad"
          >
            暂无数据连接。点击「新建连接」配置业务库的 host/port/数据库/账号密码。
          </div>
          <table v-else class="data-table">
            <thead>
              <tr>
                <th>名称</th>
                <th>类型</th>
                <th>主机</th>
                <th>端口</th>
                <th>数据库</th>
                <th>凭据引用</th>
                <th>最近测试</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="conn in connections" :key="conn.id">
                <td class="cell-title">{{ conn.connectionName }}</td>
                <td>{{ conn.connectionType }}</td>
                <td class="cell-code">{{ conn.config.host }}</td>
                <td>{{ conn.config.port }}</td>
                <td>{{ conn.config.database }}</td>
                <td class="cell-code" :title="conn.credentialRef">
                  {{ conn.credentialRef }}
                </td>
                <td>
                  <span
                    v-if="lastTestResult(conn.id)"
                    class="status-tag"
                    :style="{
                      background: (lastTestResult(conn.id)?.success ? '#22c55e' : '#ef4444') + '18',
                      color: lastTestResult(conn.id)?.success ? '#22c55e' : '#ef4444',
                      borderColor: (lastTestResult(conn.id)?.success ? '#22c55e' : '#ef4444') + '30',
                    }"
                  >
                    {{ lastTestResult(conn.id)?.success ? "通过" : "失败" }}
                  </span>
                  <span v-else class="empty-hint">未测试</span>
                </td>
                <td>
                  <div class="actions-cell">
                    <button class="btn btn-sm" @click="handleTestConnection(conn.id)">
                      测试
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- data sources (production) card -->
      <div class="card">
        <div class="card-header">
          <h2>数据源</h2>
          <div class="card-header-actions">
            <select
              v-model="bulkImportConnectionId"
              :disabled="connections.length === 0 || bulkImporting"
              class="bulk-import-connection"
            >
              <option value="">默认连接</option>
              <option
                v-for="conn in connections"
                :key="conn.id"
                :value="conn.id"
              >
                {{ conn.connectionName }}
              </option>
            </select>
            <button
              class="btn btn-sm"
              :disabled="connections.length === 0 || bulkImporting"
              @click="handleBulkImport"
            >
              {{ bulkImporting ? "导入中&hellip;" : "导入 scevl2024 9 张表" }}
            </button>
            <button
              class="btn btn-sm"
              :disabled="dataSourcesLoading"
              @click="fetchDataSources"
            >
              {{ dataSourcesLoading ? "刷新中&hellip;" : "刷新列表" }}
            </button>
            <button class="btn btn-sm btn-primary" @click="openSourceDialog">
              新建数据源
            </button>
          </div>
        </div>
        <div class="card-body card-body-nopad">
          <div
            v-if="bulkImportResult"
            class="info-banner"
          >
            <strong>批量导入完成：</strong>
            创建 {{ bulkImportResult.created }} 个，跳过 {{ bulkImportResult.skipped }} 个，失败 {{ bulkImportResult.failed }} 个。
            <details v-if="bulkImportResult.items.length">
              <summary>查看明细</summary>
              <ul class="info-list">
                <li v-for="item in bulkImportResult.items" :key="item.objectName">
                  <code>{{ item.objectName }}</code>
                  —
                  <span
                    :class="{
                      'status-ok': item.status === 'CREATED',
                      'status-skip': item.status === 'SKIPPED',
                      'status-fail': item.status === 'FAILED',
                    }"
                  >
                    {{ item.status }}
                  </span>
                  <span v-if="item.message">：{{ item.message }}</span>
                </li>
              </ul>
            </details>
          </div>
          <div
            v-if="!dataSourcesLoading && dataSources.length === 0 && !bulkImportResult"
            class="empty-hint pad"
          >
            暂无数据源。请先在上方创建数据连接，再点击「新建数据源」选择连接、数据库、数据表；或点击「导入 scevl2024 9 张表」一键导入。
          </div>
          <table v-else class="data-table">
            <thead>
              <tr>
                <th>编码</th>
                <th>名称</th>
                <th>连接</th>
                <th>数据库</th>
                <th>对象</th>
                <th>类型</th>
                <th>状态</th>
                <th>列数</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="source in dataSources" :key="source.id">
                <td class="cell-code">{{ source.sourceCode }}</td>
                <td class="cell-title">{{ source.sourceName }}</td>
                <td class="cell-code">
                  {{
                    connections.find((c) => c.id === source.connectionId)
                      ?.connectionName || source.connectionId
                  }}
                </td>
                <td>{{ source.schemaName }}</td>
                <td class="cell-code">{{ source.objectName }}</td>
                <td>{{ source.objectType }}</td>
                <td>
                  <span
                    class="status-tag"
                    :style="{
                      background: (source.sourceStatus === 'READY' ? '#22c55e' : '#94a3b8') + '18',
                      color: source.sourceStatus === 'READY' ? '#22c55e' : '#94a3b8',
                      borderColor: (source.sourceStatus === 'READY' ? '#22c55e' : '#94a3b8') + '30',
                    }"
                  >
                    {{ source.sourceStatus }}
                  </span>
                </td>
                <td>{{ source.schema?.columns.length ?? 0 }}</td>
                <td>
                  <div class="actions-cell">
                    <button
                      class="btn btn-sm"
                      :disabled="refreshingSourceId === source.id"
                      @click="handleRefreshSource(source)"
                    >
                      {{
                        refreshingSourceId === source.id ? "刷新中&hellip;" : "刷新快照"
                      }}
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- chapters card -->
      <div class="card">
        <div class="card-header">
          <h2>章节管理</h2>
          <div class="card-header-actions">
            <button class="btn btn-sm btn-primary" @click="openAddDialog(null)">
              新增章节
            </button>
          </div>
        </div>
        <div class="card-body card-body-nopad">
          <div v-if="chapters.length === 0" class="empty-hint pad">
            暂无章节数据，请点击「新增章节」添加。
          </div>
          <table v-else class="data-table">
            <thead>
              <tr>
                <th>章节编码</th>
                <th>标题</th>
                <th>层级</th>
                <th>状态</th>
                <th>顺序</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="chapter in sortedChapters" :key="chapter.id">
                <td class="cell-code">{{ chapter.chapterCode }}</td>
                <td
                  class="cell-title"
                  :style="{ paddingLeft: 16 + (chapter.levelNo - 1) * 20 + 'px' }"
                >
                  {{ chapter.title }}
                </td>
                <td>{{ chapter.levelNo }}</td>
                <td>
                  <span
                    class="status-tag"
                    :style="{
                      background: getWorkflowStatusConfig(chapter.workflowStatus).color + '18',
                      color: getWorkflowStatusConfig(chapter.workflowStatus).color,
                      borderColor: getWorkflowStatusConfig(chapter.workflowStatus).color + '30',
                    }"
                  >
                    {{ getWorkflowStatusConfig(chapter.workflowStatus).label }}
                  </span>
                </td>
                <td>{{ chapter.sortKey }}</td>
                <td>
                  <div class="actions-cell">
                    <button class="btn btn-sm" @click="openEditDialog(chapter)">
                      编辑
                    </button>
                    <button
                      class="btn btn-sm btn-outline"
                      @click="openAddDialog(chapter.id)"
                    >
                      添加子章节
                    </button>
                    <button
                      class="btn btn-sm btn-outline"
                      :disabled="reordering || sortedChapters[0]?.id === chapter.id"
                      @click="moveChapter(chapter, 'up')"
                    >
                      上移
                    </button>
                    <button
                      class="btn btn-sm btn-outline"
                      :disabled="
                        reordering ||
                        sortedChapters[sortedChapters.length - 1]?.id === chapter.id
                      "
                      @click="moveChapter(chapter, 'down')"
                    >
                      下移
                    </button>
                    <button
                      class="btn btn-sm btn-outline-danger"
                      @click="handleDeleteChapter(chapter)"
                    >
                      删除
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>

    <!-- add chapter dialog -->
    <div
      v-if="showAddDialog"
      class="dialog-overlay"
      @click.self="showAddDialog = false"
    >
      <div class="dialog">
        <div class="dialog-header">
          <h2>{{ addForm.parentId ? "添加子章节" : "新增章节" }}</h2>
          <button class="dialog-close" @click="showAddDialog = false">&times;</button>
        </div>
        <div class="dialog-body">
          <div v-if="dialogError" class="error-banner dialog-error">
            {{ dialogError }}
          </div>
          <div class="form-group">
            <label>章节编码 <span class="required">*</span></label>
            <input
              v-model="addForm.chapterCode"
              type="text"
              placeholder="例如 CH-001"
            />
          </div>
          <div class="form-group">
            <label>标题 <span class="required">*</span></label>
            <input v-model="addForm.title" type="text" placeholder="请输入章节标题" />
          </div>
          <div class="form-group">
            <label>父级章节</label>
            <select v-model="addForm.parentId">
              <option :value="null">无（顶级章节）</option>
              <option
                v-for="ch in chapters"
                :key="ch.id"
                :value="ch.id"
              >
                {{ ch.chapterCode }} - {{ ch.title }}
              </option>
            </select>
          </div>
          <div class="form-group">
            <label>排序值</label>
            <input v-model.number="addForm.sortKey" type="number" min="0" />
          </div>
        </div>
        <div class="dialog-footer">
          <button class="btn" @click="showAddDialog = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="dialogSaving"
            @click="handleAddChapter"
          >
            {{ dialogSaving ? "创建中&hellip;" : "确认创建" }}
          </button>
        </div>
      </div>
    </div>

    <!-- edit chapter dialog -->
    <div
      v-if="showEditDialog"
      class="dialog-overlay"
      @click.self="showEditDialog = false"
    >
      <div class="dialog">
        <div class="dialog-header">
          <h2>编辑章节</h2>
          <button class="dialog-close" @click="showEditDialog = false">&times;</button>
        </div>
        <div class="dialog-body">
          <div v-if="dialogError" class="error-banner dialog-error">
            {{ dialogError }}
          </div>
          <div class="form-group">
            <label>章节编码 <span class="required">*</span></label>
            <input v-model="editFormChapter.chapterCode" type="text" />
          </div>
          <div class="form-group">
            <label>标题 <span class="required">*</span></label>
            <input v-model="editFormChapter.title" type="text" />
          </div>
        </div>
        <div class="dialog-footer">
          <button class="btn" @click="showEditDialog = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="dialogSaving"
            @click="handleEditChapter"
          >
            {{ dialogSaving ? "保存中&hellip;" : "确认保存" }}
          </button>
        </div>
      </div>
    </div>

    <!-- new connection dialog -->
    <div
      v-if="showConnectionDialog"
      class="dialog-overlay"
      @click.self="showConnectionDialog = false"
    >
      <div class="dialog dialog-wide">
        <div class="dialog-header">
          <h2>新建数据连接</h2>
          <button class="dialog-close" @click="showConnectionDialog = false">&times;</button>
        </div>
        <div class="dialog-body">
          <div v-if="connectionError" class="error-banner dialog-error">
            {{ connectionError }}
          </div>
          <p class="dialog-hint">
            直接填写数据库账号密码即可。如需引用服务端配置（例如在部署环境隐藏密码），可选填下方的「凭据引用」（使用 <code>config:DataSourceCredentials:&lt;key&gt;</code> 格式）。
          </p>
          <div class="form-grid">
            <div class="form-group">
              <label>连接名称 <span class="required">*</span></label>
              <input
                v-model="connectionForm.connectionName"
                type="text"
                placeholder="例如 scevl2024"
              />
            </div>
            <div class="form-group">
              <label>类型</label>
              <input
                v-model="connectionForm.connectionType"
                type="text"
                disabled
              />
            </div>
            <div class="form-group">
              <label>主机 <span class="required">*</span></label>
              <input
                v-model="connectionForm.host"
                type="text"
                placeholder="127.0.0.1"
              />
            </div>
            <div class="form-group">
              <label>端口</label>
              <input v-model.number="connectionForm.port" type="number" min="1" max="65535" />
            </div>
            <div class="form-group">
              <label>数据库 <span class="required">*</span></label>
              <input
                v-model="connectionForm.database"
                type="text"
                placeholder="scevl2024"
              />
            </div>
            <div class="form-group">
              <label>TLS</label>
              <select v-model="connectionForm.sslMode">
                <option value="Preferred">Preferred</option>
                <option value="Required">Required</option>
                <option value="None">None</option>
              </select>
            </div>
            <div class="form-group">
              <label>用户名 <span class="required">*</span></label>
              <input
                v-model="connectionForm.username"
                type="text"
                placeholder="report_app"
              />
            </div>
            <div class="form-group">
              <label>密码 <span class="required">*</span></label>
              <input
                v-model="connectionForm.password"
                type="password"
                placeholder="数据库密码"
              />
            </div>
            <div class="form-group form-grid-wide">
              <label>凭据引用（可选）</label>
              <input
                v-model="connectionForm.credentialRef"
                type="text"
                placeholder="留空则使用上方的用户名/密码"
              />
            </div>
          </div>
        </div>
        <div class="dialog-footer">
          <button class="btn" @click="showConnectionDialog = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="connectionSaving"
            @click="handleCreateConnection"
          >
            {{ connectionSaving ? "创建中&hellip;" : "创建并测试" }}
          </button>
        </div>
      </div>
    </div>

    <!-- new data source dialog -->
    <div
      v-if="showSourceDialog"
      class="dialog-overlay"
      @click.self="showSourceDialog = false"
    >
      <div class="dialog dialog-wide">
        <div class="dialog-header">
          <h2>新建数据源</h2>
          <button class="dialog-close" @click="showSourceDialog = false">&times;</button>
        </div>
        <div class="dialog-body">
          <div v-if="sourceError" class="error-banner dialog-error">
            {{ sourceError }}
          </div>
          <div class="form-grid">
            <div class="form-group">
              <label>数据连接 <span class="required">*</span></label>
              <select
                v-model="sourceForm.connectionId"
                @change="loadSchemas"
              >
                <option value="">选择连接</option>
                <option
                  v-for="conn in connections"
                  :key="conn.id"
                  :value="conn.id"
                >
                  {{ conn.connectionName }} ({{ conn.config.host }}:{{ conn.config.port }}/{{ conn.config.database }})
                </option>
              </select>
            </div>
            <div class="form-group">
              <label>数据库 Schema <span class="required">*</span></label>
              <select
                v-model="sourceForm.schemaName"
                @change="loadObjects"
              >
                <option value="">选择 Schema</option>
                <option
                  v-for="schema in availableSchemas"
                  :key="schema"
                  :value="schema"
                >
                  {{ schema }}
                </option>
              </select>
            </div>
            <div class="form-group">
              <label>对象类型</label>
              <select v-model="sourceForm.objectType">
                <option value="TABLE">表</option>
                <option value="VIEW">视图</option>
              </select>
            </div>
            <div class="form-group">
              <label>对象（数据表/视图） <span class="required">*</span></label>
              <select
                v-model="sourceForm.objectName"
                @change="autoFillSourceCode"
              >
                <option value="">选择对象</option>
                <option
                  v-for="obj in availableObjects.filter((o) => o.objectType === sourceForm.objectType)"
                  :key="obj.schema + '.' + obj.objectName"
                  :value="obj.objectName"
                >
                  {{ obj.objectName }}
                </option>
              </select>
            </div>
            <div class="form-group">
              <label>数据源编码 <span class="required">*</span></label>
              <input
                v-model="sourceForm.sourceCode"
                type="text"
                placeholder="例如 scevl2024_data_专业监测_优势特色专业_20260726"
              />
            </div>
            <div class="form-group">
              <label>数据源名称</label>
              <input
                v-model="sourceForm.sourceName"
                type="text"
                placeholder="保持空将使用对象名"
              />
            </div>
          </div>
        </div>
        <div class="dialog-footer">
          <button class="btn" @click="showSourceDialog = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="sourceSaving || !sourceForm.connectionId || !sourceForm.objectName"
            @click="handleCreateSource"
          >
            {{ sourceSaving ? "创建并刷新中&hellip;" : "创建并刷新快照" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── layout ────────────────────────────────── */

.project-detail-view {
  padding: 24px 32px 32px;
  background: #f1f5f9;
  min-height: 100%;
  box-sizing: border-box;
}

.back-nav {
  margin-bottom: 16px;
}

.card {
  background: #fff;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  margin-bottom: 20px;
  overflow: hidden;
}

.card:last-child {
  margin-bottom: 0;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 24px;
  border-bottom: 1px solid #f1f5f9;
}

.card-header h2 {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0;
}

.card-header-actions {
  display: flex;
  gap: 8px;
}

.card-body {
  padding: 20px 24px;
}

.card-body-nopad {
  padding: 0;
}

/* ── buttons (same palette as ProjectListView) */

.btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 8px 16px;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
  background: #fff;
  color: #475569;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, color 0.15s;
  white-space: nowrap;
  user-select: none;
  font-family: inherit;
}

.btn:hover {
  background: #f8fafc;
  border-color: #cbd5e1;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  pointer-events: none;
}

.btn-link {
  border: none;
  background: none;
  color: #3b82f6;
  padding: 0;
  font-size: 13px;
}

.btn-link:hover {
  color: #2563eb;
  background: none;
  border-color: transparent;
}

.btn-primary {
  background: #3b82f6;
  border-color: #3b82f6;
  color: #fff;
}

.btn-primary:hover {
  background: #2563eb;
  border-color: #2563eb;
}

.btn-outline {
  border-color: #d1d9e6;
  color: #475569;
}

.btn-outline:hover {
  background: #f1f5f9;
}

.btn-outline-danger {
  color: #ef4444;
  border-color: #fecaca;
  background: #fef2f2;
}

.btn-outline-danger:hover {
  background: #fee2e2;
  border-color: #fca5a5;
}

.btn-sm {
  padding: 4px 10px;
  font-size: 12px;
}

/* ── status tag ───────────────────────────── */

.status-tag {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
  border: 1px solid;
}

/* ── error / loading / hint ───────────────── */

.error-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #ef4444;
  padding: 10px 16px;
  border-radius: 6px;
  font-size: 13px;
}

.page-error {
  margin: 12px 24px 0;
}

.dialog-error {
  margin-bottom: 16px;
}

.loading-state {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 64px 0;
  color: #94a3b8;
  font-size: 14px;
}

.loading-state.small {
  padding: 32px 0;
  font-size: 13px;
}

.empty-hint {
  font-size: 13px;
  color: #94a3b8;
  margin: 0;
}

.empty-hint.pad {
  padding: 24px;
}

/* ── info grid ────────────────────────────── */

.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px 32px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.info-label {
  font-size: 12px;
  font-weight: 500;
  color: #94a3b8;
  text-transform: uppercase;
  letter-spacing: 0.3px;
}

.info-value {
  font-size: 14px;
  color: #1e293b;
}

.info-value.mono {
  font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  font-size: 13px;
  color: #475569;
}

.info-value.date {
  color: #64748b;
}

/* ── edit form grid ───────────────────────── */

.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 18px 32px;
}

.edit-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid #f1f5f9;
}

/* ── form groups ──────────────────────────── */

.form-group {
  display: flex;
  flex-direction: column;
}

.form-group label {
  font-size: 13px;
  font-weight: 500;
  color: #374151;
  margin-bottom: 6px;
}

.form-group .required {
  color: #ef4444;
}

.form-group input,
.form-group textarea,
.form-group select {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  font-size: 13px;
  color: #1e293b;
  outline: none;
  box-sizing: border-box;
  font-family: inherit;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.form-group input:focus,
.form-group textarea:focus,
.form-group select:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

.form-group textarea {
  resize: vertical;
}

.form-group input:disabled {
  background: #f8fafc;
  color: #94a3b8;
  cursor: not-allowed;
}

/* ── data table ───────────────────────────── */

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.data-table th {
  text-align: left;
  padding: 12px 16px;
  background: #f8fafc;
  color: #64748b;
  font-weight: 600;
  border-bottom: 1px solid #e2e8f0;
  white-space: nowrap;
}

.data-table td {
  padding: 12px 16px;
  color: #1e293b;
  border-bottom: 1px solid #f1f5f9;
  vertical-align: middle;
}

.data-table tr:last-child td {
  border-bottom: none;
}

.data-table tbody tr:hover td {
  background: #fafcff;
}

.cell-code {
  font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  font-size: 12px;
  color: #475569;
  white-space: nowrap;
}

.cell-title {
  font-weight: 500;
}

.actions-cell {
  display: flex;
  gap: 6px;
  flex-wrap: nowrap;
}

/* ── ds result ────────────────────────────── */

.ds-result {
  min-height: 40px;
}

/* ── dialogs (same as ProjectListView) ─────── */

.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.dialog {
  background: #fff;
  border-radius: 12px;
  width: 480px;
  max-width: 92vw;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.15);
  animation: dialog-in 0.2s ease-out;
}

@keyframes dialog-in {
  from {
    opacity: 0;
    transform: translateY(8px) scale(0.98);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

.dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px 0;
}

.dialog-header h2 {
  font-size: 17px;
  font-weight: 600;
  color: #1e293b;
  margin: 0;
}

.dialog-close {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: #f1f5f9;
  border-radius: 6px;
  font-size: 20px;
  line-height: 1;
  color: #64748b;
  cursor: pointer;
  transition: background 0.15s;
  padding: 0;
}

.dialog-close:hover {
  background: #e2e8f0;
}

.dialog-body {
  padding: 20px 24px;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 16px 24px;
  border-top: 1px solid #f1f5f9;
}

.dialog-wide {
  width: 640px;
}

.dialog-hint {
  font-size: 12px;
  color: #64748b;
  margin: 0 0 16px;
  padding: 10px 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  line-height: 1.6;
}

.dialog-hint code {
  font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  background: #e2e8f0;
  padding: 1px 4px;
  border-radius: 3px;
  font-size: 11px;
  color: #1e293b;
}

.form-grid-wide {
  grid-column: 1 / -1;
}

/* ── bulk import banner ────────────────────── */

.info-banner {
  margin: 16px 24px 0;
  padding: 12px 16px;
  background: #f0f9ff;
  border: 1px solid #bae6fd;
  border-radius: 6px;
  font-size: 13px;
  color: #0c4a6e;
}

.info-banner details {
  margin-top: 8px;
}

.info-banner summary {
  cursor: pointer;
  font-weight: 500;
}

.info-list {
  margin: 8px 0 0;
  padding: 0 0 0 18px;
  font-size: 12px;
  color: #334155;
}

.info-list li {
  margin: 2px 0;
}

.status-ok {
  color: #16a34a;
  font-weight: 500;
}

.status-skip {
  color: #64748b;
  font-weight: 500;
}

.status-fail {
  color: #ef4444;
  font-weight: 500;
}

.bulk-import-connection {
  padding: 4px 8px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  background: #fff;
  font-size: 12px;
  color: #1e293b;
  cursor: pointer;
  max-width: 180px;
}
</style>
