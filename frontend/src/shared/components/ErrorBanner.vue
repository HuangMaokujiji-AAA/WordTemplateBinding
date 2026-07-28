<script setup lang="ts">
defineProps<{
  message: string;
}>();
</script>

<template>
  <div v-if="message" class="error-banner" role="alert">
    {{ message }}
  </div>
</template>

<style scoped>
.error-banner {
  margin-bottom: 16px;
  padding: 10px 16px;
  border: 1px solid #fecaca;
  border-radius: 6px;
  background: #fef2f2;
  color: #ef4444;
  font-size: 13px;
}
</style>
