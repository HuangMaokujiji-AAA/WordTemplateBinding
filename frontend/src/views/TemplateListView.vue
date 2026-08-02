<script setup lang="ts">
import { ref, reactive, onMounted } from "vue";
import { useRouter } from "vue-router";
import {
  listPersistentTemplates,
  uploadPersistentTemplate,
  archiveTemplate,
  restoreTemplate,
} from "../api/client";
import type { TemplateRecord } from "../api/types";
import ErrorBanner from "../shared/components/ErrorBanner.vue";
import PaginationControls from "../shared/components/PaginationControls.vue";
import StatusBadge from "../shared/components/StatusBadge.vue";
import { formatDateTime } from "../shared/utils/dateTime";

const router = useRouter();

/* ── state ───────────────────────────────── */

const items = ref<TemplateRecord[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 20;
const searchText = ref("");
const typeFilter = ref("");
const statusFilter = ref("");
const loading = ref(false);
const error = ref("");

/* ── upload dialog (new template) ────────── */

const showUpload = ref(false);
const uploading = ref(false);
const uploadError = ref("");
const uploadFile = ref<File | null>(null);
const uploadForm = reactive({
  templateCode: "",
  templateName: "",
  templateType: "SECTION",
  categoryCode: "",
  description: "",
});

/* ── version upload dialog ──────────────── */

const showVersionUpload = ref(false);
const versionUploading = ref(false);
const versionUploadError = ref("");
const versionTemplateId = ref("");
const versionTemplateName = ref("");
const versionFile = ref<File | null>(null);

const statusConfig: Record<string, { label: string; color: string }> = {
  ACTIVE: { label: "启用", color: "#22c55e" },
  ARCHIVED: { label: "已归档", color: "#f97316" },
  READY: { label: "就绪", color: "#3b82f6" },
};

function getStatusConfig(s: string) {
  return statusConfig[s] ?? { label: s, color: "#6b7280" };
}

const typeLabels: Record<string, string> = {
  MASTER: "主模板",
  SECTION: "章节模板",
  COMPONENT: "组件模板",
};

function getTypeLabel(t: string): string {
  return typeLabels[t] ?? t;
}

/* ── data fetching ──────────────────────── */

async function fetchTemplates() {
  loading.value = true;
  error.value = "";
  try {
    const res = await listPersistentTemplates({
      name: searchText.value || undefined,
      type: typeFilter.value || undefined,
      status: statusFilter.value || undefined,
      page: page.value,
      pageSize,
    });
    items.value = res.items;
    total.value = res.total;
  } catch (e: any) {
    error.value = e.message ?? "加载模板列表失败";
  } finally {
    loading.value = false;
  }
}

function handleSearch() {
  page.value = 1;
  fetchTemplates();
}

function goToPage(p: number) {
  page.value = p;
  fetchTemplates();
}

/* ── upload (new template) ──────────────── */

function openUploadDialog() {
  uploadForm.templateCode = `TPL_${Date.now()}`;
  uploadForm.templateName = "";
  uploadForm.templateType = "SECTION";
  uploadForm.categoryCode = "";
  uploadForm.description = "";
  uploadFile.value = null;
  uploadError.value = "";
  showUpload.value = true;
}

async function handleUpload() {
  if (!uploadFile.value) {
    uploadError.value = "请选择要上传的 DOCX 文件";
    return;
  }

  uploading.value = true;
  uploadError.value = "";
  try {
    const body = new FormData();
    body.append("file", uploadFile.value);
    body.append("templateCode", uploadForm.templateCode || `TPL_${Date.now()}`);
    body.append(
      "templateName",
      uploadForm.templateName ||
        uploadFile.value.name.replace(/\.docx$/i, ""),
    );
    body.append("templateType", uploadForm.templateType);
    if (uploadForm.categoryCode)
      body.append("categoryCode", uploadForm.categoryCode);
    if (uploadForm.description)
      body.append("description", uploadForm.description);

    const response = await fetch("/api/templates", { method: "POST", body });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      throw new Error(
        body.detail || body.title || `上传失败（HTTP ${response.status}）`,
      );
    }
    showUpload.value = false;
    await fetchTemplates();
  } catch (e: any) {
    uploadError.value = e.message ?? "上传模板失败";
  } finally {
    uploading.value = false;
  }
}

/* ── upload new version ─────────────────── */

function openVersionUpload(item: TemplateRecord) {
  versionTemplateId.value = item.id;
  versionTemplateName.value = item.templateName;
  versionFile.value = null;
  versionUploadError.value = "";
  showVersionUpload.value = true;
}

async function handleVersionUpload() {
  if (!versionFile.value) {
    versionUploadError.value = "请选择要上传的 DOCX 文件";
    return;
  }

  versionUploading.value = true;
  versionUploadError.value = "";
  try {
    await uploadPersistentTemplate(versionFile.value, versionTemplateId.value);
    showVersionUpload.value = false;
    await fetchTemplates();
  } catch (e: any) {
    versionUploadError.value = e.message ?? "上传新版本失败";
  } finally {
    versionUploading.value = false;
  }
}

/* ── archive / restore ──────────────────── */

async function handleArchive(item: TemplateRecord) {
  try {
    await archiveTemplate(item.id);
    await fetchTemplates();
  } catch (e: any) {
    error.value = e.message ?? "归档模板失败";
  }
}

async function handleRestore(item: TemplateRecord) {
  try {
    await restoreTemplate(item.id);
    await fetchTemplates();
  } catch (e: any) {
    error.value = e.message ?? "恢复模板失败";
  }
}

/* ── lifecycle ──────────────────────────── */

onMounted(fetchTemplates);
</script>

<template>
  <div class="template-list-view">
    <!-- header -->
    <div class="page-header">
      <h1 class="page-title">模板管理</h1>
      <button class="btn btn-primary" @click="openUploadDialog">上传模板</button>
    </div>

    <!-- filters -->
    <div class="filters-bar">
      <div class="search-box">
        <input
          v-model="searchText"
          type="text"
          placeholder="搜索模板名称…"
          @keyup.enter="handleSearch"
        />
        <button class="btn btn-secondary" @click="handleSearch">搜索</button>
      </div>
      <select v-model="typeFilter" @change="handleSearch">
        <option value="">全部类型</option>
        <option value="MASTER">主模板</option>
        <option value="SECTION">章节模板</option>
        <option value="COMPONENT">组件模板</option>
      </select>
      <select v-model="statusFilter" @change="handleSearch">
        <option value="">全部状态</option>
        <option value="ACTIVE">启用</option>
        <option value="ARCHIVED">已归档</option>
      </select>
    </div>

    <!-- error -->
    <ErrorBanner :message="error" />

    <!-- loading -->
    <div v-if="loading" class="loading-state">加载中…</div>

    <!-- empty -->
    <div v-else-if="!loading && items.length === 0" class="empty-state">
      <div class="empty-icon">
        <svg
          width="56"
          height="56"
          viewBox="0 0 24 24"
          fill="none"
          stroke="#94a3b8"
          stroke-width="1.2"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
          <polyline points="14 2 14 8 20 8" />
          <line x1="16" y1="13" x2="8" y2="13" />
          <line x1="16" y1="17" x2="8" y2="17" />
          <polyline points="10 9 9 9 8 9" />
        </svg>
      </div>
      <p class="empty-text">暂无模板数据</p>
      <p class="empty-hint">点击「上传模板」按钮上传第一个模板</p>
    </div>

    <!-- table -->
    <div v-else class="card">
      <table class="data-table">
        <thead>
          <tr>
            <th>模板编码</th>
            <th>模板名称</th>
            <th>类型</th>
            <th>状态</th>
            <th>当前版本</th>
            <th>元素数</th>
            <th>更新时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in items" :key="item.id">
            <td class="cell-code">{{ item.templateCode }}</td>
            <td class="cell-name">{{ item.templateName }}</td>
            <td>{{ getTypeLabel(item.templateType) }}</td>
            <td>
              <StatusBadge
                :label="getStatusConfig(item.templateStatus).label"
                :color="getStatusConfig(item.templateStatus).color"
              />
            </td>
            <td class="cell-version">{{ item.currentVersionNo }}</td>
            <td class="cell-elements">{{ (item as any).elementCount ?? '-' }}</td>
            <td class="cell-date">{{ formatDateTime(item.updatedAt) }}</td>
            <td>
              <div class="actions-cell">
                <button
                  class="btn btn-sm"
                  @click="router.push(`/template-center/templates/${item.id}`)"
                >
                  查看
                </button>
                <button
                  class="btn btn-sm"
                  @click="router.push(`/template-center/studio?templateId=${item.id}`)"
                >
                  进入绑定
                </button>
                <button
                  class="btn btn-sm"
                  @click="openVersionUpload(item)"
                >
                  上传新版本
                </button>
                <button
                  v-if="item.templateStatus !== 'ARCHIVED'"
                  class="btn btn-sm btn-outline-danger"
                  @click="handleArchive(item)"
                >
                  归档
                </button>
                <button
                  v-else
                  class="btn btn-sm btn-outline"
                  @click="handleRestore(item)"
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

    <!-- upload dialog (new template) -->
    <div
      v-if="showUpload"
      class="dialog-overlay"
      @click.self="showUpload = false"
    >
      <div class="dialog">
        <div class="dialog-header">
          <h2>上传模板</h2>
          <button class="dialog-close" @click="showUpload = false">
            &times;
          </button>
        </div>

        <div class="dialog-body">
          <ErrorBanner :message="uploadError" />

          <div class="form-group">
            <label>文件 <span class="required">*</span></label>
            <div class="file-input-row">
              <input
                type="file"
                accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                @change="
                  uploadFile = ($event.target as HTMLInputElement).files?.[0] ?? null
                "
              />
            </div>
          </div>

          <div class="form-group">
            <label>模板编码 <span class="required">*</span></label>
            <input v-model="uploadForm.templateCode" type="text" placeholder="自动生成，可修改" />
          </div>

          <div class="form-group">
            <label>模板名称 <span class="required">*</span></label>
            <input
              v-model="uploadForm.templateName"
              type="text"
              placeholder="请输入模板名称"
            />
          </div>

          <div class="form-group">
            <label>模板类型 <span class="required">*</span></label>
            <select v-model="uploadForm.templateType">
              <option value="MASTER">主模板</option>
              <option value="SECTION">章节模板</option>
              <option value="COMPONENT">组件模板</option>
            </select>
          </div>

          <div class="form-group">
            <label>分类编码</label>
            <input
              v-model="uploadForm.categoryCode"
              type="text"
              placeholder="可选，输入分类编码"
            />
          </div>

          <div class="form-group">
            <label>描述</label>
            <textarea
              v-model="uploadForm.description"
              placeholder="可选，输入模板描述"
              rows="3"
            ></textarea>
          </div>
        </div>

        <div class="dialog-footer">
          <button class="btn" @click="showUpload = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="uploading"
            @click="handleUpload"
          >
            {{ uploading ? "上传中…" : "确认上传" }}
          </button>
        </div>
      </div>
    </div>

    <!-- version upload dialog -->
    <div
      v-if="showVersionUpload"
      class="dialog-overlay"
      @click.self="showVersionUpload = false"
    >
      <div class="dialog">
        <div class="dialog-header">
          <h2>上传新版本</h2>
          <button class="dialog-close" @click="showVersionUpload = false">
            &times;
          </button>
        </div>

        <div class="dialog-body">
          <ErrorBanner :message="versionUploadError" />

          <div class="form-group">
            <label>模板</label>
            <p class="form-hint">{{ versionTemplateName }}</p>
          </div>

          <div class="form-group">
            <label>文件 <span class="required">*</span></label>
            <div class="file-input-row">
              <input
                type="file"
                accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                @change="
                  versionFile = ($event.target as HTMLInputElement).files?.[0] ?? null
                "
              />
            </div>
          </div>
        </div>

        <div class="dialog-footer">
          <button class="btn" @click="showVersionUpload = false">取消</button>
          <button
            class="btn btn-primary"
            :disabled="versionUploading"
            @click="handleVersionUpload"
          >
            {{ versionUploading ? "上传中…" : "确认上传" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── layout ────────────────────────────────── */

.template-list-view {
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
  white-space: nowrap;
}

.cell-name {
  font-weight: 500;
}

.cell-version,
.cell-elements {
  text-align: center;
  color: #64748b;
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

.file-input-row input[type="file"] {
  padding: 6px 0;
  border: none;
  box-shadow: none;
}

.form-hint {
  margin: 0;
  padding: 8px 12px;
  background: #f8fafc;
  border-radius: 6px;
  color: #475569;
  font-size: 13px;
}
</style>
