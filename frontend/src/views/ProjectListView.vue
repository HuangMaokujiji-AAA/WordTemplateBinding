<script setup lang="ts">
import { ref, onMounted } from "vue";
import { useRouter } from "vue-router";
import {
  listProjects,
  createProject,
  archiveProject,
  restoreProject,
} from "../api/client";
import type { ProjectRecord } from "../api/types";
import ErrorBanner from "../shared/components/ErrorBanner.vue";
import PaginationControls from "../shared/components/PaginationControls.vue";
import StatusBadge from "../shared/components/StatusBadge.vue";

const router = useRouter();

/* ── state ───────────────────────────────── */

const projects = ref<ProjectRecord[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const query = ref("");
const statusFilter = ref("");
const loading = ref(false);
const error = ref("");

const showCreateDialog = ref(false);
const creating = ref(false);
const createError = ref("");
const createForm = ref({
  projectCode: "",
  projectName: "",
  description: "",
});

const statusConfig: Record<string, { label: string; color: string }> = {
  DRAFT: { label: "草稿", color: "#6b7280" },
  CONFIGURING: { label: "配置中", color: "#3b82f6" },
  READY: { label: "就绪", color: "#22c55e" },
  ARCHIVED: { label: "已归档", color: "#f97316" },
};

function getStatusConfig(s: string) {
  return statusConfig[s] ?? { label: s, color: "#6b7280" };
}

/* ── data fetching ───────────────────────── */

async function fetchProjects() {
  loading.value = true;
  error.value = "";
  try {
    const res = await listProjects({
      query: query.value || undefined,
      status: statusFilter.value || undefined,
      page: page.value,
      pageSize: pageSize.value,
    });
    projects.value = res.items;
    total.value = res.total;
  } catch (e: any) {
    error.value = e.message ?? "加载项目列表失败";
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  page.value = 1;
  fetchProjects();
}

function goToPage(p: number) {
  page.value = p;
  fetchProjects();
}

/* ── create ──────────────────────────────── */

function openCreateDialog() {
  createForm.value = { projectCode: "", projectName: "", description: "" };
  createError.value = "";
  showCreateDialog.value = true;
}

async function handleCreate() {
  const code = createForm.value.projectCode.trim();
  const name = createForm.value.projectName.trim();

  if (!code || !name) {
    createError.value = "项目编码和项目名称为必填项";
    return;
  }

  creating.value = true;
  createError.value = "";
  try {
    const project = await createProject({
      projectCode: code,
      projectName: name,
      description: createForm.value.description.trim() || undefined,
    });
    showCreateDialog.value = false;
    router.push(`/projects/${project.projectId}`);
  } catch (e: any) {
    createError.value = e.message ?? "创建项目失败";
  } finally {
    creating.value = false;
  }
}

/* ── archive / restore ───────────────────── */

async function handleArchive(project: ProjectRecord) {
  try {
    await archiveProject(project.projectId, project.rowVersion);
    await fetchProjects();
  } catch (e: any) {
    error.value = e.message ?? "归档项目失败";
  }
}

async function handleRestore(project: ProjectRecord) {
  try {
    await restoreProject(project.projectId, project.rowVersion);
    await fetchProjects();
  } catch (e: any) {
    error.value = e.message ?? "恢复项目失败";
  }
}

/* ── lifecycle ───────────────────────────── */

onMounted(fetchProjects);
</script>

<template>
  <div class="project-list-view">
    <!-- header -->
    <div class="page-header">
      <h1 class="page-title">项目管理</h1>
      <button class="btn btn-primary" @click="openCreateDialog">新建项目</button>
    </div>

    <!-- filters -->
    <div class="filters-bar">
      <div class="search-box">
        <input
          v-model="query"
          type="text"
          placeholder="搜索项目名称或编码…"
          @keyup.enter="handleSearch"
        />
        <button class="btn btn-secondary" @click="handleSearch">搜索</button>
      </div>
      <select v-model="statusFilter" @change="handleSearch">
        <option value="">全部状态</option>
        <option value="DRAFT">草稿</option>
        <option value="CONFIGURING">配置中</option>
        <option value="READY">就绪</option>
        <option value="ARCHIVED">已归档</option>
      </select>
    </div>

    <!-- error -->
    <ErrorBanner :message="error" />

    <!-- loading -->
    <div v-if="loading" class="loading-state">加载中…</div>

    <!-- empty -->
    <div v-else-if="!loading && projects.length === 0" class="empty-state">
      <div class="empty-icon">
        <svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
          <polyline points="14 2 14 8 20 8" />
          <line x1="16" y1="13" x2="8" y2="13" />
          <line x1="16" y1="17" x2="8" y2="17" />
          <polyline points="10 9 9 9 8 9" />
        </svg>
      </div>
      <p class="empty-text">暂无项目数据</p>
      <p class="empty-hint">点击「新建项目」按钮创建第一个项目</p>
    </div>

    <!-- table -->
    <div v-else class="card">
      <table class="data-table">
        <thead>
          <tr>
            <th>项目编码</th>
            <th>项目名称</th>
            <th>状态</th>
            <th>更新时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="project in projects" :key="project.projectId">
            <td class="cell-code">{{ project.projectCode }}</td>
            <td class="cell-name">{{ project.projectName }}</td>
            <td>
              <StatusBadge
                :label="getStatusConfig(project.projectStatus).label"
                :color="getStatusConfig(project.projectStatus).color"
              />
            </td>
            <td class="cell-date">{{ project.updatedAt }}</td>
            <td>
              <div class="actions-cell">
                <button
                  class="btn btn-sm"
                  @click="router.push(`/projects/${project.projectId}`)"
                >
                  查看
                </button>
                <button
                  class="btn btn-sm"
                  @click="router.push(`/workspace?projectId=${project.projectId}`)"
                >
                  进入绑定
                </button>
                <button
                  v-if="project.projectStatus !== 'ARCHIVED'"
                  class="btn btn-sm btn-outline-danger"
                  @click="handleArchive(project)"
                >
                  归档
                </button>
                <button
                  v-else
                  class="btn btn-sm btn-outline"
                  @click="handleRestore(project)"
                >
                  恢复
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- pagination -->
    <PaginationControls
      :page="page"
      :page-size="pageSize"
      :total="total"
      @change="goToPage"
    />

    <!-- create dialog -->
    <div v-if="showCreateDialog" class="dialog-overlay" @click.self="showCreateDialog = false">
      <div class="dialog">
        <div class="dialog-header">
          <h2>新建项目</h2>
          <button class="dialog-close" @click="showCreateDialog = false">&times;</button>
        </div>

        <div class="dialog-body">
          <ErrorBanner :message="createError" />

          <div class="form-group">
            <label>项目编码 <span class="required">*</span></label>
            <input
              v-model="createForm.projectCode"
              type="text"
              placeholder="例如 PROJ-001"
            />
          </div>

          <div class="form-group">
            <label>项目名称 <span class="required">*</span></label>
            <input
              v-model="createForm.projectName"
              type="text"
              placeholder="请输入项目名称"
            />
          </div>

          <div class="form-group">
            <label>项目描述</label>
            <textarea
              v-model="createForm.description"
              placeholder="可选，输入项目描述"
              rows="3"
            ></textarea>
          </div>
        </div>

        <div class="dialog-footer">
          <button class="btn" @click="showCreateDialog = false">取消</button>
          <button class="btn btn-primary" :disabled="creating" @click="handleCreate">
            {{ creating ? "创建中…" : "确认创建" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── layout ────────────────────────────────── */

.project-list-view {
  padding: 28px 32px;
  background: #f1f5f9;
  min-height: 100%;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 24px;
}

.page-title {
  font-size: 22px;
  font-weight: 700;
  color: #1e293b;
  margin: 0;
}

/* ── buttons ──────────────────────────────── */

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

.btn-primary {
  background: #3b82f6;
  border-color: #3b82f6;
  color: #fff;
}
.btn-primary:hover {
  background: #2563eb;
  border-color: #2563eb;
}

.btn-secondary {
  background: #f1f5f9;
  border-color: #d1d9e6;
  color: #475569;
}
.btn-secondary:hover {
  background: #e2e8f0;
  border-color: #b8c5d6;
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

/* ── filters ──────────────────────────────── */

.filters-bar {
  display: flex;
  gap: 12px;
  margin-bottom: 20px;
  align-items: center;
}

.search-box {
  display: flex;
  gap: 8px;
  flex: 1;
  max-width: 420px;
}

.search-box input {
  flex: 1;
  padding: 8px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  background: #fff;
  font-size: 13px;
  color: #1e293b;
  outline: none;
  font-family: inherit;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.search-box input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

.search-box input::placeholder {
  color: #94a3b8;
}

.filters-bar select {
  padding: 8px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  background: #fff;
  font-size: 13px;
  color: #1e293b;
  outline: none;
  font-family: inherit;
  transition: border-color 0.15s, box-shadow 0.15s;
  min-width: 110px;
}

.filters-bar select:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

/* ── card / table ─────────────────────────── */

.card {
  background: #fff;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  overflow: hidden;
}

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
}

.cell-name {
  font-weight: 500;
}

.cell-date {
  color: #64748b;
  white-space: nowrap;
}

.actions-cell {
  display: flex;
  gap: 6px;
  flex-wrap: nowrap;
}

/* ── loading ──────────────────────────────── */

.loading-state {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 64px 0;
  color: #94a3b8;
  font-size: 14px;
}

/* ── empty ────────────────────────────────── */

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 64px 0;
  background: #fff;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
}

.empty-icon {
  margin-bottom: 16px;
  opacity: 0.5;
}

.empty-text {
  font-size: 15px;
  color: #64748b;
  margin: 0 0 4px;
  font-weight: 500;
}

.empty-hint {
  font-size: 13px;
  color: #94a3b8;
  margin: 0;
}

/* ── dialog ───────────────────────────────── */

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

/* ── form ─────────────────────────────────── */

.form-group {
  margin-bottom: 18px;
}

.form-group:last-child {
  margin-bottom: 0;
}

.form-group label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  color: #374151;
  margin-bottom: 6px;
}

.form-group .required {
  color: #ef4444;
}

.form-group input,
.form-group textarea {
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
.form-group textarea:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

.form-group textarea {
  resize: vertical;
}
</style>
