<script setup lang="ts">
import {
  computed,
  nextTick,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import {
  deleteBinding,
  downloadReport,
  getSchema,
  getTemplate,
  rescanTemplate,
  uploadTemplate,
  upsertBinding,
} from "./api/client";
import type {
  ChartItem,
  DataFieldNode,
  DataSchemaResponse,
  MockItem,
  TemplateResponse,
} from "./api/types";
import DocxUploadPanel from "./components/DocxUploadPanel.vue";
import DocxViewer from "./components/DocxViewer.vue";
import LoadingOverlay from "./components/LoadingOverlay.vue";
import ParseStatusPanel from "./components/ParseStatusPanel.vue";
import SchemaTreeNode from "./components/SchemaTreeNode.vue";
import {
  processDocx,
  type DocxProcessProgress,
  type DocxProcessResult,
} from "./features/docx/processDocx";
import { chartInstanceManager } from "./features/docx/rendering/chartInstanceManager";
import {
  decorateRenderedDocument,
  focusBindingTarget,
  refreshBindingTargetStates,
} from "./features/binding/renderedDocumentBindings";
import {
  decorateRenderedCharts,
  focusChartTarget,
  refreshChartBindingTargetStates,
} from "./features/binding/renderedChartBindings";

type WorkspaceTab = "schema" | "bindings" | "properties";

const SAMPLE_PATH = "/samples/第一部分 科学监测结果.docx";

const template = ref<TemplateResponse | null>(null);
const schema = ref<DataSchemaResponse | null>(null);
const schemaQuery = ref("");
const activeTab = ref<WorkspaceTab>("schema");
const selectedLocatorId = ref<string | null>(null);

const fileName = ref("");
const fileSize = ref("");
const statusMessage = ref("就绪");
const statusIsError = ref(false);
const loading = ref(false);
const loadingMessage = ref("");
const documentVisible = ref(false);
const renderedLocatorCount = ref(0);
const renderedChartCount = ref(0);
const unresolvedLocatorIds = ref<string[]>([]);
const unresolvedChartIds = ref<string[]>([]);
const docxViewerRef = ref<InstanceType<typeof DocxViewer> | null>(null);

const chartStats = ref(createEmptyChartStats());
const selectedItem = computed(
  () =>
    template.value?.mockItems.find(
      (item) => item.locatorId === selectedLocatorId.value
    ) || null
);
const selectedChart = computed(
  () =>
    template.value?.charts.find(
      (chart) => chart.locatorId === selectedLocatorId.value
    ) || null
);
const boundItems = computed(
  () => template.value?.mockItems.filter((item) => item.isBound) || []
);
const boundCharts = computed(
  () => template.value?.charts.filter((chart) => chart.isBound) || []
);
const footerMockCount = computed(
  () =>
    template.value?.mockItems.filter(
      (item) => item.locator.partKind === "Footer"
    ).length || 0
);
const schemaSummary = computed(() => {
  if (!schema.value) return "正在加载字段树…";
  if (schema.value.query) {
    return schema.value.isTruncated
      ? `匹配 ${schema.value.matchCount} 项，显示前 200 项`
      : `匹配 ${schema.value.matchCount} 项`;
  }
  return `共 ${schema.value.totalLeafCount} 个可用叶子字段`;
});
const workspaceTabs: ReadonlyArray<readonly [WorkspaceTab, string]> = [
  ["schema", "数据源"],
  ["bindings", "已绑定"],
  ["properties", "属性"],
];

let renderTaskId = 0;
let searchTimer: ReturnType<typeof setTimeout> | undefined;

onMounted(() => {
  void loadSchema("");
});

watch(schemaQuery, (query) => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => void loadSchema(query), 280);
});

onUnmounted(() => {
  if (searchTimer) clearTimeout(searchTimer);
  cleanupPreview();
});

function createEmptyChartStats() {
  return {
    totalCharts: 0,
    renderedCharts: 0,
    unsupportedCharts: 0,
    failedCharts: 0,
    charts: [] as DocxProcessResult["charts"],
  };
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function handleProgress(progress: DocxProcessProgress): void {
  loadingMessage.value = progress.message;
  if (progress.stage === "failed") {
    setStatus(`预览处理失败：${progress.message}`, true);
  } else if (progress.stage !== "completed") {
    setStatus(progress.message);
  }
}

async function handleFileSelected(file: File): Promise<void> {
  const taskId = ++renderTaskId;
  cleanupPreview();
  resetTemplateState();

  fileName.value = file.name;
  fileSize.value = formatFileSize(file.size);
  documentVisible.value = true;
  loading.value = true;
  loadingMessage.value = "正在上传并解析模板…";
  setStatus("正在上传并解析模板…");

  await nextTick();
  const containers = getViewerContainers();
  if (!containers) {
    setStatus("文档预览容器未就绪。", true);
    loading.value = false;
    return;
  }

  const uploadPromise = uploadTemplate(file);
  const previewPromise = processDocx(file, {
    ...containers,
    onProgress: handleProgress,
  });

  try {
    const [uploadResult, previewResult] = await Promise.allSettled([
      uploadPromise,
      previewPromise,
    ]);
    if (taskId !== renderTaskId) return;

    if (previewResult.status === "fulfilled") {
      chartStats.value = {
        totalCharts: previewResult.value.totalCharts,
        renderedCharts: previewResult.value.renderedCharts,
        unsupportedCharts: previewResult.value.unsupportedCharts,
        failedCharts: previewResult.value.failedCharts,
        charts: previewResult.value.charts,
      };
    }

    if (uploadResult.status === "rejected") {
      throw uploadResult.reason;
    }

    template.value = uploadResult.value;
    const decoration = decorateRenderedDocument(
      containers.documentContainer,
      template.value.mockItems,
      {
        onSelect: selectMockItem,
        onBind: (locatorId, field) => void bindField(locatorId, field),
        onError: (message) => setStatus(message, true),
      }
    );
    renderedLocatorCount.value = decoration.renderedCount;
    unresolvedLocatorIds.value = decoration.unresolvedLocatorIds;
    const chartDecoration = decorateRenderedCharts(
      containers.documentContainer,
      template.value.charts,
      {
        onSelect: selectChart,
        onBind: (locatorId, field) => void bindField(locatorId, field),
        onError: (message) => setStatus(message, true),
      }
    );
    renderedChartCount.value = chartDecoration.renderedCount;
    unresolvedChartIds.value = chartDecoration.unresolvedLocatorIds;

    const unresolvedCount = decoration.unresolvedLocatorIds.length
      + chartDecoration.unresolvedLocatorIds.length;
    const unresolvedNote = unresolvedCount
      ? `，${unresolvedCount} 项需从左侧列表选择`
      : "";
    setStatus(
      `已识别 ${template.value.mockItemCount} 个模拟值和 ${template.value.chartCount} 个图表，网页已定位 ${decoration.renderedCount + chartDecoration.renderedCount} 项${unresolvedNote}`
    );
  } catch (error) {
    if (taskId === renderTaskId) {
      setStatus(errorMessage(error), true);
    }
  } finally {
    if (taskId === renderTaskId) {
      loading.value = false;
    }
  }
}

async function handleLoadSample(): Promise<void> {
  loading.value = true;
  loadingMessage.value = "正在加载示例文档…";
  setStatus("正在加载示例文档…");

  try {
    const response = await fetch(SAMPLE_PATH);
    if (!response.ok) throw new Error("示例文档不存在。");
    const blob = await response.blob();
    const file = new File([blob], "第一部分 科学监测结果.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
    await handleFileSelected(file);
  } catch (error) {
    setStatus(`加载示例文档失败：${errorMessage(error)}`, true);
    loading.value = false;
  }
}

async function handleRescan(): Promise<void> {
  if (!template.value) return;

  await runAction("正在从后端保存的原始 DOCX 重新扫描…", async () => {
    template.value = await rescanTemplate(template.value!.templateId);
    syncSelection();
    refreshRenderedBindings();
    setStatus("重新扫描完成，原始模板未被修改。");
  });
}

async function handleGenerate(): Promise<void> {
  if (!template.value) return;

  await runAction("正在由后端生成 DOCX 报告…", async () => {
    const generatedName = await downloadReport(template.value!.templateId);
    setStatus(`报告已生成：${generatedName}`);
  });
}

async function bindField(
  locatorId: string,
  field: DataFieldNode
): Promise<void> {
  if (!template.value || !field.isBindable) return;

  const targetChart = template.value.charts.find(
    (chart) => chart.locatorId === locatorId
  );
  if (targetChart && !targetChart.isBindable) {
    setStatus("该图表没有可写的数据系列缓存。", true);
    return;
  }
  if (targetChart && field.type !== "Array") {
    setStatus("图表只能绑定集合字段。", true);
    return;
  }
  if (!targetChart && field.type === "Array") {
    setStatus("集合字段只能绑定图表，不能绑定文本模拟值。", true);
    return;
  }

  await runAction(`正在绑定 ${field.path}…`, async () => {
    await upsertBinding(template.value!.templateId, locatorId, field.path);
    template.value = await getTemplate(template.value!.templateId);
    selectedLocatorId.value = locatorId;
    activeTab.value = "properties";
    refreshRenderedBindings();
    setStatus(`已绑定字段：${field.path}`);
  });
}

async function removeBinding(locatorId: string): Promise<void> {
  if (!template.value) return;

  await runAction("正在取消绑定…", async () => {
    await deleteBinding(template.value!.templateId, locatorId);
    template.value = await getTemplate(template.value!.templateId);
    syncSelection();
    refreshRenderedBindings();
    setStatus("绑定已取消。");
  });
}

function handleFieldSelected(field: DataFieldNode): void {
  if (!selectedLocatorId.value) {
    setStatus("请先点击文档中的模拟值或图表，或从左侧导航中选择一项。");
    return;
  }
  void bindField(selectedLocatorId.value, field);
}

function selectMockItem(item: MockItem): void {
  selectedLocatorId.value = item.locatorId;
  activeTab.value = "properties";
}

function selectChart(chart: ChartItem): void {
  selectedLocatorId.value = chart.locatorId;
  activeTab.value = "properties";
}

function focusMockItem(item: MockItem): void {
  selectMockItem(item);
  const container = getViewerContainers()?.documentContainer;
  if (!container || !focusBindingTarget(container, item.locatorId)) {
    setStatus("该值未能映射到网页预览，可继续在右侧选择字段完成绑定。");
  }
}

function focusChart(chart: ChartItem): void {
  selectChart(chart);
  const container = getViewerContainers()?.documentContainer;
  if (!container || !focusChartTarget(container, chart.locatorId)) {
    setStatus("该图表未能映射到网页预览，可继续在右侧选择集合字段完成绑定。");
  }
}

function handleClear(): void {
  renderTaskId += 1;
  cleanupPreview();
  resetTemplateState();
  fileName.value = "";
  fileSize.value = "";
  documentVisible.value = false;
  setStatus("就绪");
}

function resetTemplateState(): void {
  template.value = null;
  selectedLocatorId.value = null;
  activeTab.value = "schema";
  chartStats.value = createEmptyChartStats();
  renderedLocatorCount.value = 0;
  renderedChartCount.value = 0;
  unresolvedLocatorIds.value = [];
  unresolvedChartIds.value = [];
}

function cleanupPreview(): void {
  chartInstanceManager.disposeAll();
  const containers = getViewerContainers();
  if (!containers) return;
  containers.documentContainer.replaceChildren();
  containers.styleContainer.replaceChildren();
}

function getViewerContainers(): {
  documentContainer: HTMLElement;
  styleContainer: HTMLElement;
} | null {
  const viewer = docxViewerRef.value;
  const documentContainer = viewer?.getDocumentContainer();
  const styleContainer = viewer?.getStyleContainer();
  return documentContainer && styleContainer
    ? { documentContainer, styleContainer }
    : null;
}

function refreshRenderedBindings(): void {
  const container = getViewerContainers()?.documentContainer;
  if (container && template.value) {
    refreshBindingTargetStates(container, template.value.mockItems);
    refreshChartBindingTargetStates(container, template.value.charts);
  }
}

function syncSelection(): void {
  if (!selectedItem.value && !selectedChart.value) selectedLocatorId.value = null;
}

async function loadSchema(query: string): Promise<void> {
  try {
    schema.value = await getSchema(query);
  } catch (error) {
    setStatus(errorMessage(error), true);
  }
}

async function runAction(
  message: string,
  action: () => Promise<void>
): Promise<void> {
  loading.value = true;
  loadingMessage.value = message;
  setStatus(message);
  try {
    await action();
  } catch (error) {
    setStatus(errorMessage(error), true);
  } finally {
    loading.value = false;
  }
}

function setStatus(message: string, isError = false): void {
  statusMessage.value = message;
  statusIsError.value = isError;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function partLabel(item: MockItem): string {
  return item.locator.partKind === "Footer" ? "页脚" : "正文";
}
</script>

<template>
  <div class="app-shell">
    <DocxUploadPanel
      :file-name="fileName"
      :file-size="fileSize"
      :status-message="statusMessage"
      :loading="loading"
      :has-template="Boolean(template)"
      :binding-count="template?.bindingCount || 0"
      @file-selected="handleFileSelected"
      @load-sample="handleLoadSample"
      @clear="handleClear"
      @rescan="handleRescan"
      @generate="handleGenerate"
    />

    <div class="workspace">
      <aside class="workspace-panel left-panel">
        <section class="panel-section">
          <h2>模板状态</h2>
          <dl class="metadata">
            <div><dt>模拟值</dt><dd>{{ template?.mockItemCount || 0 }}</dd></div>
            <div><dt>原生图表</dt><dd>{{ template?.chartCount || 0 }}</dd></div>
            <div><dt>已绑定</dt><dd>{{ template?.bindingCount || 0 }}</dd></div>
            <div><dt>网页定位</dt><dd>{{ renderedLocatorCount }}</dd></div>
            <div><dt>图表定位</dt><dd>{{ renderedChartCount }}</dd></div>
            <div><dt>页脚模拟值</dt><dd>{{ footerMockCount }}</dd></div>
            <div>
              <dt>内容哈希</dt>
              <dd :title="template?.contentHash || ''">
                {{ template?.contentHash.slice(0, 12) || "—" }}
              </dd>
            </div>
          </dl>
        </section>

        <section class="panel-section mock-list-section">
          <h2>图表导航</h2>
          <p v-if="!template" class="empty-state">上传模板后显示可绑定图表</p>
          <p v-else-if="template.charts.length === 0" class="empty-state">未识别到 Word 原生图表</p>
          <div v-else class="mock-list chart-target-list">
            <button
              v-for="chart in template.charts"
              :key="chart.locatorId"
              type="button"
              class="mock-list-item chart-list-item"
              :class="{
                'is-bound': chart.isBound,
                'is-selected': chart.locatorId === selectedLocatorId,
                'is-unresolved': unresolvedChartIds.includes(chart.locatorId),
              }"
              @click="focusChart(chart)"
            >
              <strong>{{ chart.title }}</strong>
              <span>{{ chart.boundDataPath || (chart.isBindable ? `${chart.chartType} · ${chart.series.length} 个系列` : `${chart.chartType} · 不可绑定`) }}</span>
            </button>
          </div>
        </section>

        <section class="panel-section mock-list-section">
          <h2>模拟值导航</h2>
          <p v-if="!template" class="empty-state">上传模板后显示可绑定值</p>
          <div v-else class="mock-list">
            <button
              v-for="item in template.mockItems"
              :key="item.locatorId"
              type="button"
              class="mock-list-item"
              :class="{
                'is-bound': item.isBound,
                'is-footer': item.locator.partKind === 'Footer',
                'is-selected': item.locatorId === selectedLocatorId,
                'is-unresolved': unresolvedLocatorIds.includes(item.locatorId),
              }"
              @click="focusMockItem(item)"
            >
              <strong>
                {{ item.mockValue }}
                <em v-if="item.locator.partKind === 'Footer'">页脚</em>
              </strong>
              <span>
                {{ item.boundDataPath || `${partLabel(item)} · 段落 ${item.locator.paragraphIndex + 1}` }}
              </span>
            </button>
          </div>
        </section>

        <ParseStatusPanel
          :total-charts="chartStats.totalCharts"
          :rendered-charts="chartStats.renderedCharts"
          :unsupported-charts="chartStats.unsupportedCharts"
          :failed-charts="chartStats.failedCharts"
          :charts="chartStats.charts"
        />
      </aside>

      <main class="preview-column">
        <div class="preview-notice" :class="{ 'is-error': statusIsError }">
          <strong>{{ statusMessage }}</strong>
          <span>网页效果仅用于定位与绑定；批量赋值和最终文件生成均由后端 C# 处理。</span>
          <span class="preview-legend">
            <i class="legend-body"></i>正文模拟值
            <i class="legend-footer"></i>页脚模拟值/区域
            <i class="legend-chart"></i>可绑定图表
          </span>
        </div>
        <DocxViewer ref="docxViewerRef" :visible="documentVisible" />
      </main>

      <aside class="workspace-panel right-panel">
        <div class="tabs" role="tablist">
          <button
            v-for="tab in workspaceTabs"
            :key="tab[0]"
            type="button"
            class="tab"
            :class="{ active: activeTab === tab[0] }"
            @click="activeTab = tab[0]"
          >
            {{ tab[0] === "bindings" ? `${tab[1]} ${template?.bindingCount || 0}` : tab[1] }}
          </button>
        </div>

        <section v-if="activeTab === 'schema'" class="tab-panel">
          <label class="search-box">
            <span class="visually-hidden">搜索数据字段</span>
            <input v-model="schemaQuery" type="search" placeholder="搜索字段名称或路径" />
          </label>
          <p class="small-note">{{ schemaSummary }}</p>
          <p class="binding-hint">
            标量字段绑定黄色/紫色文本；集合字段绑定橙色图表区域。也可以直接拖拽字段。
          </p>
          <div class="schema-tree">
            <SchemaTreeNode
              v-for="node in schema?.nodes || []"
              :key="node.path"
              :node="node"
              @field-selected="handleFieldSelected"
            />
          </div>
        </section>

        <section v-else-if="activeTab === 'bindings'" class="tab-panel">
          <p v-if="boundItems.length === 0 && boundCharts.length === 0" class="empty-state">尚无绑定关系</p>
          <div v-else class="binding-list">
            <article v-for="item in boundItems" :key="item.locatorId" class="binding-card">
              <button type="button" class="binding-main" @click="focusMockItem(item)">
                <strong>
                  模拟值：{{ item.mockValue }}
                  <em v-if="item.locator.partKind === 'Footer'">页脚</em>
                </strong>
                <span>{{ item.boundDataPath }}</span>
              </button>
              <button type="button" class="binding-remove" @click="removeBinding(item.locatorId)">
                取消绑定
              </button>
            </article>
            <article v-for="chart in boundCharts" :key="chart.locatorId" class="binding-card chart-binding-card">
              <button type="button" class="binding-main" @click="focusChart(chart)">
                <strong>图表：{{ chart.title }} <em>图表</em></strong>
                <span>{{ chart.boundDataPath }}</span>
              </button>
              <button type="button" class="binding-remove" @click="removeBinding(chart.locatorId)">
                取消绑定
              </button>
            </article>
          </div>
        </section>

        <section v-else class="tab-panel properties-panel">
          <p v-if="!selectedItem && !selectedChart" class="empty-state">
            点击文档高亮、图表或左侧导航查看属性
          </p>
          <dl v-else-if="selectedItem">
            <div><dt>原始值</dt><dd>{{ selectedItem.mockValue }}</dd></div>
            <div><dt>模拟数据类型</dt><dd>{{ selectedItem.dataType }}</dd></div>
            <div><dt>文档位置</dt><dd>{{ partLabel(selectedItem) }}</dd></div>
            <div><dt>文档部件</dt><dd>{{ selectedItem.locator.partKey }}</dd></div>
            <div><dt>段落索引</dt><dd>{{ selectedItem.locator.paragraphIndex }}</dd></div>
            <div><dt>起始偏移</dt><dd>{{ selectedItem.locator.startOffset }}</dd></div>
            <div><dt>已绑定字段</dt><dd>{{ selectedItem.boundDataPath || "未绑定" }}</dd></div>
            <div><dt>字段类型</dt><dd>{{ selectedItem.boundDataType || "—" }}</dd></div>
            <div><dt>LocatorId</dt><dd>{{ selectedItem.locatorId }}</dd></div>
          </dl>
          <dl v-else-if="selectedChart">
            <div><dt>目标类型</dt><dd>Word 原生图表</dd></div>
            <div><dt>图表标题</dt><dd>{{ selectedChart.title }}</dd></div>
            <div><dt>图表类型</dt><dd>{{ selectedChart.chartType }}</dd></div>
            <div><dt>分类数量</dt><dd>{{ selectedChart.categories.length }}</dd></div>
            <div><dt>系列</dt><dd>{{ selectedChart.series.map(series => series.name).join('、') }}</dd></div>
            <div><dt>文档部件</dt><dd>{{ selectedChart.locator.partKey }}</dd></div>
            <div><dt>已绑定集合</dt><dd>{{ selectedChart.boundDataPath || "未绑定" }}</dd></div>
            <div><dt>LocatorId</dt><dd>{{ selectedChart.locatorId }}</dd></div>
          </dl>
        </section>
      </aside>
    </div>

    <LoadingOverlay :visible="loading" :message="loadingMessage" />
  </div>
</template>
