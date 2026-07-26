import { createRouter, createWebHashHistory } from "vue-router";

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    {
      path: "/",
      redirect: "/projects",
    },
    {
      path: "/projects",
      name: "projects",
      component: () => import("../views/ProjectListView.vue"),
    },
    {
      path: "/projects/:projectId",
      name: "project-detail",
      component: () => import("../views/ProjectDetailView.vue"),
    },
    {
      path: "/templates",
      name: "templates",
      component: () => import("../views/TemplateListView.vue"),
    },
    {
      path: "/templates/:templateId",
      name: "template-detail",
      component: () => import("../views/TemplateDetailView.vue"),
    },
    {
      path: "/workspace",
      name: "workspace",
      component: () => import("../views/WorkspaceView.vue"),
    },
  ],
});

export default router;
