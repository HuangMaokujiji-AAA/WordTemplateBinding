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
} from "../api/client";
import type { ProjectRecord, ChapterRecord } from "../api/types";

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

/* ── lifecycle ───────────────────────────── */

onMounted(async () => {
  await fetchProject();
  if (!error.value) {
    await fetchChapters();
  }
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
</style>
