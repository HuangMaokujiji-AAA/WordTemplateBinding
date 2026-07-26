<script setup lang="ts">
import { useRoute } from "vue-router";

const route = useRoute();

const navItems = [
  { to: "/projects", label: "项目管理" },
  { to: "/templates", label: "模板管理" },
  { to: "/workspace", label: "模板绑定" },
];
</script>

<template>
  <div class="app-shell">
    <header class="app-navbar">
      <span class="navbar-brand">WordTemplateBinding</span>
      <nav class="navbar-links">
        <router-link
          v-for="item in navItems"
          :key="item.to"
          :to="item.to"
          class="navbar-link"
          :class="{ active: route.path.startsWith(item.to) }"
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
  height: 48px;
  background: #1a2332;
  color: #e0e6ed;
  flex-shrink: 0;
}

.navbar-brand {
  font-weight: 700;
  font-size: 15px;
  color: #fff;
  margin-right: 8px;
}

.navbar-links {
  display: flex;
  gap: 4px;
}

.navbar-link {
  color: #94a3b8;
  text-decoration: none;
  padding: 6px 14px;
  border-radius: 6px;
  font-size: 13px;
  transition: background 0.15s, color 0.15s;
}

.navbar-link:hover {
  background: rgba(255, 255, 255, 0.08);
  color: #e2e8f0;
}

.navbar-link.active {
  background: rgba(59, 130, 246, 0.25);
  color: #60a5fa;
  font-weight: 600;
}

.app-main {
  flex: 1;
  overflow: auto;
}
</style>
