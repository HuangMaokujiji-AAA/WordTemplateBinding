<script setup lang="ts">
defineProps<{
  visible: boolean;
  message: string;
}>();
</script>

<template>
  <div v-if="visible" class="loading-overlay">
    <div class="loading-overlay__spinner"></div>
    <div class="loading-overlay__message">{{ message }}</div>
  </div>
</template>

<style scoped>
.loading-overlay {
  position: fixed;
  inset: 0;
  background: rgba(255, 255, 255, 0.85);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.loading-overlay__spinner {
  width: 36px;
  height: 36px;
  border: 3px solid #e0e0e0;
  border-top-color: #4472c4;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.loading-overlay__message {
  margin-top: 16px;
  font-size: 14px;
  color: #666;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
