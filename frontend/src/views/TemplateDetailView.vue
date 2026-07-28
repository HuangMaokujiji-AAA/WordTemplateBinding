<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  getPersistentTemplate,
  updateTemplate,
  archiveTemplate,
  restoreTemplate,
  listTemplateVersions,
  getTemplateVersionFile,
  rescanTemplateVersion,
  uploadPersistentTemplate,
} from "../api/client";
import type {
  TemplateRecord,
  TemplateVersionView,
  TemplateElementRecord,
} from "../api/types";

const route = useRoute();
const router = useRouter();

/* ── state ────────────────────────────────── */

const templateId = computed(() => (route.params.templateId as string) || "");
const template = ref<TemplateRecord | null>(null);
const rowVersion = ref(0);
const versions = ref<TemplateVersionView[]>([]);
const loading = ref(false);
const error = ref("");

const editing = ref(false);
const editForm = ref({
  templateName: "",
  categoryCode: "",
  description: "",
  templateStatus: "",
});

const selectedVersion = ref<TemplateVersionView | null>(null);
const elementFilter = ref("全部");

const uploadFile = ref<File | null>(null);
const changeSummary = ref("");
const uploading = ref(false);

const confirmDialog = ref<{
  show: boolean;
  title: string;
  message: string;
  action: () => Promise<void>;
}>({ show: false, title: "", message: "", action: async () => {} });

/* ── computed ─────────────────────────────── */

const filteredElements = computed<TemplateElementRecord[]>(() => {
  if (!selectedVersion.value) return [];
  const elements = selectedVersion.value.elements;
  const filter = elementFilter.value;

  if (filter === "全部") return elements;
  if (filter === "文字") return elements.filter((e) => e.elementType === "TEXT");
  if (filter === "图表") return elements.filter((e) => e.elementType === "CHART");
  if (filter === "警告") return elements.filter((e) => e.parseStatus === "Warning");
  if (filter === "不支持")
    return elements.filter(
      (e) =>
        e.parseStatus === "Unsupported" ||
        e.parseStatus === "NotSupported" ||
        e.parseStatus === "Error"
    );
  return elements;
});

const statusConfig: Record<string, { label: string; color: string }> = {
  DRAFT: { label: "草稿", color: "#6b7280" },
  ACTIVE: { label: "启用", color: "#22c55e" },
  ARCHIVED: { label: "已归档", color: "#f97316" },
};

function getStatusConfig(s: string) {
  return statusConfig[s] ?? { label: s, color: "#6b7280" };
}

const templateTypeLabel: Record<string, string> = {
  SECTION: "节模板",
  DOCUMENT: "文档模板",
  EMAIL: "邮件模板",
};

function getTemplateTypeLabel(t: string) {
  return templateTypeLabel[t] ?? t;
}

/* ── data fetching ────────────────────────── */

async function fetchTemplate() {
  if (!templateId.value) return;
  loading.value = true;
  error.value = "";
  try {
    const data = await getPersistentTemplate(templateId.value);
    template.value = data;
    rowVersion.value = (data as any).rowVersion ?? 0;
  } catch (e: any) {
    error.value = e.message ?? "加载模板失败";
  } finally {
    loading.value = false;
  }
}

async function fetchVersions() {
  if (!templateId.value) return;
  try {
    versions.value = await listTemplateVersions(templateId.value);
  } catch (e: any) {
    error.value = e.message ?? "加载版本列表失败";
  }
}

/* ── edit ─────────────────────────────────── */

function toggleEdit() {
  if (!template.value) return;
  if (editing.value) {
    editing.value = false;
    return;
  }
  editForm.value = {
    templateName: template.value.templateName,
    categoryCode: template.value.categoryCode ?? "",
    description: template.value.description ?? "",
    templateStatus: template.value.templateStatus,
  };
  editing.value = true;
}

async function saveEdit() {
  if (!template.value || !templateId.value) return;
  loading.value = true;
  error.value = "";
  try {
    const data = await updateTemplate(templateId.value, {
      templateName: editForm.value.templateName || undefined,
      categoryCode: editForm.value.categoryCode || null,
      description: editForm.value.description || null,
      templateStatus: editForm.value.templateStatus || undefined,
      expectedRowVersion: rowVersion.value,
    });
    template.value = data;
    rowVersion.value = (data as any).rowVersion ?? 0;
    editing.value = false;
  } catch (e: any) {
    error.value = e.message ?? "保存模板信息失败";
  } finally {
    loading.value = false;
  }
}

/* ── archive / restore ────────────────────── */

function handleArchive() {
  if (!template.value) return;
  confirmDialog.value = {
    show: true,
    title: "归档确认",
    message: `确定要归档模板「${template.value.templateName}」吗？归档后模板将不可用。`,
    action: async () => {
      try {
        await archiveTemplate(templateId.value);
        await fetchTemplate();
        await fetchVersions();
      } catch (e: any) {
        error.value = e.message ?? "归档模板失败";
      }
    },
  };
}

function handleRestore() {
  if (!template.value) return;
  confirmDialog.value = {
    show: true,
    title: "恢复确认",
    message: `确定要恢复模板「${template.value.templateName}」吗？`,
    action: async () => {
      try {
        await restoreTemplate(templateId.value);
        await fetchTemplate();
        await fetchVersions();
      } catch (e: any) {
        error.value = e.message ?? "恢复模板失败";
      }
    },
  };
}

/* ── workspace navigation ─────────────────── */

function goToWorkspace() {
  router.push(`/template-center/studio?templateId=${templateId.value}`);
}

/* ── version operations ───────────────────── */

function selectVersion(version: TemplateVersionView) {
  selectedVersion.value = version;
  elementFilter.value = "全部";
}

async function downloadFile(version: TemplateVersionView) {
  try {
    const file = await getTemplateVersionFile(
      version.version.id,
      version.file.originalName
    );
    const url = URL.createObjectURL(file);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = version.file.originalName;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  } catch (e: any) {
    error.value = e.message ?? "下载文件失败";
  }
}

async function rescan(version: TemplateVersionView) {
  try {
    const updated = await rescanTemplateVersion(version.version.id);
    const idx = versions.value.findIndex(
      (v) => v.version.id === version.version.id
    );
    if (idx !== -1) {
      versions.value[idx] = updated;
    }
    if (selectedVersion.value?.version.id === version.version.id) {
      selectedVersion.value = updated;
    }
  } catch (e: any) {
    error.value = e.message ?? "重新扫描版本失败";
  }
}

/* ── upload new version ───────────────────── */

function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement;
  if (input.files && input.files.length > 0) {
    uploadFile.value = input.files[0];
  }
}

async function uploadVersion() {
  if (!uploadFile.value) {
    error.value = "请选择要上传的文件";
    return;
  }
  uploading.value = true;
  error.value = "";
  try {
    const result = await uploadPersistentTemplate(
      uploadFile.value,
      templateId.value
    );
    versions.value.unshift(result);
    uploadFile.value = null;
    changeSummary.value = "";
    // Reset the file input
    const fileInput = document.getElementById(
      "version-file-input"
    ) as HTMLInputElement;
    if (fileInput) fileInput.value = "";
  } catch (e: any) {
    error.value = e.message ?? "上传版本失败";
  } finally {
    uploading.value = false;
  }
}

/* ── utilities ────────────────────────────── */

function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const k = 1024;
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  const size = parseFloat((bytes / Math.pow(k, i)).toFixed(2));
  return `${size} ${units[i]}`;
}

function formatDate(dateStr: string): string {
  if (!dateStr) return "-";
  try {
    const d = new Date(dateStr);
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    const hour = String(d.getHours()).padStart(2, "0");
    const min = String(d.getMinutes()).padStart(2, "0");
    return `${year}-${month}-${day} ${hour}:${min}`;
  } catch {
    return dateStr;
  }
}

/* ── lifecycle ────────────────────────────── */

onMounted(async () => {
  await fetchTemplate();
  await fetchVersions();
});
</script>

<template>
  <div class="template-detail-view">
    <!-- header -->
    <div class="page-header">
      <button class="btn btn-sm" @click="router.push('/templates')">
        &larr; 返回模板列表
      </button>
      <h1 class="page-title">模板详情</h1>
    </div>

    <!-- error -->
    <div v-if="error" class="error-banner">{{ error }}</div>

    <!-- loading -->
    <div v-if="loading && !template" class="loading-state">加载中…</div>

    <!-- template info card -->
    <div v-if="template" class="card info-card">
      <div class="card-header-row">
        <h2 class="card-title">基本信息</h2>
        <div class="header-actions">
          <button class="btn btn-primary" @click="goToWorkspace">
            进入绑定工作区
          </button>
          <button
            v-if="template.templateStatus !== 'ARCHIVED'"
            class="btn btn-outline-danger btn-sm"
            @click="handleArchive"
          >
            归档
          </button>
          <button
            v-else
            class="btn btn-outline btn-sm"
            @click="handleRestore"
          >
            恢复
          </button>
          <button
            v-if="!editing"
            class="btn btn-sm"
            @click="toggleEdit"
          >
            编辑
          </button>
          <button
            v-if="editing"
            class="btn btn-primary btn-sm"
            :disabled="loading"
            @click="saveEdit"
          >
            {{ loading ? "保存中…" : "保存" }}
          </button>
          <button
            v-if="editing"
            class="btn btn-sm"
            @click="editing = false"
          >
            取消
          </button>
        </div>
      </div>

      <div class="info-grid">
        <div class="info-field">
          <span class="info-label">模板编码</span>
          <span class="info-value mono">{{ template.templateCode }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">模板名称</span>
          <input
            v-if="editing"
            v-model="editForm.templateName"
            class="info-input"
            type="text"
            placeholder="模板名称"
          />
          <span v-else class="info-value">{{ template.templateName }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">模板类型</span>
          <span class="info-value">{{
            getTemplateTypeLabel(template.templateType)
          }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">分类编码</span>
          <input
            v-if="editing"
            v-model="editForm.categoryCode"
            class="info-input"
            type="text"
            placeholder="分类编码"
          />
          <span v-else class="info-value">{{
            template.categoryCode || "-"
          }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">模板状态</span>
          <select
            v-if="editing"
            v-model="editForm.templateStatus"
            class="info-select"
          >
            <option value="DRAFT">草稿</option>
            <option value="ACTIVE">启用</option>
            <option value="ARCHIVED">已归档</option>
          </select>
          <span
            v-else
            class="status-tag"
            :style="{
              background: getStatusConfig(template.templateStatus).color + '18',
              color: getStatusConfig(template.templateStatus).color,
              borderColor: getStatusConfig(template.templateStatus).color + '30',
            }"
          >
            {{ getStatusConfig(template.templateStatus).label }}
          </span>
        </div>

        <div class="info-field">
          <span class="info-label">当前版本</span>
          <span class="info-value">v{{ template.currentVersionNo }}</span>
        </div>

        <div class="info-field info-field-full">
          <span class="info-label">描述</span>
          <textarea
            v-if="editing"
            v-model="editForm.description"
            class="info-textarea"
            placeholder="模板描述（可选）"
            rows="2"
          ></textarea>
          <span v-else class="info-value">{{
            template.description || "-"
          }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">创建时间</span>
          <span class="info-value date">{{ formatDate(template.createdAt) }}</span>
        </div>

        <div class="info-field">
          <span class="info-label">更新时间</span>
          <span class="info-value date">{{ formatDate(template.updatedAt) }}</span>
        </div>
      </div>
    </div>

    <!-- upload new version -->
    <div class="card upload-card">
      <h2 class="card-title">上传新版本</h2>
      <div class="upload-row">
        <input
          id="version-file-input"
          class="file-input"
          type="file"
          accept=".docx"
          @change="handleFileSelect"
        />
        <button
          class="btn btn-primary btn-sm"
          :disabled="!uploadFile || uploading"
          @click="uploadVersion"
        >
          {{ uploading ? "上传中…" : "上传" }}
        </button>
      </div>
      <div v-if="uploadFile" class="file-info">
        已选择文件：{{ uploadFile.name }}
        ({{ formatFileSize(uploadFile.size) }})
      </div>
    </div>

    <!-- version list -->
    <div class="card">
      <h2 class="card-title">版本列表</h2>
      <div v-if="versions.length === 0" class="empty-inline">
        暂无版本记录
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>版本号</th>
            <th>文件名</th>
            <th>文件大小</th>
            <th>状态</th>
            <th>元素数</th>
            <th>创建时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="version in versions"
            :key="version.version.id"
            :class="{
              'row-selected':
                selectedVersion?.version.id === version.version.id,
            }"
          >
            <td class="cell-code">v{{ version.version.versionNo }}</td>
            <td>{{ version.file.originalName }}</td>
            <td class="cell-date">{{
              formatFileSize(version.file.fileSize)
            }}</td>
            <td>
              <span
                class="status-tag"
                :style="{
                  background:
                    getStatusConfig(version.version.versionStatus).color + '18',
                  color:
                    getStatusConfig(version.version.versionStatus).color,
                  borderColor:
                    getStatusConfig(version.version.versionStatus).color +
                    '30',
                }"
              >
                {{ getStatusConfig(version.version.versionStatus).label }}
              </span>
            </td>
            <td>{{ version.version.elementCount }}</td>
            <td class="cell-date">{{
              formatDate(version.version.createdAt)
            }}</td>
            <td>
              <div class="actions-cell">
                <button
                  class="btn btn-sm"
                  @click="selectVersion(version)"
                >
                  查看元素
                </button>
                <button
                  class="btn btn-sm"
                  @click="downloadFile(version)"
                >
                  下载
                </button>
                <button
                  class="btn btn-sm"
                  @click="rescan(version)"
                >
                  重新扫描
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- elements list -->
    <div v-if="selectedVersion" class="card">
      <div class="card-header-row">
        <h2 class="card-title">
          元素列表 - 版本 v{{ selectedVersion.version.versionNo }}
        </h2>
        <div class="filter-buttons">
          <button
            v-for="f in ['全部', '文字', '图表', '警告', '不支持']"
            :key="f"
            class="btn btn-sm"
            :class="{ 'btn-primary': elementFilter === f }"
            @click="elementFilter = f"
          >
            {{ f }}
          </button>
        </div>
      </div>

      <div
        v-if="filteredElements.length === 0"
        class="empty-inline"
      >
        暂无匹配的元素
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>元素名称</th>
            <th>类型</th>
            <th>定位方式</th>
            <th>解析状态</th>
            <th>原始文字</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="element in filteredElements"
            :key="element.id"
          >
            <td class="cell-name">{{
              element.displayName || "-"
            }}</td>
            <td>{{ element.elementType }}</td>
            <td class="cell-code">{{ element.locatorType }}</td>
            <td>
              <span
                class="status-tag"
                :style="{
                  background:
                    (element.parseStatus === 'Success'
                      ? '#22c55e'
                      : element.parseStatus === 'Warning'
                        ? '#f59e0b'
                        : '#ef4444') + '18',
                  color:
                    element.parseStatus === 'Success'
                      ? '#22c55e'
                      : element.parseStatus === 'Warning'
                        ? '#f59e0b'
                        : '#ef4444',
                  borderColor:
                    (element.parseStatus === 'Success'
                      ? '#22c55e'
                      : element.parseStatus === 'Warning'
                        ? '#f59e0b'
                        : '#ef4444') + '30',
                }"
              >
                {{ element.parseStatus }}
              </span>
            </td>
            <td class="cell-locator">{{
              (element.locator as any)?.originalValue ??
              element.parseMessage ??
              "-"
            }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- confirm dialog -->
    <div
      v-if="confirmDialog.show"
      class="dialog-overlay"
      @click.self="confirmDialog.show = false"
    >
      <div class="dialog">
        <div class="dialog-header">
          <h2>{{ confirmDialog.title }}</h2>
          <button
            class="dialog-close"
            @click="confirmDialog.show = false"
          >
            &times;
          </button>
        </div>
        <div class="dialog-body">
          <p class="confirm-message">{{ confirmDialog.message }}</p>
        </div>
        <div class="dialog-footer">
          <button class="btn" @click="confirmDialog.show = false">
            取消
          </button>
          <button
            class="btn btn-primary"
            @click="
              confirmDialog.action();
              confirmDialog.show = false;
            "
          >
            确认
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── layout ────────────────────────────────── */

.template-detail-view {
  padding: 28px 32px;
  background: #f1f5f9;
  min-height: 100%;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 16px;
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

/* ── card ──────────────────────────────────── */

.card {
  background: #fff;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  overflow: hidden;
  margin-bottom: 20px;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0;
}

.card-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid #f1f5f9;
  gap: 12px;
  flex-wrap: wrap;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

/* ── info grid ────────────────────────────── */

.info-card {
  overflow: visible;
}

.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0;
  padding: 4px 0;
}

.info-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 20px;
  border-bottom: 1px solid #f8fafc;
}

.info-field-full {
  grid-column: 1 / -1;
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
  word-break: break-word;
}

.info-value.mono {
  font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  font-size: 13px;
  color: #475569;
}

.info-value.date {
  color: #64748b;
  font-size: 13px;
}

.info-input,
.info-select,
.info-textarea {
  padding: 6px 10px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  font-size: 13px;
  color: #1e293b;
  outline: none;
  font-family: inherit;
  background: #fff;
  transition: border-color 0.15s, box-shadow 0.15s;
  width: 100%;
  box-sizing: border-box;
}

.info-input:focus,
.info-select:focus,
.info-textarea:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

.info-select {
  max-width: 200px;
}

.info-textarea {
  resize: vertical;
}

/* ── upload ────────────────────────────────── */

.upload-card {
  overflow: visible;
}

.upload-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 20px;
  border-bottom: 1px solid #f1f5f9;
}

.file-input {
  font-size: 13px;
  font-family: inherit;
  color: #1e293b;
}

.file-info {
  padding: 8px 20px 16px;
  font-size: 12px;
  color: #64748b;
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

/* ── table ─────────────────────────────────── */

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

.data-table tbody tr.row-selected td {
  background: #eff6ff;
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

.cell-locator {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #64748b;
  font-size: 12px;
}

.actions-cell {
  display: flex;
  gap: 6px;
  flex-wrap: nowrap;
}

/* ── filter buttons ───────────────────────── */

.filter-buttons {
  display: flex;
  gap: 6px;
}

/* ── empty ─────────────────────────────────── */

.empty-inline {
  padding: 32px 20px;
  text-align: center;
  color: #94a3b8;
  font-size: 14px;
}

/* ── error ─────────────────────────────────── */

.error-banner {
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #ef4444;
  padding: 10px 16px;
  border-radius: 6px;
  margin-bottom: 16px;
  font-size: 13px;
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

/* ── dialog ────────────────────────────────── */

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
  width: 460px;
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

.confirm-message {
  font-size: 14px;
  color: #475569;
  margin: 0;
  line-height: 1.6;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 16px 24px;
  border-top: 1px solid #f1f5f9;
}
</style>
