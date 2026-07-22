<script setup lang="ts">
import { computed } from "vue";
import type { ChartWorkspaceItem } from "../features/binding/chartWorkspace";
import JsonStructureViewer from "./JsonStructureViewer.vue";

const props = defineProps<{
  item: ChartWorkspaceItem | null;
}>();

const MAX_TABLE_ROWS = 100;

const parsed = computed(() => props.item?.parsed ?? null);

const visibleRows = computed(() => parsed.value?.dataTable.rows.slice(0, MAX_TABLE_ROWS) ?? []);
const rowsTruncated = computed(
  () => (parsed.value?.dataTable.rowCount ?? 0) > MAX_TABLE_ROWS
);

function formatCell(value: string | number | null, isMissing: boolean): string {
  if (isMissing) return "—";
  if (value == null) return "—";
  return String(value);
}

function diagnosticLevelLabel(level: string): string {
  switch (level) {
    case "error": return "错误";
    case "warning": return "警告";
    default: return "信息";
  }
}

function axisRoleLabel(role: string): string {
  switch (role) {
    case "x": return "主 X 轴";
    case "y": return "主 Y 轴";
    case "secondary-x": return "次 X 轴";
    case "secondary-y": return "次 Y 轴";
    default: return "未知";
  }
}

function bindingSlotStatusLabel(bindable: boolean): string {
  return bindable ? "可绑定" : "已识别，当前版本暂未开放单独绑定";
}
</script>

<template>
  <div class="chart-structure-panel">
    <p v-if="!item" class="empty-state">选中一个图表以查看结构详情</p>

    <template v-else>
      <section class="cs-section">
        <h3>概要</h3>
        <dl class="cs-dl">
          <div><dt>图表标题</dt><dd>{{ parsed!.title?.plainText || "（无标题）" }}</dd></div>
          <div><dt>图表类型</dt><dd>{{ parsed!.typeLabel }}</dd></div>
          <div><dt>ChartPart</dt><dd>{{ parsed!.source.chartPartPath }}</dd></div>
          <div><dt>RelationshipId</dt><dd>{{ parsed!.identity.relationshipId }}</dd></div>
          <div><dt>文档顺序</dt><dd>{{ parsed!.identity.documentOrder }}</dd></div>
          <div><dt>尺寸</dt><dd>{{ parsed!.dimensions.widthPx }} × {{ parsed!.dimensions.heightPx }} px</dd></div>
          <div><dt>支持网页预览</dt><dd>{{ parsed!.supportedForPreview ? "是" : "否" }}</dd></div>
          <div><dt>支持整图绑定</dt><dd>{{ item!.canBind ? "是" : "否" }}</dd></div>
          <div><dt>当前绑定字段</dt><dd>{{ item!.boundDataPath || "未绑定" }}</dd></div>
          <div><dt>嵌入工作簿</dt><dd>{{ parsed!.source.embeddedWorkbookPath || "未检测到" }}</dd></div>
          <div><dt>解析状态</dt>
            <dd>
              完整度 {{ parsed!.diagnostics.completenessScore }}/100
              <span v-if="parsed!.diagnostics.hasErrors" class="cs-badge cs-badge--error">存在错误</span>
              <span v-else-if="parsed!.diagnostics.hasWarnings" class="cs-badge cs-badge--warning">存在警告</span>
            </dd>
          </div>
        </dl>
        <p v-if="item!.mergeWarnings.length" class="cs-merge-warning">
          {{ item!.mergeWarnings.join("；") }}
        </p>
      </section>

      <section class="cs-section">
        <h3>数据表（{{ parsed!.dataTable.rowCount }} 行 × {{ parsed!.dataTable.columnCount }} 列）</h3>
        <div class="cs-table-scroll">
          <table class="cs-table">
            <thead>
              <tr>
                <th v-for="col in parsed!.dataTable.columns" :key="col.key">{{ col.label }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in visibleRows" :key="row.index">
                <td
                  v-for="col in parsed!.dataTable.columns"
                  :key="col.key"
                  :class="{ 'cs-cell--missing': row.isMissing[col.key] }"
                >
                  {{ formatCell(row.cells[col.key], row.isMissing[col.key]) }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-if="rowsTruncated" class="cs-note">仅显示前 {{ MAX_TABLE_ROWS }} 行，共 {{ parsed!.dataTable.rowCount }} 行。</p>
      </section>

      <section class="cs-section">
        <h3>系列（{{ parsed!.series.length }}）</h3>
        <details v-for="s in parsed!.series" :key="s.key" class="cs-series">
          <summary>{{ s.name }}（{{ s.chartType }}）</summary>
          <dl class="cs-dl">
            <div><dt>index / order</dt><dd>{{ s.index }} / {{ s.order }}</dd></div>
            <div><dt>所属图表组</dt><dd>{{ s.plotGroupId }}</dd></div>
            <div><dt>轴</dt><dd>{{ s.axisRole === "secondary" ? "次轴" : s.axisRole === "primary" ? "主轴" : "无" }}</dd></div>
            <div><dt>名称公式</dt><dd>{{ s.nameSource.formula || "（无，直接值）" }}</dd></div>
            <div v-if="s.xValues"><dt>X 公式</dt><dd>{{ s.xValues.formula || "—" }}</dd></div>
            <div v-if="s.yValues"><dt>Y 公式</dt><dd>{{ s.yValues.formula || "—" }}</dd></div>
            <div v-if="!s.xValues"><dt>数值公式</dt><dd>{{ s.values.formula || "（无，直接值）" }}</dd></div>
            <div><dt>点数量</dt><dd>{{ s.values.pointCount ?? 0 }}</dd></div>
            <div><dt>数值格式</dt><dd>{{ s.values.formatCode || "—" }}</dd></div>
            <div><dt>颜色</dt><dd>{{ s.style.fill?.color?.resolvedHex || s.style.fill?.color?.raw || "（默认调色板）" }}</dd></div>
            <div><dt>数据标签</dt><dd>{{ s.dataLabels?.showValue ? "显示数值" : "不显示" }}</dd></div>
            <div><dt>标记样式</dt><dd>{{ s.marker?.symbol || "—" }}</dd></div>
          </dl>
        </details>
      </section>

      <section class="cs-section">
        <h3>坐标轴（{{ parsed!.axes.length }}）</h3>
        <p v-if="parsed!.axes.length === 0" class="cs-note">该图表没有坐标轴（如饼图/环形图）。</p>
        <details v-for="ax in parsed!.axes" :key="ax.id" class="cs-series">
          <summary>{{ ax.id }} · {{ axisRoleLabel(ax.role) }}</summary>
          <dl class="cs-dl">
            <div><dt>轴类型</dt><dd>{{ ax.type }}</dd></div>
            <div><dt>位置</dt><dd>{{ ax.position || "—" }}</dd></div>
            <div><dt>标题</dt><dd>{{ ax.title?.plainText || "—" }}</dd></div>
            <div><dt>最小值 / 最大值</dt><dd>{{ ax.min ?? "自动" }} / {{ ax.max ?? "自动" }}</dd></div>
            <div><dt>主单位 / 次单位</dt><dd>{{ ax.majorUnit ?? "自动" }} / {{ ax.minorUnit ?? "自动" }}</dd></div>
            <div><dt>数字格式</dt><dd>{{ ax.numberFormat || "—" }}</dd></div>
            <div><dt>方向</dt><dd>{{ ax.reversed ? "反向" : "正常" }}</dd></div>
            <div><dt>交叉轴 ID</dt><dd>{{ ax.crossAxisId || "—" }}</dd></div>
            <div><dt>可见</dt><dd>{{ ax.visible ? "是" : "否" }}</dd></div>
          </dl>
        </details>
      </section>

      <section class="cs-section">
        <h3>绑定结构</h3>
        <p class="cs-note">默认模式：{{ parsed!.bindingSchema.defaultMode }}</p>
        <ul class="cs-slot-list">
          <li v-for="slot in parsed!.bindingSchema.slots" :key="slot.id">
            <strong>{{ slot.label }}</strong>
            <span class="cs-slot-status" :class="{ 'cs-slot-status--bindable': slot.bindable }">
              {{ bindingSlotStatusLabel(slot.bindable) }}
            </span>
            <span v-if="slot.currentSourceFormula" class="cs-slot-formula">{{ slot.currentSourceFormula }}</span>
          </li>
        </ul>
      </section>

      <section class="cs-section">
        <h3>诊断信息（{{ parsed!.diagnostics.items.length }}）</h3>
        <p v-if="parsed!.diagnostics.items.length === 0" class="cs-note">未发现问题。</p>
        <ul v-else class="cs-diagnostics-list">
          <li v-for="(diag, i) in parsed!.diagnostics.items" :key="i" :class="`cs-diag--${diag.level}`">
            <span class="cs-badge" :class="`cs-badge--${diag.level}`">{{ diagnosticLevelLabel(diag.level) }}</span>
            {{ diag.message }}
          </li>
        </ul>
      </section>

      <section class="cs-section">
        <JsonStructureViewer :data="parsed" title="ParsedWordChart" />
      </section>
    </template>
  </div>
</template>

<style scoped>
.chart-structure-panel {
  font-size: 13px;
}

.cs-section {
  margin-bottom: 16px;
}

.cs-section h3 {
  font-size: 13px;
  margin: 0 0 8px;
  color: #333;
}

.cs-dl {
  display: grid;
  grid-template-columns: 1fr;
  gap: 4px 0;
  margin: 0;
}

.cs-dl div {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding: 2px 0;
  border-bottom: 1px dashed #eee;
}

.cs-dl dt {
  color: #888;
}

.cs-dl dd {
  margin: 0;
  text-align: right;
  word-break: break-all;
}

.cs-merge-warning {
  color: #e67e22;
  font-size: 12px;
  margin-top: 6px;
}

.cs-table-scroll {
  overflow-x: auto;
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid #eee;
  border-radius: 6px;
}

.cs-table {
  border-collapse: collapse;
  width: 100%;
  font-size: 12px;
}

.cs-table th,
.cs-table td {
  padding: 4px 8px;
  border-bottom: 1px solid #f0f0f0;
  white-space: nowrap;
  text-align: left;
}

.cs-table th {
  background: #fafafa;
  position: sticky;
  top: 0;
}

.cs-cell--missing {
  color: #bbb;
}

.cs-note {
  color: #999;
  font-size: 12px;
}

.cs-series {
  border: 1px solid #eee;
  border-radius: 6px;
  padding: 6px 10px;
  margin-bottom: 6px;
}

.cs-series summary {
  cursor: pointer;
  font-weight: 500;
}

.cs-slot-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.cs-slot-list li {
  border: 1px solid #eee;
  border-radius: 6px;
  padding: 6px 10px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.cs-slot-status {
  font-size: 11px;
  color: #999;
}

.cs-slot-status--bindable {
  color: #27ae60;
}

.cs-slot-formula {
  font-family: "Consolas", monospace;
  font-size: 11px;
  color: #666;
}

.cs-diagnostics-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 12px;
}

.cs-badge {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 11px;
  margin-right: 6px;
}

.cs-badge--error {
  background: #fdedec;
  color: #e74c3c;
}

.cs-badge--warning {
  background: #fef5e7;
  color: #e67e22;
}

.cs-badge--info {
  background: #eaf2fb;
  color: #4472c4;
}

.empty-state {
  color: #999;
  font-size: 13px;
}
</style>
