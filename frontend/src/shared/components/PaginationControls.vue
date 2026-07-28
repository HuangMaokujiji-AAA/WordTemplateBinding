<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{
  page: number;
  pageSize: number;
  total: number;
}>();

const emit = defineEmits<{
  change: [page: number];
}>();

const totalPages = computed(() =>
  Math.max(1, Math.ceil(props.total / props.pageSize))
);
</script>

<template>
  <nav
    v-if="total > pageSize"
    class="pagination"
    aria-label="分页导航"
  >
    <button
      type="button"
      class="pagination-button"
      :disabled="page <= 1"
      @click="emit('change', page - 1)"
    >
      上一页
    </button>
    <span class="page-info">
      第 {{ page }} / {{ totalPages }} 页，共 {{ total }} 条
    </span>
    <button
      type="button"
      class="pagination-button"
      :disabled="page >= totalPages"
      @click="emit('change', page + 1)"
    >
      下一页
    </button>
  </nav>
</template>

<style scoped>
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-top: 20px;
  padding: 12px 0;
}

.pagination-button {
  padding: 5px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #fff;
  color: #334155;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
}

.pagination-button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.page-info {
  color: #64748b;
  font-size: 13px;
}
</style>
