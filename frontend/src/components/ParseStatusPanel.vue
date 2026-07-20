<script setup lang="ts">
import { ref } from "vue";

const props = defineProps<{
  totalCharts: number;
  renderedCharts: number;
  unsupportedCharts: number;
  failedCharts: number;
  charts: Array<{
    slotId: string;
    sourcePath: string;
    detectedType: string;
    status: "rendered" | "unsupported" | "failed";
    message?: string;
  }>;
}>();

const collapsed = ref(false);

function toggleCollapsed() {
  collapsed.value = !collapsed.value;
}

function statusLabel(status: string): string {
  switch (status) {
    case "rendered":
      return "已渲染";
    case "unsupported":
      return "暂不支持";
    case "failed":
      return "失败";
    default:
      return status;
  }
}

function statusClass(status: string): string {
  switch (status) {
    case "rendered":
      return "status--success";
    case "unsupported":
      return "status--warning";
    case "failed":
      return "status--error";
    default:
      return "";
  }
}
</script>

<template>
  <div v-if="props.totalCharts > 0" class="parse-panel">
    <div class="parse-panel__summary" @click="toggleCollapsed">
      <div class="parse-panel__stats">
        <span class="parse-panel__stat">
          Chart总数：<strong>{{ props.totalCharts }}</strong>
        </span>
        <span class="parse-panel__stat parse-panel__stat--success">
          已渲染：<strong>{{ props.renderedCharts }}</strong>
        </span>
        <span class="parse-panel__stat parse-panel__stat--warning">
          暂不支持：<strong>{{ props.unsupportedCharts }}</strong>
        </span>
        <span
          v-if="props.failedCharts > 0"
          class="parse-panel__stat parse-panel__stat--error"
        >
          失败：<strong>{{ props.failedCharts }}</strong>
        </span>
      </div>
      <span class="parse-panel__toggle">{{ collapsed ? "展开" : "收起" }}</span>
    </div>

    <div v-if="!collapsed" class="parse-panel__details">
      <div
        v-for="chart in props.charts"
        :key="chart.slotId"
        class="parse-panel__row"
      >
        <span class="parse-panel__row-path">{{ chart.sourcePath }}</span>
        <span class="parse-panel__row-type">{{ chart.detectedType }}</span>
        <span
          class="parse-panel__row-status"
          :class="statusClass(chart.status)"
        >
          {{ statusLabel(chart.status) }}
        </span>
        <span v-if="chart.message" class="parse-panel__row-msg">
          {{ chart.message }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.parse-panel {
  background: white;
  border-radius: 8px;
  padding: 12px 20px;
  margin-bottom: 16px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
  font-size: 13px;
}

.parse-panel__summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  user-select: none;
}

.parse-panel__stats {
  display: flex;
  gap: 20px;
  flex-wrap: wrap;
}

.parse-panel__stat {
  color: #666;
}

.parse-panel__stat strong {
  color: #333;
  margin-left: 2px;
}

.parse-panel__stat--success strong {
  color: #27ae60;
}

.parse-panel__stat--warning strong {
  color: #e67e22;
}

.parse-panel__stat--error strong {
  color: #e74c3c;
}

.parse-panel__toggle {
  color: #4472c4;
  font-size: 12px;
}

.parse-panel__details {
  margin-top: 10px;
  border-top: 1px solid #eee;
  padding-top: 8px;
  max-height: 300px;
  overflow-y: auto;
}

.parse-panel__row {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 4px 0;
  font-size: 12px;
  font-family: "Consolas", "Courier New", monospace;
}

.parse-panel__row-path {
  color: #666;
  min-width: 140px;
}

.parse-panel__row-type {
  color: #333;
  min-width: 100px;
}

.parse-panel__row-status {
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 500;
}

.status--success {
  background: #eafaf1;
  color: #27ae60;
}

.status--warning {
  background: #fef5e7;
  color: #e67e22;
}

.status--error {
  background: #fdedec;
  color: #e74c3c;
}

.parse-panel__row-msg {
  color: #999;
  font-size: 11px;
}
</style>
