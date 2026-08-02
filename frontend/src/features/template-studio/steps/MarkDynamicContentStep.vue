<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import {
  getTemplateStudioWorkspace,
  hydrateTemplateResponse,
  rescanTemplateVersion,
} from "../../../api/client";
import type {
  ChartItem,
  TemplateElementRecord,
  TemplateResponse,
  TemplateStudioWorkspace,
} from "../../../api/types";
import StatusBadge from "../../../shared/components/StatusBadge.vue";
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
  complete: [];
}>();

const workspace = ref<TemplateStudioWorkspace | null>(null);
const selectedSegmentId = ref("");
const selectedElementId = ref("");
const loading = ref(false);
const message = ref("");
const preview = ref<InstanceType<typeof StudioSegmentPreview> | null>(null);

const selectedSegment = computed(
  () =>
    workspace.value?.segments.find(
      (segment) => segment.id === selectedSegmentId.value
    ) || null
);

const segmentElements = computed(() =>
  (workspace.value?.versionView.elements || []).filter(
    (element) => element.segmentId === selectedSegmentId.value
  )
);

const segmentPreviewData = computed<TemplateResponse | null>(() => {
  const current = workspace.value;
  if (!current || !selectedSegment.value) return null;
  return hydrateTemplateResponse(
    {
      ...current.versionView,
      elements: segmentElements.value,
    },
    []
  );
});

const selectedCharts = computed(() => segmentPreviewData.value?.charts || []);

const selectedElement = computed<TemplateElementRecord | null>(
  () =>
    segmentElements.value.find(
      (element) => element.id === selectedElementId.value
    ) || null
);

const bindingGroups = computed(() => {
  const elements = segmentElements.value;
  return [
    {
      key: "TEXT",
      label: "文本与数字",
      items: elements.filter((item) => item.elementType === "TEXT"),
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
      label: "其他识别目标",
      items: elements.filter(
        (item) =>
          !["TEXT", "CHART", "TABLE", "REPEAT_BLOCK"].includes(
            item.elementType
          )
      ),
    },
  ].filter((group) => group.items.length > 0);
});

const bindingTargetCount = computed(() =>
  bindingGroups.value.reduce((total, group) => total + group.items.length, 0)
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
    const preferredSegment = workspace.value.segments.find(
      (segment) => segment.id === props.context.segmentId
    );
    selectSegment(preferredSegment?.id || workspace.value.segments[0]?.id || "");
    emit("update-context", {
      versionId: version.id,
      segmentId: selectedSegmentId.value,
    });
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

function selectSegment(segmentId: string): void {
  selectedSegmentId.value = segmentId;
  const firstElement = (workspace.value?.versionView.elements || []).find(
    (element) => element.segmentId === segmentId
  );
  selectedElementId.value = firstElement?.id || "";
  emit("update-context", { segmentId });
}

function selectPreviewTarget(locatorId: string): void {
  const element = segmentElements.value.find(
    (item) => elementLocatorId(item) === locatorId
  );
  if (element) selectedElementId.value = element.id;
}

async function focusElement(element: TemplateElementRecord): Promise<void> {
  selectedElementId.value = element.id;
  const locatorId = elementLocatorId(element);
  if (!locatorId) return;
  const targetType = ["TABLE", "REPEAT_BLOCK"].includes(element.elementType)
    ? "TABLE"
    : element.elementType === "CHART"
      ? "CHART"
      : element.elementType === "TEXT"
        ? "TEXT"
        : null;
  if (!targetType) return;
  await nextTick();
  preview.value?.focusTarget(locatorId, targetType);
}

async function focusChart(chart: ChartItem): Promise<void> {
  selectPreviewTarget(chart.locatorId);
  await nextTick();
  preview.value?.focusTarget(chart.locatorId, "CHART");
}

function elementLocatorId(element: TemplateElementRecord): string {
  return typeof element.locator.locatorId === "string"
    ? element.locator.locatorId
    : "";
}

function statusColor(status: string): string {
  if (status.toUpperCase() === "VALID") return "#16805c";
  if (status.toUpperCase() === "WARNING") return "#b7791f";
  return "#b42318";
}

onMounted(() => void loadWorkspace());
</script>

<template>
  <section class="studio-step-card marking-step">
    <header class="studio-step-header">
      <div>
        <h2>标记动态内容</h2>
        <p>
          选择第 2 步划分的片段，在 Word 页面中检查系统识别的可绑定内容；
          当前片段对应的 Word 原生图表单独展示在预览下方。
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
          <strong>{{ workspace.summary.segmentCount }}</strong>
          <span>划分片段</span>
        </div>
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
          <strong>{{ workspace.summary.unsupportedElementCount }}</strong>
          <span>不支持或需处理</span>
        </div>
      </div>

      <div v-if="workspace" class="marking-layout">
        <aside class="marking-sidebar">
          <div class="column-heading">
            <strong>结构划分页面</strong>
            <span>{{ workspace.segments.length }} 个</span>
          </div>
          <div class="segment-list">
            <button
              v-for="segment in workspace.segments"
              :key="segment.id"
              type="button"
              class="segment-item"
              :class="{ active: segment.id === selectedSegmentId }"
              @click="selectSegment(segment.id)"
            >
              <strong>{{ segment.segmentName }}</strong>
              <span>{{ segment.segmentKey }}</span>
              <small>{{ segment.elementCount }} 个识别元素</small>
            </button>
          </div>

          <div class="column-heading target-heading">
            <strong>当前片段绑定目标</strong>
            <span>{{ bindingTargetCount }} 个</span>
          </div>
          <div v-if="bindingGroups.length" class="target-groups">
            <section v-for="group in bindingGroups" :key="group.key">
              <h3>{{ group.label }} <span>{{ group.items.length }}</span></h3>
              <button
                v-for="element in group.items"
                :key="element.id"
                type="button"
                class="target-item"
                :class="{ active: element.id === selectedElementId }"
                @click="focusElement(element)"
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
          <div v-else class="studio-empty compact-empty">
            当前片段没有文本或表格绑定目标。
          </div>

          <div class="column-heading chart-heading">
            <strong>当前片段的 Word 原生图表</strong>
            <span>{{ selectedCharts.length }} 个</span>
          </div>
          <div v-if="selectedCharts.length" class="chart-name-list">
            <button
              v-for="(chart, index) in selectedCharts"
              :key="chart.locatorId"
              type="button"
              class="chart-name-item"
              :class="{
                active:
                  selectedElement?.elementType === 'CHART' &&
                  elementLocatorId(selectedElement) === chart.locatorId,
              }"
              @click="focusChart(chart)"
            >
              <span>图表 {{ index + 1 }}</span>
              <strong>{{ chart.title || `未命名图表 ${index + 1}` }}</strong>
            </button>
          </div>
          <div v-else class="studio-empty compact-empty">
            当前片段没有识别到 Word 原生图表。
          </div>

          <div v-if="selectedElement" class="element-detail">
            <strong>{{ selectedElement.displayName || "未命名元素" }}</strong>
            <dl>
              <div><dt>稳定元素键</dt><dd>{{ selectedElement.elementKey }}</dd></div>
              <div><dt>元素类型</dt><dd>{{ selectedElement.elementType }}</dd></div>
              <div><dt>定位方式</dt><dd>{{ selectedElement.locatorType }}</dd></div>
              <div><dt>是否必填</dt><dd>{{ selectedElement.isRequired ? "是" : "否" }}</dd></div>
            </dl>
            <p v-if="selectedElement.parseMessage">{{ selectedElement.parseMessage }}</p>
          </div>
        </aside>

        <div class="marking-main">
          <section class="segment-page-panel">
            <div class="panel-heading">
              <div>
                <strong>划分页面预览</strong>
                <span>{{ selectedSegment?.segmentName || "请选择片段" }}</span>
              </div>
              <small>黄色为文本，绿色为表格，橙色轮廓为图表</small>
            </div>
            <StudioSegmentPreview
              ref="preview"
              :segment="selectedSegment"
              :mock-items="segmentPreviewData?.mockItems || []"
              :charts="selectedCharts"
              :elements="segmentElements"
              :selected-locator-id="
                selectedElement ? elementLocatorId(selectedElement) : ''
              "
              @select-target="selectPreviewTarget"
            />
          </section>

        </div>
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
.marking-layout {
  display: grid;
  grid-template-columns: 290px minmax(0, 1fr);
  gap: 14px;
  margin-top: 16px;
}

.marking-sidebar,
.segment-page-panel {
  min-width: 0;
  padding: 14px;
  border: 1px solid #e0e6ef;
  border-radius: 10px;
  background: #f9fbfd;
}

.marking-main {
  display: grid;
  gap: 14px;
  min-width: 0;
}

.column-heading,
.panel-heading,
.panel-heading > div {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
}

.column-heading,
.panel-heading {
  margin-bottom: 10px;
  color: #344054;
  font-size: 11px;
}

.column-heading span,
.panel-heading span,
.panel-heading small {
  color: #8a95a6;
  font-size: 9px;
}

.segment-list {
  max-height: 260px;
  overflow: auto;
}

.segment-item {
  display: grid;
  gap: 3px;
  width: 100%;
  margin-bottom: 6px;
  padding: 9px 10px;
  border: 1px solid #e1e6ee;
  border-radius: 8px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.segment-item span,
.segment-item small {
  overflow: hidden;
  color: #8a95a6;
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.segment-item.active,
.target-item.active,
.chart-card.active {
  border-color: #9fb1f0;
  background: #eef2ff;
}

.target-heading {
  margin-top: 18px;
}

.chart-heading {
  margin-top: 18px;
}

.target-groups {
  max-height: 340px;
  overflow: auto;
}

.target-groups section + section {
  margin-top: 13px;
}

.target-groups h3 {
  margin: 0 0 6px;
  color: #475467;
  font-size: 10px;
}

.target-groups h3 span {
  color: #8a95a6;
}

.target-item {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  align-items: center;
  width: 100%;
  margin-bottom: 5px;
  padding: 8px;
  border: 1px solid #e1e6ee;
  border-radius: 7px;
  background: #fff;
  color: #344054;
  text-align: left;
}

.target-item > span,
.target-item strong,
.target-item small {
  display: block;
  min-width: 0;
}

.target-item strong,
.target-item small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.target-item small {
  margin-top: 2px;
  color: #8a95a6;
  font-size: 8px;
}

.compact-empty {
  padding: 18px 8px;
  font-size: 10px;
}

.element-detail {
  display: grid;
  gap: 8px;
  margin-top: 14px;
  padding: 10px;
  border: 1px solid #dce4ef;
  border-radius: 8px;
  background: #fff;
}

.element-detail > strong {
  color: #344054;
  font-size: 11px;
}

.element-detail dl {
  display: grid;
  gap: 6px;
  margin: 0;
}

.element-detail dl div {
  display: grid;
  gap: 1px;
}

.element-detail dt {
  color: #8a95a6;
  font-size: 8px;
}

.element-detail dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: #475467;
  font-size: 9px;
}

.element-detail p {
  margin: 0;
  color: #b42318;
  font-size: 9px;
}

.segment-page-panel {
  padding-bottom: 12px;
}

.chart-name-list {
  display: grid;
  gap: 8px;
}

.chart-name-item {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding: 9px 10px;
  border: 1px solid #e1e6ee;
  border-radius: 8px;
  background: #f9fbfd;
  color: #344054;
  text-align: left;
}

.chart-name-item:hover,
.chart-name-item.active {
  border-color: #9fb1f0;
  background: #eef2ff;
}

.chart-name-item span {
  color: #b66b00;
  font-size: 9px;
  font-weight: 700;
}

.chart-name-item strong {
  overflow: hidden;
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1080px) {
  .marking-layout {
    grid-template-columns: 250px minmax(0, 1fr);
  }

}

@media (max-width: 820px) {
  .marking-layout {
    grid-template-columns: 1fr;
  }
}
</style>
