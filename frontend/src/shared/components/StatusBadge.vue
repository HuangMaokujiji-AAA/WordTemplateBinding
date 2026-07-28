<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string;
    color?: string;
  }>(),
  {
    color: "#6b7280",
  }
);
</script>

<template>
  <span
    class="status-badge"
    :style="{
      background: `${color}18`,
      color,
      borderColor: `${color}30`,
    }"
  >
    {{ label }}
  </span>
</template>

<style scoped>
.status-badge {
  display: inline-block;
  padding: 2px 10px;
  border: 1px solid;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 500;
}
</style>
