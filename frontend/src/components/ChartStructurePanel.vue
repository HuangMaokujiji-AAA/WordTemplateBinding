<script setup lang="ts">
import { computed, ref } from "vue";
import type { ChartWorkspaceItem } from "../features/binding/chartWorkspace";
import JsonStructureViewer from "./JsonStructureViewer.vue";

const props = defineProps<{
  item: ChartWorkspaceItem | null;
}>();

const MAX_TABLE_ROWS = 100;

const parsed = computed(() => props.item?.parsed ?? null);
const backend = computed(() => props.item?.backend ?? null);
const analysis = computed(() => backend.value?.analysis ?? null);
const dataDef = computed(() => backend.value?.dataDefinition ?? null);
const existingMapping = computed(() => backend.value?.chartMapping ?? null);
const radarGroup = computed(() =>
  parsed.value?.type === "radar"
    ? parsed.value.plotGroups.find((group) => group.type === "radar") ?? null
    : null
);
const radarStyleLabel = computed(() => {
  switch (radarGroup.value?.radarStyle) {
    case "marker": return "带标记";
    case "filled": return "填充";
    default: return "标准";
  }
});
const radarValueAxis = computed(() =>
  parsed.value?.type === "radar"
    ? parsed.value.axes.find((axis) => axis.type === "value") ?? null
    : null
);
const radarMinimum = computed(() =>
  radarValueAxis.value?.min ??
  analysis.value?.chart.radarMinimum ??
  null
);
const radarMaximum = computed(() =>
  radarValueAxis.value?.max ??
  analysis.value?.chart.radarMaximum ??
  null
);

const activeSubTab = ref<"frontend" | "binding" | "sample" | "backend">("binding");

// Mapping state
const categoryField = ref(existingMapping.value?.categoryField ?? "");
const seriesMappings = ref<
  { seriesIndex: number; seriesKey: string; templateName: string; valueField: string; seriesNameField: string }[]
>([]);
const sampleJson = ref("");
const sampleError = ref("");

// Initialize mappings from existing or template
function initMappings(): void {
  if (existingMapping.value) {
    categoryField.value = existingMapping.value.categoryField;
    seriesMappings.value = existingMapping.value.seriesMappings.map((sm) => ({
      seriesIndex: sm.seriesIndex,
      seriesKey: sm.seriesKey,
      templateName: sm.templateSeriesName,
      valueField: sm.valueField,
      seriesNameField: sm.seriesNameField ?? "",
    }));
  } else if (dataDef.value) {
    categoryField.value = "";
    seriesMappings.value = dataDef.value.series.map((s) => ({
      seriesIndex: s.seriesIndex,
      seriesKey: s.seriesKey,
      templateName: s.name,
      valueField: "",
      seriesNameField: "",
    }));
  }
  initSampleData();
}

function initSampleData(): void {
  if (!dataDef.value) return;
  const categories = dataDef.value.category.values;
  const rows: Record<string, unknown>[] = [];
  const rowCount = categories.length;
  for (let i = 0; i < rowCount; i++) {
    const row: Record<string, unknown> = {};
    row[categoryField.value || "category"] = categories[i] ?? "";
    const seriesArr = dataDef.value.series as Array<{ seriesIndex: number; seriesKey: string; values: Array<number | null> }>;
    for (let si = 0; si < seriesArr.length; si++) {
      const s = seriesArr[si];
      const idx: number = s.seriesIndex;
      const sm = seriesMappings.value.find((m: { seriesIndex: number }) => m.seriesIndex === idx);
      const propName: string = sm?.valueField || s.seriesKey;
      row[propName] = s.values[i] ?? null;
    }
    rows.push(row);
  }
  try {
    sampleJson.value = JSON.stringify(rows, null, 2);
  } catch { sampleJson.value = JSON.stringify(rows); }
}

// Emit test report
const emit = defineEmits<{
  (e: "test-report", payload: Record<string, unknown>[]): void;
  (e: "save-mapping", payload: {
    locatorId: string;
    dataPath: string;
    mapping: { mode: string; categoryField: string; seriesMappings: Array<{ seriesIndex: number; seriesKey: string; valueField: string; seriesNameField?: string | null }> };
  }): void;
}>();

function saveMapping(): void {
  if (!props.item?.backend || !dataDef.value) return;
  const dataPath = props.item.backend.boundDataPath;
  if (!dataPath) return;

  emit("save-mapping", {
    locatorId: props.item.backend.locatorId,
    dataPath,
    mapping: {
      mode: dataDef.value.dataMode,
      categoryField: categoryField.value,
      seriesMappings: seriesMappings.value.map((sm) => ({
        seriesIndex: sm.seriesIndex,
        seriesKey: sm.seriesKey,
        valueField: sm.valueField,
        seriesNameField: sm.seriesNameField || null,
      })),
    },
  });
}

// Preview normalized data
const normalizedPreview = computed(() => {
  if (!dataDef.value) return null;
  try {
    const parsed = JSON.parse(sampleJson.value);
    if (!Array.isArray(parsed) || parsed.length === 0) return null;

    const catField = categoryField.value || "category";
    const categories = parsed.map((row: Record<string, unknown>) => row[catField] ?? null);

    const series = seriesMappings.value.map((sm: { seriesIndex: number; seriesKey: string; valueField: string; templateName: string }) => {
      const values = (parsed as Array<Record<string, unknown>>).map((row: Record<string, unknown>) => {
        const raw = row[sm.valueField];
        if (raw === null || raw === undefined) return null;
        const num = Number(raw);
        return isNaN(num) ? null : num;
      });
      return {
        seriesIndex: sm.seriesIndex,
        seriesKey: sm.seriesKey,
        name: sm.templateName,
        values,
      };
    });

    return { categories, series };
  } catch {
    return null;
  }
});

function formatSampleJson(): void {
  try {
    const parsed = JSON.parse(sampleJson.value);
    sampleJson.value = JSON.stringify(parsed, null, 2);
    sampleError.value = "";
  } catch (e) {
    sampleError.value = `JSON 格式错误：${e instanceof Error ? e.message : String(e)}`;
  }
}

function resetSample(): void { initSampleData(); }

async function copySample(): Promise<void> { try { await navigator.clipboard.writeText(sampleJson.value); } catch { /* */ } }

function generateTestReport(): void {
  sampleError.value = "";
  try {
    const parsed = JSON.parse(sampleJson.value);
    if (!Array.isArray(parsed)) { sampleError.value = "数据必须是 JSON 数组。"; return; }
    emit("test-report", parsed as Record<string, unknown>[]);
  } catch (e) { sampleError.value = `JSON 格式错误：${e instanceof Error ? e.message : String(e)}`; }
}

// Expose
defineExpose({ initMappings });

function formatCell(value: string | number | null, isMissing: boolean): string {
  if (isMissing) return "—";
  if (value == null) return "—";
  return String(value);
}

const comparisonWarnings = computed(() => {
  const a = analysis.value; const p = parsed.value;
  if (!a || !p) return [];
  const warnings: string[] = [];
  const bt = a.chart?.type; const ft = p.type;
  if (bt && ft && bt !== ft) warnings.push(`图表类型不一致：前端 "${ft}"，后端 "${bt}"。`);
  const bs = a.series?.length ?? 0; const fs = p.series?.length ?? 0;
  if (bs !== fs) warnings.push(`系列数量不一致：前端 ${fs}，后端 ${bs}。`);
  return warnings;
});
</script>

<template>
  <div class="chart-structure-panel">
    <p v-if="!item" class="empty-state">选中一个图表以查看结构详情</p>
    <template v-else>
      <div v-if="comparisonWarnings.length" class="cs-comparison-warnings">
        <strong>⚠ 解析差异：</strong>
        <ul><li v-for="w in comparisonWarnings" :key="w">{{ w }}</li></ul>
      </div>
      <p v-if="item!.mergeWarnings.length" class="cs-merge-warning">
        {{ item!.mergeWarnings.join("；") }}
      </p>

      <section class="cs-section">
        <h3>概要</h3>
        <dl class="cs-dl">
          <div><dt>标题</dt><dd>{{ parsed!.title?.plainText || "（无标题）" }}</dd></div>
          <div><dt>类型</dt><dd>{{ parsed!.typeLabel }}</dd></div>
          <div v-if="dataDef"><dt>写回能力</dt><dd>{{ dataDef!.writeCapability }}</dd></div>
          <div><dt>分类</dt><dd>{{ parsed!.categories.length }} 项</dd></div>
          <div><dt>系列</dt><dd>{{ parsed!.series.length }} 个</dd></div>
          <div><dt>绑定</dt><dd>{{ item!.boundDataPath || "未绑定" }}</dd></div>
          <template v-if="parsed!.type === 'radar'">
            <div><dt>雷达样式</dt><dd>{{ radarStyleLabel }}</dd></div>
            <div><dt>轴最小值</dt><dd>{{ radarMinimum ?? "自动" }}</dd></div>
            <div><dt>轴最大值</dt><dd>{{ radarMaximum ?? "自动" }}</dd></div>
            <div><dt>嵌入工作簿</dt><dd>{{ parsed!.source.embeddedWorkbookDetected ? "是" : "否" }}</dd></div>
            <div><dt>绑定状态</dt><dd>{{ item!.canBind ? "可绑定" : "结构不完整" }}</dd></div>
          </template>
        </dl>
      </section>

      <section v-if="parsed!.type === 'radar'" class="cs-section">
        <h3>雷达图数据源</h3>
        <dl class="cs-dl">
          <div><dt>分类公式</dt><dd>{{ dataDef?.category.formula || "（缓存字面量）" }}</dd></div>
          <div v-for="series in dataDef?.series ?? []" :key="series.seriesKey">
            <dt>{{ series.name }} 数值公式</dt>
            <dd>{{ series.valueFormula || "（缓存字面量）" }}</dd>
          </div>
        </dl>
        <ul v-if="parsed!.diagnostics.items.length" class="cs-diagnostics">
          <li v-for="diagnostic in parsed!.diagnostics.items" :key="`${diagnostic.code}-${diagnostic.seriesKey ?? ''}`">
            {{ diagnostic.code }}：{{ diagnostic.message }}
          </li>
        </ul>
      </section>

      <div class="cs-subtabs">
        <button :class="{ active: activeSubTab === 'binding' }" @click="activeSubTab = 'binding'; initMappings()">图表绑定</button>
        <button :class="{ active: activeSubTab === 'sample' }" @click="activeSubTab = 'sample'; initSampleData()">测试数据</button>
        <button :class="{ active: activeSubTab === 'frontend' }" @click="activeSubTab = 'frontend'">前端解析</button>
        <button :class="{ active: activeSubTab === 'backend' }" @click="activeSubTab = 'backend'">后端分析</button>
      </div>

      <!-- Chart Binding Tab -->
      <template v-if="activeSubTab === 'binding' && dataDef">
        <section class="cs-section">
          <h3>数据源数组</h3>
          <p class="cs-note">数据路径：{{ item!.boundDataPath || "（请先在数据源中绑定 Array 字段）" }}</p>
          <p v-if="!item!.isBound" class="cs-note cs-note--warn">⚠ 请先将一个 Array 字段拖拽到图表区域完成绑定。</p>
        </section>

        <section class="cs-section">
          <h3>分类字段映射</h3>
          <p class="cs-note">当前值：{{ dataDef!.category.values.filter((v) => v !== null).slice(0, 5).join("、") }}{{ (dataDef!.category.values.length > 5) ? "…" : "" }}</p>
          <div class="cs-field-row">
            <label>绑定到接口字段：</label>
            <input v-model="categoryField" type="text" placeholder="如 grade" class="cs-field-input" />
          </div>
          <p v-if="dataDef!.category.formula" class="cs-note">公式：{{ dataDef!.category.formula }}</p>
        </section>

        <section class="cs-section">
          <h3>系列字段映射</h3>
          <div v-for="sm in seriesMappings" :key="sm.seriesKey" class="cs-mapping-card">
            <strong>{{ sm.templateName }}</strong>
            <span class="cs-note">当前值：{{ dataDef!.series.find((s) => s.seriesIndex === sm.seriesIndex)?.values.slice(0, 3).join("、") ?? "—" }}</span>
            <div class="cs-field-row">
              <label>绑定到接口字段：</label>
              <input v-model="sm.valueField" type="text" placeholder="如 countyScore" class="cs-field-input" />
            </div>
          </div>
        </section>

        <section class="cs-section">
          <button type="button" class="cs-btn cs-btn--primary" :disabled="!item!.isBound" @click="saveMapping">
            保存图表映射
          </button>
        </section>
      </template>

      <!-- Sample Data Tab -->
      <template v-if="activeSubTab === 'sample' && dataDef">
        <section class="cs-section">
          <h3>测试数据</h3>
          <div class="cs-sample-toolbar">
            <button type="button" class="cs-btn" @click="formatSampleJson">格式化 JSON</button>
            <button type="button" class="cs-btn" @click="resetSample">恢复原始数据</button>
            <button type="button" class="cs-btn" @click="copySample">复制</button>
          </div>
          <textarea v-model="sampleJson" class="cs-sample-editor" rows="12" spellcheck="false"></textarea>
          <p v-if="sampleError" class="cs-sample-error">{{ sampleError }}</p>

          <div class="cs-sample-actions">
            <button type="button" class="cs-btn cs-btn--primary" @click="generateTestReport">使用此数据生成测试报告</button>
          </div>
          <p v-if="!item!.isBound" class="cs-note cs-note--warn">⚠ 请先绑定 Array 字段再生成报告。</p>
        </section>

        <section v-if="normalizedPreview" class="cs-section">
          <h3>转换预览（NormalizedChartData）</h3>
          <JsonStructureViewer :data="normalizedPreview" title="标准化数据" />
        </section>
      </template>

      <!-- Frontend Tab -->
      <template v-if="activeSubTab === 'frontend'">
        <section class="cs-section">
          <h3>数据表</h3>
          <div class="cs-table-scroll">
            <table class="cs-table">
              <thead><tr><th v-for="col in parsed!.dataTable.columns" :key="col.key">{{ col.label }}</th></tr></thead>
              <tbody>
                <tr v-for="row in parsed!.dataTable.rows.slice(0, MAX_TABLE_ROWS)" :key="row.index">
                  <td v-for="col in parsed!.dataTable.columns" :key="col.key" :class="{ 'cs-cell--missing': row.isMissing[col.key] }">
                    {{ formatCell(row.cells[col.key] as string | number | null, row.isMissing[col.key]) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
        <section class="cs-section"><JsonStructureViewer :data="parsed" title="ParsedWordChart" /></section>
      </template>

      <!-- Backend Tab -->
      <template v-if="activeSubTab === 'backend'">
        <template v-if="analysis">
          <section class="cs-section"><h3>后端图表分析</h3><JsonStructureViewer :data="analysis" title="ChartAnalysisSnapshot" /></section>
        </template>
        <template v-else>
          <section class="cs-section"><h3>后端分析</h3><p class="cs-note">后端图表分析不可用。</p></section>
        </template>
      </template>
    </template>
  </div>
</template>

<style scoped>
.chart-structure-panel { font-size: 13px; }
.cs-section { margin-bottom: 16px; }
.cs-section h3 { font-size: 13px; margin: 0 0 8px; color: #333; }
.cs-subtabs { display: flex; gap: 4px; margin-bottom: 12px; border-bottom: 2px solid #e0e0e0; }
.cs-subtabs button { background: none; border: none; padding: 6px 14px; font-size: 13px; color: #666; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px; }
.cs-subtabs button:hover { color: #4472c4; }
.cs-subtabs button.active { color: #4472c4; border-bottom-color: #4472c4; font-weight: 600; }
.cs-dl { display: grid; grid-template-columns: 1fr; gap: 4px 0; margin: 0; }
.cs-dl div { display: flex; justify-content: space-between; gap: 8px; padding: 2px 0; border-bottom: 1px dashed #eee; }
.cs-dl dt { color: #888; }
.cs-dl dd { margin: 0; text-align: right; word-break: break-all; }
.cs-comparison-warnings { background: #fef5e7; border: 1px solid #f0c060; border-radius: 6px; padding: 8px 12px; margin-bottom: 12px; font-size: 12px; }
.cs-comparison-warnings ul { margin: 4px 0 0; padding-left: 18px; }
.cs-comparison-warnings li { color: #e67e22; margin-bottom: 2px; }
.cs-table-scroll { overflow-x: auto; max-height: 320px; overflow-y: auto; border: 1px solid #eee; border-radius: 6px; }
.cs-table { border-collapse: collapse; width: 100%; font-size: 12px; }
.cs-table th, .cs-table td { padding: 4px 8px; border-bottom: 1px solid #f0f0f0; white-space: nowrap; text-align: left; }
.cs-table th { background: #fafafa; position: sticky; top: 0; }
.cs-cell--missing { color: #bbb; }
.cs-diagnostics { margin: 8px 0 0; padding-left: 18px; color: #a05a00; font-size: 12px; }
.cs-note { color: #999; font-size: 12px; }
.cs-note--warn { color: #e67e22; font-weight: 500; margin-top: 8px; }
.cs-field-row { display: flex; align-items: center; gap: 8px; margin: 6px 0; }
.cs-field-row label { font-size: 12px; color: #666; white-space: nowrap; }
.cs-field-input { flex: 1; border: 1px solid #ddd; border-radius: 4px; padding: 4px 8px; font-size: 12px; }
.cs-mapping-card { border: 1px solid #eee; border-radius: 6px; padding: 8px 10px; margin-bottom: 6px; }
.cs-mapping-card strong { display: block; margin-bottom: 2px; }
.cs-btn { background: white; border: 1px solid #ddd; border-radius: 4px; padding: 4px 12px; font-size: 12px; cursor: pointer; color: #4472c4; }
.cs-btn:hover { background: #f5f8ff; border-color: #4472c4; }
.cs-btn--primary { background: #4472c4; color: white; border-color: #4472c4; padding: 6px 16px; font-size: 13px; }
.cs-btn--primary:hover { background: #365fa0; }
.cs-btn--primary:disabled { background: #aaa; border-color: #aaa; cursor: not-allowed; }
.cs-sample-toolbar { display: flex; gap: 6px; margin-bottom: 8px; }
.cs-sample-editor { width: 100%; font-family: "Consolas", "Courier New", monospace; font-size: 11px; line-height: 1.4; border: 1px solid #ddd; border-radius: 4px; padding: 8px; resize: vertical; box-sizing: border-box; }
.cs-sample-error { color: #e74c3c; font-size: 12px; margin-top: 4px; }
.cs-sample-actions { margin-top: 8px; }
.empty-state { color: #999; font-size: 13px; }
</style>
