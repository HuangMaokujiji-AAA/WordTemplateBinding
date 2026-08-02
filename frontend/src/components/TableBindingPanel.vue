<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type {
  TableBindingMapping,
  TableColumnBinding,
  TableItem,
} from "../api/types";

const props = withDefaults(defineProps<{
  table: TableItem | null;
  dataPath: string;
  fieldOptions: Array<{ label: string; value: string }>;
  fieldOptionsLoading?: boolean;
  fieldOptionsError?: string;
}>(), {
  fieldOptionsLoading: false,
  fieldOptionsError: "",
});

const emit = defineEmits<{
  save: [payload: {
    table: TableItem;
    dataPath: string;
    mapping: TableBindingMapping;
  }];
}>();

interface MappingRow {
  columnIndex: number;
  header: string;
  sourceField: string;
  fallbackValue: string;
}

const rows = ref<MappingRow[]>([]);
const headerRowCount = ref(1);
const filterField = ref("");
const filterValue = ref("");

const errorMessage = computed(() => {
  if (!props.table) return "请先选择一个表格。";
  if (!props.dataPath) return "请先给表格选择一个 Array 集合字段。";
  if (props.fieldOptionsError) return props.fieldOptionsError;
  if (!props.fieldOptionsLoading && props.fieldOptions.length === 0) {
    return "当前 Array 没有可供选择的内部数据字段，请刷新数据源快照。";
  }
  if (headerRowCount.value < 1 || headerRowCount.value >= props.table.templateRowCount) {
    return `表头行数必须在 1～${Math.max(1, props.table.templateRowCount - 1)} 之间。`;
  }
  if (!rows.value.some((row) => row.sourceField.trim().length > 0)) {
    return "至少需要配置一列数据字段。";
  }
  return "";
});

function reset(): void {
  const table = props.table;
  if (!table) {
    rows.value = [];
    return;
  }
  const mapping = table.tableMapping || table.defaultMapping;
  const mappingsByColumn = new Map(
    mapping.columns.map((column) => [column.columnIndex, column])
  );
  rows.value = table.columns.map((column) => {
    const current = mappingsByColumn.get(column.columnIndex);
    return {
      columnIndex: column.columnIndex,
      header: column.header,
      sourceField: current?.sourceField || column.suggestedField || "",
      fallbackValue: current?.fallbackValue || "",
    };
  });
  headerRowCount.value = mapping.headerRowCount || table.headerRowCount;
  filterField.value = mapping.filterField || "";
  filterValue.value = mapping.filterValue || "";
}

function save(): void {
  if (!props.table || errorMessage.value) return;
  const columns: TableColumnBinding[] = rows.value
    .filter((row) => row.sourceField.trim().length > 0)
    .map((row) => ({
      columnIndex: row.columnIndex,
      header: row.header || null,
      sourceField: row.sourceField.trim(),
      fallbackValue: row.fallbackValue || null,
    }));
  emit("save", {
    table: props.table,
    dataPath: props.dataPath,
    mapping: {
      headerRowCount: headerRowCount.value,
      columns,
      filterField: filterField.value || null,
      filterValue: filterValue.value || null,
    },
  });
}

function isAvailableField(value: string): boolean {
  return value === "rowNumber" || props.fieldOptions.some((field) => field.value === value);
}

watch(
  () => [props.table?.locatorId, props.table?.tableMapping, props.dataPath],
  reset,
  { immediate: true }
);
</script>

<template>
  <div v-if="!table" class="table-binding-empty">
    点击 Word 预览中的绿色表格或左侧表格导航开始配置。
  </div>
  <div v-else class="table-binding-panel">
    <header>
      <div>
        <strong>{{ table.title }}</strong>
        <span>{{ table.columns.length }} 列 · 模板共 {{ table.templateRowCount }} 行</span>
      </div>
      <small>{{ dataPath || "尚未选择集合字段" }}</small>
    </header>

    <p v-if="!dataPath" class="table-binding-notice">
      请先在“数据源”页选择一个 Array 字段，系统会在保存映射时一起建立表格绑定。
    </p>
    <p v-else-if="fieldOptionsLoading" class="table-binding-notice">
      正在读取绑定数据源中的字段…
    </p>
    <p v-else-if="fieldOptions.length > 0" class="table-binding-field-status">
      已读取 {{ fieldOptions.length }} 个可用于列映射的数据字段。
    </p>

    <label class="table-header-count">
      <span>表头行数</span>
      <input v-model.number="headerRowCount" type="number" min="1" :max="Math.max(1, table.templateRowCount - 1)" />
    </label>

    <section>
      <h3>列字段映射</h3>
      <p>从当前数组的数据库字段中选择；不绑定的列在导出时会清空模板样例内容。</p>
      <div class="table-column-list">
        <article v-for="row in rows" :key="row.columnIndex">
          <div class="table-column-title">
            <span>第 {{ row.columnIndex + 1 }} 列</span>
            <strong>{{ row.header || "未命名列" }}</strong>
          </div>
          <label>
            <span>数据字段</span>
            <select v-model="row.sourceField" :disabled="fieldOptionsLoading">
              <option value="">不绑定此列（导出清空）</option>
              <option value="rowNumber">自动序号</option>
              <option v-for="field in fieldOptions" :key="field.value" :value="field.value">
                {{ field.label }}（{{ field.value }}）
              </option>
              <option
                v-if="row.sourceField && !isAvailableField(row.sourceField)"
                :value="row.sourceField"
              >
                {{ row.sourceField }}（历史字段，当前数据源中未找到）
              </option>
            </select>
          </label>
          <label>
            <span>缺省值</span>
            <input v-model="row.fallbackValue" placeholder="字段为空时使用，可不填" />
          </label>
        </article>
      </div>
    </section>

    <details class="table-filter">
      <summary>可选：数据行过滤</summary>
      <label><span>过滤字段</span><input v-model="filterField" placeholder="如 collegeName" /></label>
      <label><span>等于</span><input v-model="filterValue" placeholder="如 当前学院名称" /></label>
    </details>

    <p v-if="errorMessage" class="table-binding-error">{{ errorMessage }}</p>
    <button
      type="button"
      class="table-binding-save"
      :disabled="fieldOptionsLoading || Boolean(errorMessage)"
      @click="save"
    >
      保存表格绑定与列映射
    </button>
  </div>
</template>

<style scoped>
.table-binding-empty,
.table-binding-notice,
.table-binding-field-status,
.table-binding-panel p {
  color: #64748b;
  font-size: 11px;
}

.table-binding-panel {
  display: grid;
  gap: 14px;
}

.table-binding-panel header,
.table-binding-panel header > div {
  display: grid;
  gap: 3px;
}

.table-binding-panel header {
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  padding-bottom: 10px;
  border-bottom: 1px solid #e2e8f0;
}

.table-binding-panel header span,
.table-binding-panel header small {
  color: #64748b;
  font-size: 10px;
}

.table-binding-notice {
  margin: 0;
  padding: 9px;
  border-radius: 6px;
  background: #fff7ed;
}

.table-binding-field-status {
  margin: 0;
  padding: 8px 9px;
  border-radius: 6px;
  background: #ecfdf5;
  color: #047857;
}

.table-header-count,
.table-column-list article,
.table-filter label {
  display: grid;
  gap: 5px;
}

.table-header-count {
  grid-template-columns: 80px 90px;
  align-items: center;
}

.table-binding-panel h3 {
  margin: 0;
  font-size: 12px;
}

.table-binding-panel section > p {
  margin: 4px 0 9px;
}

.table-column-list {
  display: grid;
  gap: 8px;
}

.table-column-list article {
  padding: 9px;
  border: 1px solid #dbe3ec;
  border-radius: 7px;
  background: #f8fafc;
}

.table-column-title {
  display: flex;
  gap: 7px;
  align-items: center;
}

.table-column-title span,
.table-column-list label span,
.table-filter label span,
.table-header-count span {
  color: #64748b;
  font-size: 10px;
}

.table-column-list input,
.table-column-list select,
.table-filter input,
.table-header-count input {
  min-width: 0;
  height: 32px;
  padding: 0 8px;
  border: 1px solid #cbd5e1;
  border-radius: 5px;
  background: #fff;
}

.table-filter {
  padding: 9px;
  border: 1px solid #e2e8f0;
  border-radius: 7px;
}

.table-filter summary {
  margin-bottom: 8px;
  cursor: pointer;
  font-size: 11px;
}

.table-filter label + label {
  margin-top: 7px;
}

.table-binding-error {
  margin: 0 !important;
  color: #b42318 !important;
}

.table-binding-save {
  padding: 9px 12px;
  border: 1px solid #0f766e;
  border-radius: 7px;
  background: #0f766e;
  color: #fff;
}

.table-binding-save:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}
</style>
