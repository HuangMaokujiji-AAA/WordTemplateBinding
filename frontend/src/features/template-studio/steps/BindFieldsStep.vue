<script setup lang="ts">
import {
  computed,
  nextTick,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import { useRoute } from "vue-router";
import {
  deletePersistentBinding,
  downloadBindingSetReport,
  downloadBindingSetReusableTemplate,
  getCurrentTemplateVersion,
  getTemplateVersion,
  getBindingPreview,
  getOrCreateBindingSet,
  getPersistentSchema,
  getTemplateSegmentPreview,
  getTemplateSegmentOutline,
  hydrateTemplateResponse,
  listBindingItems,
  listChapters,
  listDataSources,
  listPersistentTemplates,
  listTemplateSegmentElements,
  listTemplateSegments,
  insertTemplateSegmentBoundary,
  listProjects,
  refreshDataSource,
  removeTemplateSegmentBoundary,
  rescanTemplateVersion,
  uploadPersistentTemplate,
  upsertPersistentBinding,
  validateBindingSet,
} from "../../../api/client";
import type {
  ChapterRecord,
  ChartItem,
  DataSourceRecord,
  DataFieldNode,
  DataSchemaResponse,
  MockItem,
  ProjectRecord,
  TableBindingMapping,
  TableItem,
  TemplateRecord,
  TemplateResponse,
  TemplateSegmentRecord,
  TemplateVersionView,
} from "../../../api/types";
import DocxUploadPanel from "../../../components/DocxUploadPanel.vue";
import DocxViewer from "../../../components/DocxViewer.vue";
import LoadingOverlay from "../../../components/LoadingOverlay.vue";
import ParseStatusPanel from "../../../components/ParseStatusPanel.vue";
import SchemaTreeNode from "../../../components/SchemaTreeNode.vue";
import ChartStructurePanel from "../../../components/ChartStructurePanel.vue";
import TableBindingPanel from "../../../components/TableBindingPanel.vue";
import {
  processDocx,
  type DocxProcessProgress,
  type DocxProcessResult,
} from "../../docx/processDocx";
import type { ParsedWordChart } from "../../docx/chart-analysis/models/types";
import { chartInstanceManager } from "../../docx/rendering/chartInstanceManager";
import {
  decorateRenderedDocument,
  focusBindingTarget,
  refreshBindingTargetStates,
} from "../../binding/renderedDocumentBindings";
import {
  decorateRenderedCharts,
  focusChartTarget,
  refreshChartBindingTargetStates,
} from "../../binding/renderedChartBindings";
import {
  decorateRenderedTables,
  focusTableTarget,
  refreshTableBindingTargetStates,
} from "../../binding/renderedTableBindings";
import { formatImportSummary } from "../../binding/importSummaryStatus";
import { buildChartWorkspace, type ChartWorkspaceItem } from "../../binding/chartWorkspace";
import {
  useBindingEditor,
  workspaceTabs,
} from "../../../composables/useBindingEditor";
import { useSegmentEditor } from "../../../composables/useSegmentEditor";

const SAMPLE_PATH = "/samples/第一部分 科学监测结果.docx";
const route = useRoute();
const emit = defineEmits<{ complete: [] }>();

const template = ref<TemplateResponse | null>(null);
const versionView = ref<TemplateVersionView | null>(null);
const schema = ref<DataSchemaResponse | null>(null);
const projects = ref<ProjectRecord[]>([]);
const chapters = ref<ChapterRecord[]>([]);
const dataSources = ref<DataSourceRecord[]>([]);
const templateCatalog = ref<TemplateRecord[]>([]);
const selectedProjectId = ref("");
const selectedChapterId = ref("");
const selectedDataSourceId = ref("");
const selectedTemplateId = ref("");
const schemaQuery = ref("");
const {
  segments,
  selectedSegmentId,
  segmentElements,
  segmentOutline,
  boundaryManagerVisible,
  boundaryKey,
  boundaryName,
  boundaryStartBlockId,
  boundaryEndBlockId,
  selectableOutlineBlocks,
  boundaryEndBlocks,
  resetSegmentEditor,
  segmentIndent,
  outlineLabel,
} = useSegmentEditor();
const {
  bindingSetId,
  bindingPreview,
  activeTab,
  selectedLocatorId,
  selectedItem,
  selectedChart,
  selectedTable,
  boundItems,
  boundCharts,
  boundTables,
  syncSelection,
  resetBindingEditor,
} = useBindingEditor(template);

const fileName = ref("");
const fileSize = ref("");
const statusMessage = ref("就绪");
const statusIsError = ref(false);
const loading = ref(false);
const loadingMessage = ref("");
const documentVisible = ref(false);
const renderedLocatorCount = ref(0);
const renderedChartCount = ref(0);
const renderedTableCount = ref(0);
const unresolvedLocatorIds = ref<string[]>([]);
const unresolvedChartIds = ref<string[]>([]);
const unresolvedTableIds = ref<string[]>([]);
const pendingTableField = ref<DataFieldNode | null>(null);
const docxViewerRef = ref<InstanceType<typeof DocxViewer> | null>(null);
const previewCanvas = ref<HTMLElement | null>(null);
const previewSurfaceWidth = ref(0);
const zoomPercent = ref(100);
const zoomOptions = Array.from({ length: 11 }, (_, index) => 50 + index * 10);

const chartStats = ref(createEmptyChartStats());
const parsedCharts = ref<ParsedWordChart[]>([]);
const chartWorkspace = computed<ChartWorkspaceItem[]>(() =>
  buildChartWorkspace(parsedCharts.value, template.value?.charts ?? [])
);
const selectedChartWorkspaceItem = computed<ChartWorkspaceItem | null>(
  () =>
    chartWorkspace.value.find(
      (item) => item.locatorId === selectedLocatorId.value
    ) || null
);
const selectedTableDataPath = computed(
  () => pendingTableField.value?.path || selectedTable.value?.boundDataPath || ""
);
const selectedTableFieldOptions = computed(() => {
  const dataPath = selectedTableDataPath.value;
  if (!dataPath) return [];
  const field = pendingTableField.value?.path === dataPath
    ? pendingTableField.value
    : findSchemaNode(schema.value?.nodes || [], dataPath);
  if (!field) return [];
  const prefix = `${field.path}[]`;
  return flattenSchemaNodes(field.children)
    .filter((node) => node.isLeaf)
    .map((node) => ({
      label: `${node.name} · ${node.type}`,
      value: node.path.startsWith(`${prefix}.`)
        ? node.path.slice(prefix.length + 1)
        : node.path.startsWith(`${field.path}.`)
          ? node.path.slice(field.path.length + 1)
        : node.path.split(".").at(-1) || node.name,
    }));
});
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
let renderTaskId = 0;
let searchTimer: ReturnType<typeof setTimeout> | undefined;
let bootstrappingContext = true;

function flattenSchemaNodes(nodes: DataFieldNode[]): DataFieldNode[] {
  return nodes.flatMap((node) => [node, ...flattenSchemaNodes(node.children)]);
}

function findSchemaNode(nodes: DataFieldNode[], path: string): DataFieldNode | null {
  for (const node of nodes) {
    if (node.path === path) return node;
    const nested = findSchemaNode(node.children, path);
    if (nested) return nested;
  }
  return null;
}

function setZoom(value: number): void {
  zoomPercent.value = Math.min(150, Math.max(50, value));
}

function zoomOut(): void {
  setZoom(zoomPercent.value - 10);
}

function zoomIn(): void {
  setZoom(zoomPercent.value + 10);
}

function naturalPageWidth(): number {
  const container = docxViewerRef.value?.getDocumentContainer();
  if (!container) return 0;
  return Math.max(
    0,
    ...[...container.querySelectorAll<HTMLElement>("section.docx")]
      .map((page) => page.offsetWidth)
  );
}

function syncPreviewSurfaceWidth(): void {
  const canvas = previewCanvas.value;
  const pageWidth = naturalPageWidth();
  if (!canvas || pageWidth <= 0) {
    previewSurfaceWidth.value = 0;
    return;
  }

  const scale = zoomPercent.value / 100;
  const availableWidth = Math.max(1, canvas.clientWidth - 48);
  // The extra 24px accounts for DocxViewer's horizontal page gutter.
  previewSurfaceWidth.value = Math.ceil(
    Math.max(pageWidth + 24, availableWidth / scale)
  );
}

async function centerPreview(): Promise<void> {
  await nextTick();
  const canvas = previewCanvas.value;
  if (!canvas) return;
  canvas.scrollLeft = Math.max(0, (canvas.scrollWidth - canvas.clientWidth) / 2);
}

function fitWidth(allowEnlarge = true): void {
  const canvas = previewCanvas.value;
  const pageWidth = naturalPageWidth();
  if (!canvas || pageWidth <= 0 || canvas.clientWidth <= 72) return;

  const availableWidth = Math.max(1, canvas.clientWidth - 72);
  const fittedPercent =
    Math.floor((availableWidth / pageWidth) * 100 / 10) * 10;
  setZoom(allowEnlarge ? fittedPercent : Math.min(100, fittedPercent));
  syncPreviewSurfaceWidth();
  void centerPreview();
}

onMounted(() => {
  void loadWorkspaceContext();
});

watch(schemaQuery, (query) => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => void loadSchema(query), 280);
});

watch(selectedProjectId, () => {
  if (bootstrappingContext) return;
  void loadProjectContext();
});

watch(selectedDataSourceId, () => {
  if (bootstrappingContext) return;
  void loadSchema(schemaQuery.value);
});

watch(zoomPercent, () => {
  syncPreviewSurfaceWidth();
  void centerPreview();
});

onUnmounted(() => {
  if (searchTimer) clearTimeout(searchTimer);
  cleanupPreview();
});

function createEmptyChartStats() {
  return {
    totalCharts: 0,
    renderedCharts: 0,
    partiallyRenderedCharts: 0,
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
  ++renderTaskId;
  cleanupPreview();
  resetTemplateState();

  fileName.value = file.name;
  fileSize.value = formatFileSize(file.size);
  documentVisible.value = true;
  loading.value = true;
  loadingMessage.value = "正在上传并解析模板…";
  setStatus("正在上传并解析模板…");

  try {
    const uploadResult = await uploadPersistentTemplate(
      file,
      selectedTemplateId.value || null
    );
    versionView.value = uploadResult;
    selectedTemplateId.value = uploadResult.template.id;
    await refreshTemplateCatalog();
    await loadSegmentsAndSelect();
  } catch (error) {
    setStatus(errorMessage(error), true);
  } finally {
    loading.value = false;
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
  if (!versionView.value) return;

  await runAction("正在从后端保存的原始 DOCX 重新扫描…", async () => {
    versionView.value = await rescanTemplateVersion(versionView.value!.version.id);
    await loadSegmentsAndSelect();
    syncSelection();
    refreshRenderedBindings();
    const importSummary = template.value?.importSummary;
    setStatus(
      `重新扫描完成，原始模板未被修改${importSummary ? formatImportSummary(importSummary) : ""}。`
    );
  });
}

async function handleGenerate(): Promise<void> {
  if (!bindingSetId.value) {
    setStatus("请先选择项目、章节并建立绑定配置。", true);
    return;
  }

  await runAction("正在由后端生成 DOCX 报告…", async () => {
    const generatedName = await downloadBindingSetReport(bindingSetId.value);
    setStatus(`报告已生成：${generatedName}`);
  });
}

async function handleTestReport(payload: Record<string, unknown>[]): Promise<void> {
  if (!bindingSetId.value) return;
  const chartItem = selectedChartWorkspaceItem.value;
  if (!chartItem?.isBound || !chartItem.boundDataPath) {
    setStatus("请先将一个 Array 集合字段绑定到当前图表。", true);
    return;
  }

  const values: Record<string, unknown> = {
    [chartItem.boundDataPath]: payload,
  };

  await runAction("正在使用测试数据生成 DOCX 报告…", async () => {
    void values;
    const generatedName = await downloadBindingSetReport(bindingSetId.value);
    setStatus(`测试报告已生成：${generatedName}`);
  });
}

async function handleSaveMapping(mappingPayload: {
  locatorId: string;
  dataPath: string;
  mapping: {
    mode: string;
    categoryField: string;
    seriesMappings: Array<{
      seriesIndex: number;
      seriesKey: string;
      valueField: string;
      seriesNameField?: string | null;
    }>;
  };
}): Promise<void> {
  if (!template.value || !bindingSetId.value || !selectedDataSourceId.value) return;

  await runAction("正在保存图表字段映射…", async () => {
    const chart = template.value!.charts.find(
      (item) => item.locatorId === mappingPayload.locatorId
    );
    if (!chart?.templateElementId) throw new Error("图表缺少持久化模板元素 ID。");
    await upsertPersistentBinding(
      bindingSetId.value,
      chart.templateElementId,
      selectedDataSourceId.value,
      mappingPayload.dataPath,
      JSON.stringify({ chartMapping: mappingPayload.mapping })
    );
    await refreshTemplateBindings();
    setStatus("图表字段映射已保存。");
  });
}

async function handleSaveTableMapping(payload: {
  table: TableItem;
  dataPath: string;
  mapping: TableBindingMapping;
}): Promise<void> {
  if (!bindingSetId.value || !selectedDataSourceId.value) return;
  await runAction("正在保存表格绑定与列映射…", async () => {
    await upsertPersistentBinding(
      bindingSetId.value,
      payload.table.templateElementId,
      selectedDataSourceId.value,
      payload.dataPath,
      JSON.stringify({ tableMapping: payload.mapping })
    );
    pendingTableField.value = null;
    await refreshTemplateBindings();
    selectedLocatorId.value = payload.table.locatorId;
    activeTab.value = "table-structure";
    refreshRenderedBindings();
    setStatus(`表格已绑定：${payload.dataPath}，列映射已保存。`);
  });
}

async function handleExportReusable(): Promise<void> {
  if (!bindingSetId.value) return;

  await runAction("正在生成可复用绑定模板…", async () => {
    const exportedName = await downloadBindingSetReusableTemplate(bindingSetId.value);
    setStatus(`复用模板已导出：${exportedName}`);
  });
}

async function bindField(
  locatorId: string,
  field: DataFieldNode
): Promise<void> {
  if (
    !template.value ||
    !field.isBindable ||
    !bindingSetId.value ||
    !selectedDataSourceId.value
  ) {
    setStatus("请先选择项目、章节和数据源。", true);
    return;
  }

  const targetChart = template.value.charts.find(
    (chart) => chart.locatorId === locatorId
  );
  const targetItem = template.value.mockItems.find(
    (item) => item.locatorId === locatorId
  );
  const targetTable = template.value.tables.find(
    (table) => table.locatorId === locatorId
  );
  if (targetTable) {
    if (!targetTable.isBindable) {
      setStatus(targetTable.parseMessage || "该表格当前不可绑定。", true);
      return;
    }
    if (field.type !== "Array") {
      setStatus("表格只能绑定集合字段。", true);
      return;
    }
    pendingTableField.value = field;
    activeTab.value = "table-structure";
    setStatus(`已选择集合字段 ${field.path}，请确认表格列映射后保存。`);
    return;
  }
  if (targetChart && !targetChart.isBindable) {
    setStatus("该图表没有可写的数据系列缓存。", true);
    return;
  }
  if (targetChart && field.type !== "Array") {
    setStatus("图表只能绑定集合字段。", true);
    return;
  }
  if (!targetChart && field.type === "Array") {
    setStatus("集合字段只能绑定图表或表格，不能绑定文本模拟值。", true);
    return;
  }

  const previousDataPath =
    targetChart?.boundDataPath || targetItem?.boundDataPath || null;
  const isRebinding =
    previousDataPath !== null && previousDataPath !== field.path;
  const progressMessage = isRebinding
    ? `正在将绑定从 ${previousDataPath} 修改为 ${field.path}…`
    : `正在绑定 ${field.path}…`;

  await runAction(progressMessage, async () => {
    const templateElementId =
      targetChart?.templateElementId || targetItem?.templateElementId;
    if (!templateElementId) throw new Error("目标缺少持久化模板元素 ID。");
    await upsertPersistentBinding(
      bindingSetId.value,
      templateElementId,
      selectedDataSourceId.value,
      field.path
    );
    await refreshTemplateBindings();
    selectedLocatorId.value = locatorId;
    activeTab.value = "properties";
    refreshRenderedBindings();
    setStatus(
      isRebinding
        ? `已将绑定从 ${previousDataPath} 修改为 ${field.path}`
        : `已绑定字段：${field.path}`
    );
  });
}

async function removeBinding(locatorId: string): Promise<void> {
  if (!template.value || !bindingSetId.value) return;

  await runAction("正在取消绑定…", async () => {
    const target = [
      ...template.value!.mockItems,
      ...template.value!.charts,
      ...template.value!.tables,
    ].find((item) => item.locatorId === locatorId);
    if (!target?.templateElementId) throw new Error("目标缺少持久化模板元素 ID。");
    await deletePersistentBinding(bindingSetId.value, target.templateElementId);
    await refreshTemplateBindings();
    syncSelection();
    refreshRenderedBindings();
    setStatus("绑定已取消。");
  });
}

function handleFieldSelected(field: DataFieldNode): void {
  if (!selectedLocatorId.value) {
    setStatus("请先点击文档中的模拟值、图表或表格，或从左侧导航中选择一项。");
    return;
  }
  void bindField(selectedLocatorId.value, field);
}

function selectMockItem(item: MockItem): void {
  selectedLocatorId.value = item.locatorId;
  activeTab.value = "properties";
  void loadBindingPreview(item.templateElementId);
}

function selectChart(chart: ChartItem): void {
  selectedLocatorId.value = chart.locatorId;
  activeTab.value = "properties";
  void loadBindingPreview(chart.templateElementId);
}

function selectTable(table: TableItem): void {
  if (selectedLocatorId.value !== table.locatorId) {
    pendingTableField.value = null;
  }
  selectedLocatorId.value = table.locatorId;
  activeTab.value = table.isBound ? "table-structure" : "properties";
  void loadBindingPreview(table.templateElementId);
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

function focusTable(table: TableItem): void {
  selectTable(table);
  const container = getViewerContainers()?.documentContainer;
  if (!container || !focusTableTarget(container, table.locatorId)) {
    setStatus("该表格未能映射到网页预览，可继续选择集合字段并配置列映射。", true);
  }
}

function handleClear(): void {
  renderTaskId += 1;
  cleanupPreview();
  resetTemplateState();
  fileName.value = "";
  fileSize.value = "";
  documentVisible.value = false;
  selectedTemplateId.value = "";
  const workspaceUrl = new URL(window.location.href);
  workspaceUrl.searchParams.delete("segmentId");
  window.history.replaceState(null, "", workspaceUrl);
  setStatus("就绪");
}

function resetTemplateState(): void {
  template.value = null;
  versionView.value = null;
  resetBindingEditor();
  chartStats.value = createEmptyChartStats();
  parsedCharts.value = [];
  renderedLocatorCount.value = 0;
  renderedChartCount.value = 0;
  renderedTableCount.value = 0;
  unresolvedLocatorIds.value = [];
  unresolvedChartIds.value = [];
  unresolvedTableIds.value = [];
  pendingTableField.value = null;
  resetSegmentEditor();
}

function cleanupPreview(): void {
  chartInstanceManager.disposeAll();
  previewSurfaceWidth.value = 0;
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
    refreshTableBindingTargetStates(container, template.value.tables);
  }
}

async function loadSchema(query: string): Promise<void> {
  if (!selectedDataSourceId.value) {
    schema.value = null;
    return;
  }
  try {
    schema.value = await getPersistentSchema(selectedDataSourceId.value, query);
  } catch (error) {
    setStatus(errorMessage(error), true);
  }
}

async function loadWorkspaceContext(): Promise<void> {
  try {
    await refreshTemplateCatalog();
    const projectResult = await listProjects({ pageSize: 100 });
    projects.value = projectResult.items;
    const requestedProjectId = queryValue("projectId");
    selectedProjectId.value = projectResult.items.some(
      item => item.projectId === requestedProjectId
    )
      ? requestedProjectId
      : projectResult.items[0]?.projectId || "";
    await loadProjectContext();

    const requestedTemplateId = queryValue("templateId");
    if (
      requestedTemplateId &&
      templateCatalog.value.some(item => item.id === requestedTemplateId)
    ) {
      selectedTemplateId.value = requestedTemplateId;
      await handleTemplateSelected();
    }
  } catch (error) {
    setStatus(errorMessage(error), true);
  } finally {
    bootstrappingContext = false;
  }
}

async function refreshTemplateCatalog(): Promise<void> {
  const result = await listPersistentTemplates({ pageSize: 100 });
  templateCatalog.value = result.items;
}

async function loadProjectContext(): Promise<void> {
  chapters.value = [];
  dataSources.value = [];
  selectedChapterId.value = "";
  selectedDataSourceId.value = "";
  if (!selectedProjectId.value) return;
  try {
    const [chapterItems, sourceItems] = await Promise.all([
      listChapters(selectedProjectId.value),
      listDataSources(selectedProjectId.value),
    ]);
    chapters.value = chapterItems;
    dataSources.value = sourceItems;
    const requestedChapterId = queryValue("chapterId");
    const requestedDataSourceId = queryValue("dataSourceId");
    selectedChapterId.value = chapterItems.some(
      item => item.id === requestedChapterId
    )
      ? requestedChapterId
      : chapterItems[0]?.id || "";
    selectedDataSourceId.value = sourceItems.some(
      item => item.id === requestedDataSourceId
    )
      ? requestedDataSourceId
      : sourceItems[0]?.id || "";
    await loadSchema(schemaQuery.value);
    if (versionView.value) await refreshBindingContext();
  } catch (error) {
    setStatus(errorMessage(error), true);
  }
}

async function handleTemplateSelected(): Promise<void> {
  if (!selectedTemplateId.value) return;
  const taskId = ++renderTaskId;
  cleanupPreview();
  resetTemplateState();
  loading.value = true;
  loadingMessage.value = "正在从数据库加载模板版本…";
  documentVisible.value = true;
  try {
    const requestedVersionId = queryValue("versionId");
    const view = requestedVersionId
      ? await getTemplateVersion(requestedVersionId)
      : await getCurrentTemplateVersion(selectedTemplateId.value);
    if (view.template.id !== selectedTemplateId.value) {
      throw new Error("指定模板版本不属于当前模板。");
    }
    versionView.value = view;
    if (taskId !== renderTaskId) return;
    await loadSegmentsAndSelect();
  } catch (error) {
    setStatus(errorMessage(error), true);
  } finally {
    loading.value = false;
  }
}

async function loadSegmentsAndSelect(): Promise<void> {
  if (!versionView.value) return;
  await refreshBindingContext();
  const response = await listTemplateSegments(
    versionView.value.version.id,
    bindingSetId.value || undefined
  );
  segments.value = response.items;
  const requestedSegmentId = queryValue("segmentId");
  selectedSegmentId.value = response.items.some(
    item => item.id === requestedSegmentId
  )
    ? requestedSegmentId!
    : response.items[0]?.id || "";
  if (!selectedSegmentId.value) {
    throw new Error("模板没有可用片段，请检查片段解析诊断。");
  }
  await loadSelectedSegment();
}

function queryValue(key: string): string {
  const value = route.query[key];
  return typeof value === "string" ? value : "";
}

async function toggleBoundaryManager(): Promise<void> {
  boundaryManagerVisible.value = !boundaryManagerVisible.value;
  if (boundaryManagerVisible.value && !segmentOutline.value) {
    await loadBoundaryOutline();
  }
}

async function loadBoundaryOutline(): Promise<void> {
  if (!versionView.value) return;
  segmentOutline.value = await getTemplateSegmentOutline(
    versionView.value.version.id
  );
  const selectable = selectableOutlineBlocks.value;
  boundaryStartBlockId.value = selectable[0]?.blockId || "";
  boundaryEndBlockId.value = selectable[0]?.blockId || "";
}

async function handleInsertBoundary(): Promise<void> {
  if (
    !versionView.value ||
    !segmentOutline.value ||
    !boundaryKey.value.trim() ||
    !boundaryName.value.trim() ||
    !boundaryStartBlockId.value ||
    !boundaryEndBlockId.value
  ) {
    setStatus("请填写片段名称、片段键并选择起止块。", true);
    return;
  }

  await runAction("正在创建包含新边界的模板版本…", async () => {
    const edited = await insertTemplateSegmentBoundary(
      versionView.value!.version.id,
      {
        segmentKey: boundaryKey.value.trim(),
        segmentName: boundaryName.value.trim(),
        startBlockId: boundaryStartBlockId.value,
        endBlockId: boundaryEndBlockId.value,
        expectedContentHash: segmentOutline.value!.contentHash,
      }
    );
    versionView.value = edited;
    boundaryKey.value = "";
    boundaryName.value = "";
    segmentOutline.value = null;
    await refreshTemplateCatalog();
    await loadSegmentsAndSelect();
    await loadBoundaryOutline();
    setStatus(`拆分边界已写入新模板版本 v${edited.version.versionNo}。`);
  });
}

async function handleRemoveBoundary(segment: TemplateSegmentRecord): Promise<void> {
  if (!versionView.value || segment.anchorType !== "CONTENT_CONTROL") return;
  if (!window.confirm(
    `确定取消“${segment.segmentName}”的拆分边界吗？正文内容会完整保留，并创建新模板版本。`
  )) return;

  await runAction("正在创建取消边界后的模板版本…", async () => {
    if (!segmentOutline.value) await loadBoundaryOutline();
    const edited = await removeTemplateSegmentBoundary(
      versionView.value!.version.id,
      segment.segmentKey,
      segmentOutline.value!.contentHash
    );
    versionView.value = edited;
    segmentOutline.value = null;
    await refreshTemplateCatalog();
    await loadSegmentsAndSelect();
    await loadBoundaryOutline();
    setStatus(`边界已取消，正文已保留在新模板版本 v${edited.version.versionNo}。`);
  });
}

async function loadSelectedSegment(): Promise<void> {
  if (!versionView.value || !selectedSegmentId.value) return;
  const workspaceUrl = new URL(window.location.href);
  workspaceUrl.searchParams.set("segmentId", selectedSegmentId.value);
  window.history.replaceState(null, "", workspaceUrl);
  const taskId = ++renderTaskId;
  cleanupPreview();
  documentVisible.value = true;
  loading.value = true;
  loadingMessage.value = "正在加载当前片段预览…";
  try {
    const current = segments.value.find(item => item.id === selectedSegmentId.value);
    const [file, elements] = await Promise.all([
      getTemplateSegmentPreview(
        selectedSegmentId.value,
        `${current?.segmentKey || "segment"}-preview.docx`
      ),
      listTemplateSegmentElements(selectedSegmentId.value),
    ]);
    if (taskId !== renderTaskId) return;
    segmentElements.value = elements;
    fileName.value = file.name;
    fileSize.value = formatFileSize(file.size);
    await nextTick();
    const containers = getViewerContainers();
    if (!containers) throw new Error("文档预览容器未就绪。");
    const previewResult = await processDocx(file, {
      ...containers,
      onProgress: handleProgress,
    });
    if (taskId !== renderTaskId) return;
    chartStats.value = {
      totalCharts: previewResult.totalCharts,
      renderedCharts: previewResult.renderedCharts,
      partiallyRenderedCharts: previewResult.partiallyRenderedCharts,
      unsupportedCharts: previewResult.unsupportedCharts,
      failedCharts: previewResult.failedCharts,
      charts: previewResult.charts,
    };
    parsedCharts.value = previewResult.charts
      .map(chart => chart.model)
      .filter((model): model is ParsedWordChart => model !== null);
    await refreshBindingContext();
    await refreshTemplateBindings();
    decorateCurrentTemplate(containers.documentContainer);
    syncPreviewSurfaceWidth();
    fitWidth(false);
    setStatus(`已加载片段：${current?.segmentName || selectedSegmentId.value}。`);
  } catch (error) {
    if (taskId === renderTaskId) setStatus(errorMessage(error), true);
  } finally {
    if (taskId === renderTaskId) loading.value = false;
  }
}

async function refreshBindingContext(): Promise<void> {
  bindingSetId.value = "";
  if (!versionView.value || !selectedChapterId.value) return;
  const set = await getOrCreateBindingSet(
    selectedChapterId.value,
    versionView.value.version.id
  );
  bindingSetId.value = set.id;
}

async function refreshTemplateBindings(): Promise<void> {
  if (!versionView.value) return;
  const items = bindingSetId.value
    ? await listBindingItems(bindingSetId.value)
    : [];
  template.value = hydrateTemplateResponse(
    { ...versionView.value, elements: segmentElements.value },
    items
  );
  if (segments.value.length > 0) {
    segments.value = (await listTemplateSegments(
      versionView.value.version.id,
      bindingSetId.value || undefined
    )).items;
  }
}

function decorateCurrentTemplate(container: HTMLElement): void {
  if (!template.value) return;
  const text = decorateRenderedDocument(container, template.value.mockItems, {
    onSelect: selectMockItem,
    onBind: (locatorId, field) => void bindField(locatorId, field),
    onError: (message) => setStatus(message, true),
  });
  renderedLocatorCount.value = text.renderedCount;
  unresolvedLocatorIds.value = text.unresolvedLocatorIds;
  const charts = decorateRenderedCharts(container, template.value.charts, {
    onSelect: selectChart,
    onBind: (locatorId, field) => void bindField(locatorId, field),
    onError: (message) => setStatus(message, true),
  });
  renderedChartCount.value = charts.renderedCount;
  unresolvedChartIds.value = charts.unresolvedLocatorIds;
  const tables = decorateRenderedTables(container, template.value.tables, {
    onSelect: selectTable,
    onBind: (locatorId, field) => void bindField(locatorId, field),
    onError: (message) => setStatus(message, true),
  });
  renderedTableCount.value = tables.renderedCount;
  unresolvedTableIds.value = tables.unresolvedLocatorIds;
}

async function loadBindingPreview(
  templateElementId?: string
): Promise<void> {
  bindingPreview.value = "";
  if (!templateElementId || !bindingSetId.value) return;
  const target = [
    ...(template.value?.mockItems || []),
    ...(template.value?.charts || []),
    ...(template.value?.tables || []),
  ]
    .find((item) => item.templateElementId === templateElementId);
  if (!target?.isBound) return;
  try {
    const preview = await getBindingPreview(bindingSetId.value, templateElementId);
    bindingPreview.value =
      preview.formattedValue ?? JSON.stringify(preview, null, 2);
  } catch (error) {
    bindingPreview.value = errorMessage(error);
  }
}

async function handleChapterChanged(): Promise<void> {
  if (!versionView.value) return;
  await runAction("正在切换章节绑定配置…", async () => {
    await refreshBindingContext();
    await refreshTemplateBindings();
    refreshRenderedBindings();
    setStatus("章节绑定配置已切换。");
  });
}

async function handleRefreshSource(): Promise<void> {
  if (!selectedDataSourceId.value) return;
  await runAction("正在刷新数据源快照…", async () => {
    await refreshDataSource(selectedDataSourceId.value);
    await loadSchema(schemaQuery.value);
    setStatus("数据源快照和字段元数据已刷新。");
  });
}

async function handleValidateBindings(): Promise<void> {
  if (!bindingSetId.value) return;
  await runAction("正在校验绑定配置…", async () => {
    const result = await validateBindingSet(bindingSetId.value);
    const issue = result.items[0]?.message;
    setStatus(
      issue
        ? `校验状态：${result.status}；${issue}`
        : `校验状态：${result.status}`
    );
  });
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
      @export-reusable="handleExportReusable"
      @generate="handleGenerate"
    />

    <section class="workspace-context" aria-label="持久化工作区上下文">
      <label>
        <span>项目</span>
        <select v-model="selectedProjectId" :disabled="loading">
          <option value="">选择项目</option>
          <option v-for="item in projects" :key="item.projectId" :value="item.projectId">
            {{ item.projectCode }} · {{ item.projectName }}
          </option>
        </select>
      </label>
      <label>
        <span>章节</span>
        <select
          v-model="selectedChapterId"
          :disabled="loading || !selectedProjectId"
          @change="handleChapterChanged"
        >
          <option value="">选择章节</option>
          <option v-for="item in chapters" :key="item.id" :value="item.id">
            {{ item.chapterCode }} · {{ item.title }}
          </option>
        </select>
      </label>
      <label>
        <span>模板</span>
        <select
          v-model="selectedTemplateId"
          :disabled="loading"
          @change="handleTemplateSelected"
        >
          <option value="">上传时新建模板</option>
          <option v-for="item in templateCatalog" :key="item.id" :value="item.id">
            {{ item.templateCode }} · {{ item.templateName }}
          </option>
        </select>
      </label>
      <label>
        <span>数据源</span>
        <select
          v-model="selectedDataSourceId"
          :disabled="loading || !selectedProjectId"
        >
          <option value="">选择数据源</option>
          <option v-for="item in dataSources" :key="item.id" :value="item.id">
            {{ item.sourceCode }} · {{ item.sourceName }}
          </option>
        </select>
      </label>
      <button
        type="button"
        class="context-action"
        :disabled="loading || !selectedDataSourceId"
        @click="handleRefreshSource"
      >
        刷新快照
      </button>
      <button
        type="button"
        class="context-action"
        :disabled="loading || !bindingSetId"
        @click="handleValidateBindings"
      >
        校验绑定
      </button>
      <small>
        绑定集 {{ bindingSetId || "—" }} · 模板版本
        {{ versionView?.version.id || "—" }}
      </small>
    </section>

    <div class="workspace">
      <aside class="workspace-panel left-panel">
        <section class="panel-section">
          <div class="segment-panel-heading">
            <h2>模板片段</h2>
            <button
              type="button"
              class="segment-manage-toggle"
              :disabled="!versionView || loading"
              @click="toggleBoundaryManager"
            >
              {{ boundaryManagerVisible ? "收起边界管理" : "管理拆分边界" }}
            </button>
          </div>
          <div v-if="boundaryManagerVisible" class="boundary-manager">
            <p class="boundary-note">
              选择同一层级的连续块。保存或删除边界都会创建新的模板版本。
            </p>
            <label>
              <span>片段名称</span>
              <input v-model="boundaryName" maxlength="255" placeholder="例如：专业监测结果" />
            </label>
            <label>
              <span>片段键</span>
              <input
                v-model="boundaryKey"
                maxlength="128"
                placeholder="例如：major-monitoring"
              />
            </label>
            <label>
              <span>起始块</span>
              <select v-model="boundaryStartBlockId">
                <option
                  v-for="block in selectableOutlineBlocks"
                  :key="block.blockId"
                  :value="block.blockId"
                >
                  {{ outlineLabel(block) }}
                </option>
              </select>
            </label>
            <label>
              <span>结束块</span>
              <select v-model="boundaryEndBlockId">
                <option
                  v-for="block in boundaryEndBlocks"
                  :key="block.blockId"
                  :value="block.blockId"
                >
                  {{ outlineLabel(block) }}
                </option>
              </select>
            </label>
            <button
              type="button"
              class="boundary-create"
              :disabled="loading || selectableOutlineBlocks.length === 0"
              @click="handleInsertBoundary"
            >
              插入拆分边界并创建新版本
            </button>
            <div
              v-if="segments.some(item => item.anchorType === 'CONTENT_CONTROL')"
              class="boundary-existing"
            >
              <strong>已有边界</strong>
              <button
                v-for="segment in segments.filter(
                  item => item.anchorType === 'CONTENT_CONTROL'
                )"
                :key="`remove-${segment.id}`"
                type="button"
                :disabled="loading"
                @click="handleRemoveBoundary(segment)"
              >
                删除 {{ segment.segmentName }}
              </button>
            </div>
          </div>
          <p v-if="segments.length === 0" class="empty-state">选择模板后显示片段</p>
          <div v-else class="segment-tree" role="tree">
            <button
              v-for="segment in segments"
              :key="segment.id"
              type="button"
              role="treeitem"
              class="segment-tree-item"
              :class="{ 'is-selected': segment.id === selectedSegmentId }"
              :style="{ paddingLeft: segmentIndent(segment) }"
              :disabled="loading"
              @click="selectedSegmentId = segment.id; loadSelectedSegment()"
            >
              <span>{{ segment.segmentName }}</span>
              <small>
                {{ segment.bindingProgress.bound }}/{{ segment.bindingProgress.total }}
                · {{ segment.previewStatus }}
              </small>
            </button>
          </div>
        </section>
        <section class="panel-section">
          <h2>模板状态</h2>
          <dl class="metadata">
            <div><dt>模拟值</dt><dd>{{ template?.mockItemCount || 0 }}</dd></div>
            <div><dt>原生图表</dt><dd>{{ template?.chartCount || 0 }}</dd></div>
            <div><dt>可绑定表格</dt><dd>{{ template?.tableCount || 0 }}</dd></div>
            <div><dt>已绑定</dt><dd>{{ template?.bindingCount || 0 }}</dd></div>
            <div><dt>网页定位</dt><dd>{{ renderedLocatorCount }}</dd></div>
            <div><dt>图表定位</dt><dd>{{ renderedChartCount }}</dd></div>
            <div><dt>表格定位</dt><dd>{{ renderedTableCount }}</dd></div>
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
          <h2>表格导航</h2>
          <p v-if="!template" class="empty-state">加载模板后显示可绑定表格</p>
          <p v-else-if="template.tables.length === 0" class="empty-state">当前片段未识别到业务表格</p>
          <div v-else class="mock-list table-target-list">
            <button
              v-for="table in template.tables"
              :key="table.locatorId"
              type="button"
              class="mock-list-item table-list-item"
              :class="{
                'is-bound': table.isBound,
                'is-selected': table.locatorId === selectedLocatorId,
                'is-unresolved': unresolvedTableIds.includes(table.locatorId),
              }"
              @click="focusTable(table)"
            >
              <strong>{{ table.title }}</strong>
              <span>{{ table.boundDataPath || `${table.columns.length} 列 · ${table.isBindable ? '可绑定' : '不可绑定'}` }}</span>
            </button>
          </div>
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
          :partially-rendered-charts="chartStats.partiallyRenderedCharts"
          :unsupported-charts="chartStats.unsupportedCharts"
          :failed-charts="chartStats.failedCharts"
          :charts="chartStats.charts"
        />
      </aside>

      <main ref="previewCanvas" class="preview-column">
        <div class="preview-notice" :class="{ 'is-error': statusIsError }">
          <div class="preview-notice-heading">
            <strong>{{ statusMessage }}</strong>
            <div class="preview-zoom" aria-label="Word 预览缩放">
              <button
                type="button"
                title="缩小"
                aria-label="缩小 Word 预览"
                :disabled="zoomPercent <= 50"
                @click="zoomOut"
              >
                −
              </button>
              <select
                v-model.number="zoomPercent"
                title="选择缩放比例"
                aria-label="Word 预览缩放比例"
              >
                <option v-for="option in zoomOptions" :key="option" :value="option">
                  {{ option }}%
                </option>
              </select>
              <button
                type="button"
                title="放大"
                aria-label="放大 Word 预览"
                :disabled="zoomPercent >= 150"
                @click="zoomIn"
              >
                +
              </button>
              <button type="button" title="恢复原始大小" @click="setZoom(100)">
                100%
              </button>
              <button type="button" title="按预览区域宽度缩放" @click="fitWidth()">
                适应宽度
              </button>
            </div>
          </div>
          <span>网页效果仅用于定位与绑定；批量赋值和最终文件生成均由后端 C# 处理。</span>
          <span class="preview-legend">
            <i class="legend-body"></i>正文模拟值
            <i class="legend-footer"></i>页脚模拟值/区域
            <i class="legend-chart"></i>可绑定图表
            <i class="legend-table"></i>可绑定表格
          </span>
        </div>
        <div
          class="binding-preview-zoom-content"
          :style="{
            '--preview-zoom': zoomPercent / 100,
            width: previewSurfaceWidth ? `${previewSurfaceWidth}px` : '100%',
          }"
        >
          <DocxViewer ref="docxViewerRef" :visible="documentVisible" />
        </div>
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
            标量字段绑定黄色/紫色文本；集合字段绑定橙色图表或青绿色表格。表格选择集合后需确认列映射。
            悬停任意字段可查看完整路径、示例值和内部数据结构。
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
          <p v-if="boundItems.length === 0 && boundCharts.length === 0 && boundTables.length === 0" class="empty-state">尚无绑定关系</p>
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
            <article v-for="table in boundTables" :key="table.locatorId" class="binding-card table-binding-card">
              <button type="button" class="binding-main" @click="focusTable(table)">
                <strong>表格：{{ table.title }} <em>表格</em></strong>
                <span>{{ table.boundDataPath }}</span>
              </button>
              <button type="button" class="binding-remove" @click="removeBinding(table.locatorId)">
                取消绑定
              </button>
            </article>
          </div>
        </section>

        <section v-else-if="activeTab === 'properties'" class="tab-panel properties-panel">
          <p v-if="!selectedItem && !selectedChart && !selectedTable" class="empty-state">
            点击文档高亮、图表、表格或左侧导航查看属性
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
            <div><dt>绑定预览</dt><dd>{{ bindingPreview || "—" }}</dd></div>
            <div><dt>TemplateElementId</dt><dd>{{ selectedItem.templateElementId || "—" }}</dd></div>
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
            <div><dt>绑定预览</dt><dd>{{ bindingPreview || "—" }}</dd></div>
            <div><dt>TemplateElementId</dt><dd>{{ selectedChart.templateElementId || "—" }}</dd></div>
            <div><dt>LocatorId</dt><dd>{{ selectedChart.locatorId }}</dd></div>
          </dl>
          <dl v-else-if="selectedTable">
            <div><dt>目标类型</dt><dd>Word 业务表格</dd></div>
            <div><dt>表格名称</dt><dd>{{ selectedTable.title }}</dd></div>
            <div><dt>列数</dt><dd>{{ selectedTable.columns.length }}</dd></div>
            <div><dt>模板行数</dt><dd>{{ selectedTable.templateRowCount }}</dd></div>
            <div><dt>已绑定集合</dt><dd>{{ selectedTableDataPath || "未绑定" }}</dd></div>
            <div><dt>绑定预览</dt><dd>{{ bindingPreview || "—" }}</dd></div>
            <div><dt>TemplateElementId</dt><dd>{{ selectedTable.templateElementId }}</dd></div>
            <div><dt>LocatorId</dt><dd>{{ selectedTable.locatorId }}</dd></div>
          </dl>
        </section>

        <section v-else-if="activeTab === 'chart-structure'" class="tab-panel">
          <ChartStructurePanel
            :item="selectedChartWorkspaceItem"
            @test-report="handleTestReport"
            @save-mapping="handleSaveMapping"
          />
        </section>
        <section v-else-if="activeTab === 'table-structure'" class="tab-panel">
          <TableBindingPanel
            :table="selectedTable"
            :data-path="selectedTableDataPath"
            :field-options="selectedTableFieldOptions"
            @save="handleSaveTableMapping"
          />
        </section>
      </aside>
    </div>

    <div
      v-if="route.name === 'template-center-studio'"
      class="studio-binding-actions"
    >
      <span>绑定会自动保存到当前章节的绑定集。</span>
      <button
        type="button"
        :disabled="loading || !bindingSetId"
        @click="emit('complete')"
      >
        绑定完成，进入校验
      </button>
    </div>

    <LoadingOverlay :visible="loading" :message="loadingMessage" />
  </div>
</template>

<style scoped>
.segment-panel-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.segment-panel-heading h2 {
  margin: 0;
}

.segment-manage-toggle,
.boundary-create,
.boundary-existing button {
  border: 1px solid #94a3b8;
  border-radius: 0.4rem;
  background: #fff;
  padding: 0.4rem 0.6rem;
}

.boundary-manager {
  display: grid;
  gap: 0.6rem;
  margin: 0.75rem 0;
  padding: 0.75rem;
  border: 1px solid #bfdbfe;
  border-radius: 0.55rem;
  background: #f8fbff;
}

.boundary-manager label {
  display: grid;
  gap: 0.25rem;
}

.boundary-manager input,
.boundary-manager select {
  min-width: 0;
  width: 100%;
  border: 1px solid #cbd5e1;
  border-radius: 0.35rem;
  padding: 0.45rem;
}

.boundary-note {
  margin: 0;
  color: #475569;
  font-size: 0.82rem;
}

.boundary-create {
  border-color: #2563eb;
  background: #2563eb;
  color: #fff;
}

.boundary-existing {
  display: grid;
  gap: 0.4rem;
}

.boundary-existing button {
  border-color: #fca5a5;
  color: #b91c1c;
  text-align: left;
}

.segment-tree {
  display: grid;
  gap: 0.35rem;
}

.segment-tree-item {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  border: 1px solid var(--border-color, #d9dee8);
  border-radius: 0.45rem;
  background: transparent;
  padding: 0.55rem 0.75rem;
  text-align: left;
}

.segment-tree-item.is-selected {
  border-color: #2563eb;
  background: #eff6ff;
}

.segment-tree-item small {
  white-space: nowrap;
  color: #64748b;
}

.studio-binding-actions {
  position: sticky;
  z-index: 20;
  bottom: 12px;
  display: flex;
  justify-content: flex-end;
  gap: 14px;
  align-items: center;
  width: fit-content;
  margin: 14px 22px 0 auto;
  padding: 10px 12px;
  border: 1px solid #d9e2ef;
  border-radius: 10px;
  background: rgb(255 255 255 / 94%);
  box-shadow: 0 10px 28px rgb(31 42 61 / 12%);
}

.studio-binding-actions span {
  color: #667085;
  font-size: 12px;
}

.preview-notice-heading {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
}

.preview-notice-heading > strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-zoom {
  display: flex;
  flex: none;
  gap: 4px;
  align-items: center;
}

.preview-zoom button,
.preview-zoom select {
  height: 26px;
  border: 1px solid #cbd5e1;
  border-radius: 5px;
  background: #fff;
  color: #475467;
  font: 10px/1 system-ui, sans-serif;
}

.preview-zoom button {
  min-width: 27px;
  padding: 0 7px;
  cursor: pointer;
}

.preview-zoom button:hover:not(:disabled),
.preview-zoom select:hover {
  border-color: #7890d8;
  color: #2949b8;
}

.preview-zoom button:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.preview-zoom select {
  padding: 0 4px;
}

.binding-preview-zoom-content {
  --preview-zoom: 1;
  zoom: var(--preview-zoom);
}

@supports not (zoom: 1) {
  .binding-preview-zoom-content {
    transform: scale(var(--preview-zoom));
    transform-origin: top left;
  }
}

/*
 * docx-preview may render mixed-width pages (for example portrait pages next
 * to landscape/chart pages). Let the preview surface follow the widest page,
 * then center every page independently so a wide page cannot pull the rest of
 * the document away from the visual center.
 */
.preview-column :deep(.docx-viewer-wrapper) {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
}

.preview-column :deep(.docx-viewer__document),
.preview-column :deep(.docx-wrapper) {
  width: 100%;
  min-width: 0;
  max-width: none;
  margin-inline: auto;
}

.preview-column :deep(.docx-wrapper) {
  display: block !important;
}

.preview-column :deep(.docx-wrapper > section.docx) {
  margin-right: auto !important;
  margin-left: auto !important;
}

.preview-column :deep(.docx-wrapper > section.docx article p),
.preview-column :deep(.docx-wrapper > section.docx article p > span) {
  max-width: 100%;
  overflow-wrap: anywhere;
  word-break: break-word;
  white-space: pre-wrap !important;
}

.studio-binding-actions button {
  padding: 9px 14px;
  border: 1px solid #3157d5;
  border-radius: 8px;
  background: #3157d5;
  color: #fff;
  cursor: pointer;
}

.studio-binding-actions button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
</style>
