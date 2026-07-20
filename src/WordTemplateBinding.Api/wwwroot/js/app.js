import {
  deleteBinding,
  downloadReport,
  getSchema,
  getTemplate,
  rescanTemplate,
  uploadTemplate,
  upsertBinding,
} from "./api-client.js";
import {
  focusMockItem,
  renderParagraphNavigation,
  renderPreview,
} from "./template-view.js";
import { renderSchemaTree } from "./schema-tree.js";
import {
  renderBindingList,
  renderProperties,
} from "./binding-manager.js";

const state = {
  template: null,
  selectedLocatorId: null,
  schema: null,
  searchTimer: null,
};

const elements = {
  fileInput: document.getElementById("fileInput"),
  uploadButton: document.getElementById("uploadButton"),
  rescanButton: document.getElementById("rescanButton"),
  generateButton: document.getElementById("generateButton"),
  templateName: document.getElementById("templateName"),
  bindingCount: document.getElementById("bindingCount"),
  statusText: document.getElementById("statusText"),
  mockCount: document.getElementById("mockCount"),
  leftBindingCount: document.getElementById("leftBindingCount"),
  contentHash: document.getElementById("contentHash"),
  paragraphNavigation: document.getElementById("paragraphNavigation"),
  documentPreview: document.getElementById("documentPreview"),
  schemaSearch: document.getElementById("schemaSearch"),
  schemaSummary: document.getElementById("schemaSummary"),
  schemaTree: document.getElementById("schemaTree"),
  bindingList: document.getElementById("bindingList"),
  propertiesPanel: document.getElementById("propertiesPanel"),
};

elements.uploadButton.addEventListener("click", () => elements.fileInput.click());
elements.fileInput.addEventListener("change", handleUpload);
elements.rescanButton.addEventListener("click", handleRescan);
elements.generateButton.addEventListener("click", handleGenerate);
elements.schemaSearch.addEventListener("input", handleSchemaSearch);

for (const tab of document.querySelectorAll(".tab")) {
  tab.addEventListener("click", () => activateTab(tab.dataset.tab));
}

await loadSchema("");

async function handleUpload() {
  const [file] = elements.fileInput.files;
  if (!file) {
    return;
  }

  await runAction("正在上传并扫描模板…", async () => {
    state.template = await uploadTemplate(file);
    state.selectedLocatorId = null;
    renderApplication();
    setStatus(`已识别 ${state.template.mockItemCount} 个模拟数据`);
  });
  elements.fileInput.value = "";
}

async function handleRescan() {
  if (!state.template) {
    return;
  }

  await runAction("正在从原始模板重新扫描…", async () => {
    state.template = await rescanTemplate(state.template.templateId);
    syncSelection();
    renderApplication();
    setStatus("重新扫描完成");
  });
}

async function handleGenerate() {
  if (!state.template) {
    return;
  }

  await runAction("正在生成 DOCX 报告…", async () => {
    const fileName = await downloadReport(state.template.templateId);
    setStatus(`报告已生成：${fileName}`);
  });
}

function handleSchemaSearch() {
  window.clearTimeout(state.searchTimer);
  state.searchTimer = window.setTimeout(
    () => loadSchema(elements.schemaSearch.value),
    280,
  );
}

async function loadSchema(query) {
  try {
    elements.schemaSummary.textContent = query.trim()
      ? "正在搜索字段…"
      : "正在加载字段树…";
    state.schema = await getSchema(query);
    renderSchemaTree(elements.schemaTree, state.schema.nodes);
    if (state.schema.query) {
      elements.schemaSummary.textContent = state.schema.isTruncated
        ? `匹配 ${state.schema.matchCount} 项，显示前 200 项`
        : `匹配 ${state.schema.matchCount} 项`;
    } else {
      elements.schemaSummary.textContent =
        `约 ${state.schema.totalLeafCount} 个叶子字段，默认折叠加载`;
    }
  } catch (error) {
    setStatus(error.message, true);
  }
}

async function bindField(locatorId, field) {
  if (!state.template || !field?.path) {
    return;
  }

  await runAction(`正在绑定 ${field.path}…`, async () => {
    await upsertBinding(state.template.templateId, locatorId, field.path);
    state.template = await getTemplate(state.template.templateId);
    state.selectedLocatorId = locatorId;
    renderApplication();
    activateTab("properties");
    setStatus(`已绑定字段：${field.path}`);
  });
}

async function removeBinding(locatorId) {
  if (!state.template) {
    return;
  }

  await runAction("正在取消绑定…", async () => {
    await deleteBinding(state.template.templateId, locatorId);
    state.template = await getTemplate(state.template.templateId);
    syncSelection();
    renderApplication();
    setStatus("绑定已取消");
  });
}

function renderApplication() {
  const template = state.template;
  if (!template) {
    return;
  }

  elements.templateName.textContent = template.fileName;
  elements.bindingCount.textContent = `绑定 ${template.bindingCount} 项`;
  elements.mockCount.textContent = String(template.mockItemCount);
  elements.leftBindingCount.textContent = String(template.bindingCount);
  elements.contentHash.textContent = template.contentHash.slice(0, 12);
  elements.contentHash.title = template.contentHash;
  elements.rescanButton.disabled = false;
  elements.generateButton.disabled = template.bindingCount === 0;

  renderPreview(
    elements.documentPreview,
    template.preview,
    template.mockItems,
    {
      onSelect: selectMockItem,
      onBind: bindField,
      onError: (message) => setStatus(message, true),
    },
  );
  renderParagraphNavigation(elements.paragraphNavigation, template.preview);
  renderBindingList(elements.bindingList, template.mockItems, {
    onFocus: (item) => {
      selectMockItem(item);
      focusMockItem(item.locatorId);
      activateTab("properties");
    },
    onDelete: removeBinding,
  });
  renderProperties(elements.propertiesPanel, getSelectedMockItem());
}

function selectMockItem(item) {
  state.selectedLocatorId = item?.locatorId || null;
  renderProperties(elements.propertiesPanel, item);
  activateTab("properties");
}

function getSelectedMockItem() {
  return (
    state.template?.mockItems.find(
      (item) => item.locatorId === state.selectedLocatorId,
    ) || null
  );
}

function syncSelection() {
  if (!getSelectedMockItem()) {
    state.selectedLocatorId = null;
  }
}

function activateTab(tabName) {
  for (const tab of document.querySelectorAll(".tab")) {
    tab.classList.toggle("active", tab.dataset.tab === tabName);
  }
  for (const panel of document.querySelectorAll(".tab-panel")) {
    panel.classList.toggle("active", panel.dataset.panel === tabName);
  }
}

async function runAction(message, action) {
  setBusy(true);
  setStatus(message);
  try {
    await action();
  } catch (error) {
    setStatus(error.message || "操作失败。", true);
  } finally {
    setBusy(false);
  }
}

function setBusy(isBusy) {
  elements.uploadButton.disabled = isBusy;
  elements.rescanButton.disabled = isBusy || !state.template;
  elements.generateButton.disabled =
    isBusy || !state.template || state.template.bindingCount === 0;
}

function setStatus(message, isError = false) {
  elements.statusText.textContent = message;
  elements.statusText.classList.toggle("error", isError);
}
