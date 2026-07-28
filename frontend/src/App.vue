<script setup lang="ts">
import { useRoute } from "vue-router";

const route = useRoute();

const navItems = [
  { to: "/", label: "首页", exact: true },
  { to: "/template-center/templates", label: "模板制作中心" },
  { to: "/report-center/jobs", label: "报告生成中心" },
];

function isActive(item: (typeof navItems)[number]): boolean {
  if (item.exact) return route.path === item.to;
  const centerPath = item.to.split("/").slice(0, 2).join("/");
  return route.path.startsWith(centerPath);
}
</script>

<template>
  <div class="app-shell">
    <header class="app-navbar">
      <router-link class="navbar-brand" to="/">
        <span class="brand-mark">W</span>
        <span>
          <strong>WordTemplateBinding</strong>
          <small>Word 模板数据绑定平台</small>
        </span>
      </router-link>
      <nav class="navbar-links">
        <router-link
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="navbar-link"
          :class="{ active: isActive(item) }"
        >
          {{ item.label }}
        </router-link>
      </nav>
    </header>
    <main class="app-main">
      <router-view />
    </main>
  </div>
</template>

<style scoped>
.app-navbar {
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 0 24px;
  height: 58px;
  border-bottom: 1px solid rgb(255 255 255 / 8%);
  background: #172033;
  color: #e0e6ed;
  flex-shrink: 0;
}

.navbar-brand {
  display: flex;
  gap: 10px;
  align-items: center;
  font-weight: 700;
  color: #fff;
  margin-right: 18px;
  text-decoration: none;
}

.brand-mark {
  display: grid;
  width: 30px;
  height: 30px;
  place-items: center;
  border-radius: 8px;
  background: linear-gradient(145deg, #4d72ea, #3157d5);
  font-size: 14px;
}

.navbar-brand strong,
.navbar-brand small {
  display: block;
}

.navbar-brand strong {
  font-size: 14px;
  line-height: 1.1;
}

.navbar-brand small {
  margin-top: 2px;
  color: #8f9bad;
  font-size: 9px;
  font-weight: 500;
}

.navbar-links {
  display: flex;
  gap: 4px;
}

.navbar-link {
  color: #94a3b8;
  text-decoration: none;
  padding: 7px 14px;
  border-radius: 6px;
  font-size: 13px;
  transition: background 0.15s, color 0.15s;
}

.navbar-link:hover {
  background: rgba(255, 255, 255, 0.08);
  color: #e2e8f0;
}

.navbar-link.active {
  background: rgb(96 133 250 / 18%);
  color: #8ba8ff;
  font-weight: 600;
}

.app-main {
  flex: 1;
  overflow: auto;
}
</style>
