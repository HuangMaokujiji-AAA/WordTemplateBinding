<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  getTemplateStudioWorkspace,
  rescanTemplateVersion,
} from "../../../api/client";
import type {
  TemplateElementRecord,
  TemplateStudioWorkspace,
} from "../../../api/types";
import StatusBadge from "../../../shared/components/StatusBadge.vue";
import type {
  TemplateStudioContext,
  TemplateStudioContextPatch,
} from "../types";

const props = defineProps<{
  context: TemplateStudioContext;
}>();

const emit = defineEmits<{
  "update-context": [patch: TemplateStudioContextPatch];
  complete: [];
}>();

const workspace = ref<TemplateStudioWorkspace | null>(null);
const selectedElementId = ref("");
const loading = ref(false);
const message = ref("");

const groups = computed(() => {
  const elements = workspace.value?.versionView.elements || [];
  return [
    {
      key: "TEXT",
      label: "文本与数字",
      items: elements.filter((item) => item.elementType === "TEXT"),
    },
    {
      key: "CHART",
      label: "Word 原生图表",
      items: elements.filter((item) => item.elementType === "CHART"),
    },
    {
      key: "TABLE",
      label: "表格与重复区域",
      items: elements.filter((item) =>
        ["TABLE", "REPEAT_BLOCK"].includes(item.elementType)
      ),
    },
    {
      key: "OTHER",
      label: "其他与待处理",
      items: elements.filter(
        (item) =>
          !["TEXT", "CHART", "TABLE", "REPEAT_BLOCK"].includes(
            item.elementType
          )
      ),
    },
  ].filter((group) => group.items.length > 0);
});

const selectedElement = computed<TemplateElementRecord | null>(
  () =>
    workspace.value?.versionView.elements.find(
      (item) => item.id === selectedElementId.value
    ) || null
);

async function loadWorkspace(versionId = props.context.versionId): Promise<void> {
  if (!props.context.templateId) return;
  loading.value = true;
  message.value = "";
  try {
    workspace.value = await getTemplateStudioWorkspace(
      props.context.templateId,
      { versionId: versionId || undefined }
    );
    const version = workspace.value.versionView.version;
    selectedElementId.value =
      workspace.value.versionView.elements[0]?.id || "";
    emit("update-context", { versionId: version.id });
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "加载动态内容失败。";
  } finally {
    loading.value = false;
  }
}

async function rescan(): Promise<void> {
  if (!workspace.value) return;
  loading.value = true;
  message.value = "正在重新扫描保存的原始 DOCX…";
  try {
    const result = await rescanTemplateVersion(
      workspace.value.versionView.version.id
    );
    await loadWorkspace(result.version.id);
    message.value = "重新扫描完成，原始 DOCX 内容未被修改。";
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "重新扫描失败。";
  } finally {
    loading.value = false;
  }
}

function statusColor(status: string): string {
  if (status.toUpperCase() === "VALID") return "#16805c";
  if (status.toUpperCase() === "WARNING") return "#b7791f";
  return "#b42318";
}

onMounted(() => void loadWorkspace());
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>标记动态内容</h2>
        <p>
          检查系统识别的文本、数字、表格和 Word 原生图表。元素键来自文档定位信息，不使用显示标题作为稳定键。
        </p>
      </div>
      <button
        type="button"
        class="studio-button"
        :disabled="loading || !workspace"
        @click="rescan"
      >
        重新扫描
      </button>
    </header>

    <div v-if="!context.templateId" class="studio-step-body">
      <div class="studio-empty">请先创建模板并确认结构。</div>
    </div>
    <div v-else class="studio-step-body">
      <div v-if="workspace" class="studio-metrics">
        <div class="studio-metric">
          <strong>{{ workspace.summary.elementCount }}</strong>
          <span>动态元素</span>
        </div>
        <div class="studio-metric">
          <strong>{{ workspace.summary.validElementCount }}</strong>
          <span>可绑定</span>
        </div>
        <div class="studio-metric">
          <strong>{{ workspace.summary.chartCount }}</strong>
          <span>Word 原生图表</span>
        </div>
        <div class="studio-metric">
          <strong>{{ workspace.summary.tableCount }}</strong>
          <span>自动识别表格</span>
        </div>
        <div class="studio-metric">
          <strong>{{ workspace.summary.unsupportedElementCount }}</strong>
          <span>不支持或需处理</span>
        </div>
      </div>

      <div v-if="workspace" class="element-layout">
        <div class="element-groups">
          <section v-for="group in groups" :key="group.key">
            <h3>{{ group.label }} <span>{{ group.items.length }}</span></h3>
            <button
              v-for="element in group.items"
              :key="element.id"
              type="button"
              class="element-item"
              :class="{ active: element.id === selectedElementId }"
              @click="selectedElementId = element.id"
            >
              <span>
                <strong>{{ element.displayName || element.elementKey }}</strong>
                <small>{{ element.elementType }} · {{ element.locatorType }}</small>
              </span>
              <StatusBadge
                :label="element.parseStatus"
                :color="statusColor(element.parseStatus)"
              />
            </button>
          </section>
        </div>

        <aside class="element-detail">
          <template v-if="selectedElement">
            <span class="detail-kicker">元素详情</span>
            <h3>{{ selectedElement.displayName || "未命名元素" }}</h3>
            <dl>
              <div>
                <dt>稳定元素键</dt>
                <dd>{{ selectedElement.elementKey }}</dd>
              </div>
              <div>
                <dt>元素类型</dt>
                <dd>{{ selectedElement.elementType }}</dd>
              </div>
              <div>
                <dt>定位方式</dt>
                <dd>{{ selectedElement.locatorType }}</dd>
              </div>
              <div>
                <dt>所属片段</dt>
                <dd>{{ selectedElement.segmentId || "全局" }}</dd>
              </div>
              <div>
                <dt>是否必填</dt>
                <dd>{{ selectedElement.isRequired ? "是" : "否" }}</dd>
              </div>
            </dl>
            <div
              v-if="selectedElement.parseMessage"
              class="studio-message"
              :class="{ error: selectedElement.parseStatus !== 'VALID' }"
            >
              {{ selectedElement.parseMessage }}
            </div>
          </template>
          <div v-else class="studio-empty">当前版本没有识别到动态元素。</div>
          <div class="marking-guide">
            <strong>缺少标记？</strong>
            <span>
              可在 Word/WPS 中使用 <code v-text="'{{path}}'"></code>、
              黄色高亮或原生图表后重新上传版本。
            </span>
          </div>
        </aside>
      </div>

      <div v-if="message" class="studio-message">{{ message }}</div>
      <div class="studio-actions">
        <button
          type="button"
          class="studio-button primary"
          :disabled="loading || !workspace"
          @click="emit('complete')"
        >
          动态内容确认完成，继续
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.element-layout {
  display: grid;
  grid-template-columns: minmax(0, 1.5fr) minmax(280px, 0.7fr);
  gap: 14px;
  margin-top: 16px;
}

.element-groups,
.element-detail {
  padding: 14px;
  border: 1px solid #e0e6ef;
  border-radius: 10px;
  background: #f9fbfd;
}

.element-groups section + section {
  margin-top: 16px;
}

.element-groups h3 {
  margin: 0 0 8px;
  color: #344054;
  font-size: 12px;
}

.element-groups h3 span {
  color: #8a95a6;
  font-size: 10px;
}

.element-item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  width: 100%;
  margin-bottom: 5px;
  padding: 9px 10px;
  border: 1px solid #e1e6ee;
  border-radius: 8px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.element-item span,
.element-item strong,
.element-item small {
  display: block;
}

.element-item small {
  margin-top: 2px;
  color: #8a95a6;
  font-size: 9px;
}

.element-item.active {
  border-color: #b9c7f6;
  background: #f0f3ff;
}

.detail-kicker {
  color: #3157d5;
  font-size: 9px;
  font-weight: 800;
  letter-spacing: 0.1em;
}

.element-detail h3 {
  margin: 5px 0 14px;
}

dl {
  display: grid;
  gap: 9px;
  margin: 0;
}

dl div {
  display: grid;
  gap: 2px;
}

dt {
  color: #8a95a6;
  font-size: 9px;
}

dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: #344054;
  font-size: 11px;
}

.marking-guide {
  display: grid;
  gap: 4px;
  margin-top: 18px;
  padding: 11px;
  border: 1px solid #dce4ef;
  border-radius: 8px;
  background: #fff;
  color: #667085;
  font-size: 10px;
}

code {
  color: #2949b8;
}
</style>
