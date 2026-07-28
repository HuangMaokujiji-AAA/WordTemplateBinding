<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";

const route = useRoute();

const pageContent = computed(() => ({
  title: (route.meta.pageTitle as string | undefined) || "报告生成中心",
  description:
    (route.meta.pageDescription as string | undefined) ||
    "批量报告生成能力将在后续步骤接入。",
  action:
    (route.meta.primaryAction as string | undefined) || "前往模板制作中心",
}));
</script>

<template>
  <div class="placeholder-page">
    <section class="placeholder-card">
      <span class="placeholder-kicker">报告生成中心</span>
      <h1>{{ pageContent.title }}</h1>
      <p>{{ pageContent.description }}</p>
      <div class="flow-preview" aria-label="报告生成九步流程">
        <span v-for="step in 9" :key="step" :class="{ active: step === 1 }">
          {{ step }}
        </span>
      </div>
      <div class="placeholder-note">
        当前阶段已建立清晰入口和页面骨架。模板发布、异步任务和产物下载接入后，这里会承载完整九步向导。
      </div>
      <router-link class="placeholder-action" to="/template-center/templates">
        {{ pageContent.action }}
      </router-link>
    </section>
  </div>
</template>

<style scoped>
.placeholder-page {
  min-height: calc(100vh - 96px);
  padding: 56px;
  background: #f5f8f6;
}

.placeholder-card {
  max-width: 760px;
  margin: 0 auto;
  padding: 38px;
  border: 1px solid #d9e7dc;
  border-radius: 16px;
  background: #fff;
  box-shadow: 0 18px 44px rgb(25 74 45 / 7%);
}

.placeholder-kicker {
  color: #238b57;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.12em;
}

h1 {
  margin: 9px 0 8px;
  color: #1f2a24;
  font-size: 28px;
}

p {
  margin: 0;
  color: #667085;
}

.flow-preview {
  display: grid;
  grid-template-columns: repeat(9, 1fr);
  gap: 8px;
  margin: 28px 0 20px;
}

.flow-preview span {
  display: grid;
  aspect-ratio: 1;
  place-items: center;
  border: 1px solid #d8e2da;
  border-radius: 50%;
  background: #f8fbf9;
  color: #87968c;
  font-size: 11px;
  font-weight: 700;
}

.flow-preview span.active {
  border-color: #238b57;
  background: #e9f7ed;
  color: #177345;
}

.placeholder-note {
  margin-bottom: 22px;
  padding: 13px 15px;
  border: 1px solid #e0e8e2;
  border-radius: 9px;
  background: #f8fbf9;
  color: #667085;
  font-size: 12px;
}

.placeholder-action {
  display: inline-block;
  padding: 9px 14px;
  border-radius: 8px;
  background: #238b57;
  color: #fff;
  text-decoration: none;
  font-size: 12px;
  font-weight: 650;
}
</style>
