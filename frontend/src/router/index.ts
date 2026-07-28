import { createRouter, createWebHashHistory } from "vue-router";

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    {
      path: "/",
      name: "home",
      component: () => import("../views/HomePage.vue"),
    },
    {
      path: "/template-center",
      component: () => import("../layouts/TemplateCenterLayout.vue"),
      redirect: "/template-center/templates",
      children: [
        {
          path: "templates",
          name: "template-center-library",
          component: () => import("../views/TemplateListView.vue"),
          meta: { breadcrumb: "模板库" },
        },
        {
          path: "templates/:templateId",
          name: "template-center-detail",
          component: () => import("../views/TemplateDetailView.vue"),
          meta: { breadcrumb: "模板详情" },
        },
        {
          path: "studio",
          name: "template-center-studio",
          component: () =>
            import("../features/template-studio/TemplateStudioView.vue"),
          meta: { breadcrumb: "制作工作台" },
        },
      ],
    },
    {
      path: "/report-center",
      component: () => import("../layouts/ReportCenterLayout.vue"),
      redirect: "/report-center/jobs",
      children: [
        {
          path: "jobs",
          name: "report-center-jobs",
          component: () => import("../views/ReportCenterPlaceholderView.vue"),
          meta: {
            breadcrumb: "生成任务",
            pageTitle: "生成任务",
            pageDescription: "集中查看批量任务进度、成功数量和失败原因。",
          },
        },
        {
          path: "new",
          name: "report-center-new",
          component: () => import("../views/ReportCenterPlaceholderView.vue"),
          meta: {
            breadcrumb: "新建生成任务",
            pageTitle: "新建生成任务",
            pageDescription: "从已发布模板开始，按九步向导配置一批报告。",
            primaryAction: "先检查可用模板",
          },
        },
        {
          path: "history",
          name: "report-center-history",
          component: () => import("../views/ReportCenterPlaceholderView.vue"),
          meta: {
            breadcrumb: "生成记录",
            pageTitle: "生成记录",
            pageDescription: "查询历史任务参数、模板版本、结果和下载产物。",
          },
        },
      ],
    },
    {
      path: "/projects",
      name: "legacy-projects",
      component: () => import("../views/ProjectListView.vue"),
    },
    {
      path: "/projects/:projectId",
      name: "legacy-project-detail",
      component: () => import("../views/ProjectDetailView.vue"),
    },
    {
      path: "/templates",
      name: "legacy-templates",
      component: () => import("../views/TemplateListView.vue"),
    },
    {
      path: "/templates/:templateId",
      name: "legacy-template-detail",
      component: () => import("../views/TemplateDetailView.vue"),
    },
    {
      path: "/workspace",
      name: "legacy-workspace",
      component: () => import("../views/WorkspaceView.vue"),
    },
  ],
});

export default router;
