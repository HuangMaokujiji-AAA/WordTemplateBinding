<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";

const route = useRoute();

const navigation = [
  {
    to: "/report-center/jobs",
    label: "生成任务",
    description: "查看进行中的批量任务",
  },
  {
    to: "/report-center/new",
    label: "新建生成任务",
    description: "按向导配置报告",
  },
  {
    to: "/report-center/history",
    label: "生成记录",
    description: "查询历史结果与产物",
  },
];

const currentLabel = computed(
  () => (route.meta.breadcrumb as string | undefined) || "报告生成中心"
);
</script>

<template>
  <div class="center-layout report-center">
    <aside class="center-sidebar" aria-label="报告生成中心导航">
      <div class="center-identity">
        <span class="center-index">02</span>
        <div>
          <strong>报告生成中心</strong>
          <small>使用模板批量生成报告</small>
        </div>
      </div>
      <nav class="center-navigation">
        <router-link
          v-for="item in navigation"
          :key="item.to"
          :to="item.to"
          class="center-navigation-item"
        >
          <strong>{{ item.label }}</strong>
          <span>{{ item.description }}</span>
        </router-link>
      </nav>
      <div class="center-guide">
        <strong>生成流程</strong>
        <span>模板 → 年度 → 范围 → 输出 → 检查</span>
        <span>样例 → 批量 → 结果 → 记录</span>
      </div>
    </aside>

    <section class="center-content">
      <div class="center-breadcrumb" aria-label="面包屑">
        <router-link to="/">首页</router-link>
        <span>/</span>
        <router-link to="/report-center/jobs">报告生成中心</router-link>
        <span>/</span>
        <strong>{{ currentLabel }}</strong>
      </div>
      <router-view />
    </section>
  </div>
</template>

<style scoped>
.center-layout {
  display: grid;
  grid-template-columns: 232px minmax(0, 1fr);
  min-height: calc(100vh - 58px);
}

.center-sidebar {
  display: flex;
  flex-direction: column;
  gap: 22px;
  padding: 24px 18px;
  border-right: 1px solid #d9eadc;
  background: #f8fdf9;
}

.center-identity {
  display: flex;
  gap: 12px;
  align-items: center;
}

.center-index {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 12px;
  background: #238b57;
  color: #fff;
  font-size: 12px;
  font-weight: 800;
}

.center-identity strong,
.center-identity small {
  display: block;
}

.center-identity strong {
  color: #17251d;
  font-size: 15px;
}

.center-identity small {
  margin-top: 2px;
  color: #667085;
  font-size: 11px;
}

.center-navigation {
  display: grid;
  gap: 6px;
}

.center-navigation-item {
  display: grid;
  gap: 2px;
  padding: 11px 12px;
  border: 1px solid transparent;
  border-radius: 9px;
  color: #344054;
  text-decoration: none;
}

.center-navigation-item span {
  color: #7b8799;
  font-size: 11px;
}

.center-navigation-item:hover {
  border-color: #d5e6d9;
  background: #fff;
}

.center-navigation-item.router-link-active {
  border-color: #bfe0c8;
  background: #e9f7ed;
  color: #177345;
}

.center-guide {
  display: grid;
  gap: 5px;
  margin-top: auto;
  padding: 13px;
  border: 1px solid #d9eadc;
  border-radius: 10px;
  background: #fff;
  color: #667085;
  font-size: 10px;
}

.center-guide strong {
  color: #344054;
  font-size: 11px;
}

.center-content {
  min-width: 0;
  overflow: auto;
}

.center-breadcrumb {
  display: flex;
  gap: 7px;
  align-items: center;
  height: 38px;
  padding: 0 24px;
  border-bottom: 1px solid #e4e9f1;
  background: #fff;
  color: #98a2b3;
  font-size: 11px;
}

.center-breadcrumb a {
  color: #667085;
  text-decoration: none;
}

.center-breadcrumb strong {
  color: #344054;
}
</style>
