<script setup lang="ts">
import {
  computed,
  defineAsyncComponent,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
} from "vue";
import { onBeforeRouteLeave, useRoute, useRouter } from "vue-router";
import type {
  TemplateStudioContext,
  TemplateStudioContextPatch,
} from "./types";
import "./template-studio.css";

const route = useRoute();
const router = useRouter();
const unsavedBoundaryDrafts = ref(false);
const RESUME_KEY = "wtb.template-studio.resume.v1";

const steps = [
  {
    id: 1,
    label: "创建模板",
    shortLabel: "创建",
    component: defineAsyncComponent(
      () => import("./steps/CreateTemplateStep.vue")
    ),
  },
  {
    id: 2,
    label: "确认报告结构",
    shortLabel: "结构",
    component: defineAsyncComponent(
      () => import("./steps/ConfirmStructureStep.vue")
    ),
  },
  {
    id: 3,
    label: "标记动态内容",
    shortLabel: "标记",
    component: defineAsyncComponent(
      () => import("./steps/MarkDynamicContentStep.vue")
    ),
  },
  {
    id: 4,
    label: "设置区块规则",
    shortLabel: "规则",
    component: defineAsyncComponent(
      () => import("./steps/ConfigureRulesStep.vue")
    ),
  },
  {
    id: 5,
    label: "连接数据源",
    shortLabel: "数据",
    component: defineAsyncComponent(
      () => import("./steps/ConnectDataSourceStep.vue")
    ),
  },
  {
    id: 6,
    label: "拖拽绑定字段",
    shortLabel: "绑定",
    component: defineAsyncComponent(
      () => import("./steps/BindFieldsStep.vue")
    ),
  },
  {
    id: 7,
    label: "检查与校验",
    shortLabel: "校验",
    component: defineAsyncComponent(
      () => import("./steps/ValidateTemplateStep.vue")
    ),
  },
  {
    id: 8,
    label: "测试模板",
    shortLabel: "测试",
    component: defineAsyncComponent(
      () => import("./steps/TestTemplateStep.vue")
    ),
  },
  {
    id: 9,
    label: "发布模板",
    shortLabel: "发布",
    component: defineAsyncComponent(
      () => import("./steps/PublishTemplateStep.vue")
    ),
  },
] as const;

const activeStepId = computed(() => {
  const parsed = Number(route.query.step || 0);
  if (parsed >= 1 && parsed <= 9) return parsed;
  return route.query.templateId ? 2 : 1;
});

const activeStep = computed(
  () => steps.find((step) => step.id === activeStepId.value) || steps[0]
);

const context = computed<TemplateStudioContext>(() => ({
  templateId: stringQuery("templateId"),
  versionId: stringQuery("versionId"),
  projectId: stringQuery("projectId"),
  chapterId: stringQuery("chapterId"),
  dataSourceId: stringQuery("dataSourceId"),
  segmentId: stringQuery("segmentId"),
}));

const activeComponentProps = computed(() =>
  activeStepId.value === 6 ? {} : { context: context.value }
);

function stringQuery(key: keyof TemplateStudioContext): string {
  const value = route.query[key];
  return typeof value === "string" ? value : "";
}

async function patchQuery(
  patch: Record<string, string | number | undefined>
): Promise<void> {
  const query = { ...route.query };
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined || value === "") {
      delete query[key];
    } else {
      query[key] = String(value);
    }
  }
  await router.replace({ query });
}

function goToStep(stepId: number): void {
  if (stepId > 1 && !context.value.templateId) return;
  if (
    unsavedBoundaryDrafts.value &&
    stepId !== activeStepId.value &&
    !window.confirm("存在尚未保存的片段边界，确定离开当前步骤吗？")
  ) {
    return;
  }
  void patchQuery({ step: stepId });
}

function updateContext(patch: TemplateStudioContextPatch): void {
  void patchQuery(patch);
}

function completeStep(patch?: TemplateStudioContextPatch): void {
  if (patch) updateContext(patch);
  const next = Math.min(9, activeStepId.value + 1);
  void patchQuery({ ...patch, step: next });
}

function setDirty(value: boolean): void {
  unsavedBoundaryDrafts.value = value;
}

function beforeUnload(event: BeforeUnloadEvent): void {
  if (!unsavedBoundaryDrafts.value) return;
  event.preventDefault();
  event.returnValue = "";
}

onBeforeRouteLeave(() => {
  if (!unsavedBoundaryDrafts.value) return true;
  return window.confirm("存在尚未保存的片段边界，确定离开制作工作台吗？");
});

watch(
  () => route.query,
  () => {
    if (!context.value.templateId) return;
    localStorage.setItem(
      RESUME_KEY,
      JSON.stringify({
        ...context.value,
        step: activeStepId.value,
        updatedAt: new Date().toISOString(),
      })
    );
  },
  { deep: true }
);

onMounted(() => {
  window.addEventListener("beforeunload", beforeUnload);
  if (route.query.templateId) return;
  try {
    const saved = JSON.parse(localStorage.getItem(RESUME_KEY) || "null") as
      | (TemplateStudioContext & { step?: number })
      | null;
    if (saved?.templateId) {
      void patchQuery({
        ...saved,
        step: saved.step || 2,
        updatedAt: undefined,
      });
    }
  } catch {
    localStorage.removeItem(RESUME_KEY);
  }
});

onBeforeUnmount(() => {
  window.removeEventListener("beforeunload", beforeUnload);
});
</script>

<template>
  <div class="template-studio-view">
    <header class="studio-header">
      <div>
        <span class="studio-kicker">模板制作工作台</span>
        <h1>{{ activeStep.label }}</h1>
        <p>
          模板 {{ context.templateId || "尚未创建" }} · 版本
          {{ context.versionId || "自动选择当前版本" }}
        </p>
      </div>
      <router-link class="studio-library-link" to="/template-center/templates">
        返回模板库
      </router-link>
    </header>

    <nav class="studio-steps" aria-label="模板制作步骤">
      <button
        v-for="step in steps"
        :key="step.id"
        type="button"
        class="studio-step"
        :class="{
          active: step.id === activeStepId,
          complete: Boolean(context.templateId) && step.id < activeStepId,
        }"
        :disabled="step.id > 1 && !context.templateId"
        @click="goToStep(step.id)"
      >
        <span>{{ step.id }}</span>
        <strong>{{ step.shortLabel }}</strong>
      </button>
    </nav>

    <main class="studio-content">
      <Suspense>
        <component
          :is="activeStep.component"
          v-bind="activeComponentProps"
          @update-context="updateContext"
          @complete="completeStep"
          @dirty-change="setDirty"
          @go-step="goToStep"
        />
        <template #fallback>
          <div class="studio-loading">正在加载当前步骤…</div>
        </template>
      </Suspense>
    </main>
  </div>
</template>

<style scoped>
.template-studio-view {
  min-height: calc(100vh - 96px);
  background: #f3f6fa;
}

.studio-header {
  display: flex;
  justify-content: space-between;
  gap: 24px;
  align-items: center;
  padding: 22px 28px 16px;
  background: #fff;
}

.studio-kicker {
  color: #3157d5;
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.12em;
}

.studio-header h1 {
  margin: 4px 0 2px;
  color: #172033;
  font-size: 23px;
}

.studio-header p {
  margin: 0;
  color: #7b8799;
  font-size: 11px;
}

.studio-library-link {
  padding: 8px 12px;
  border: 1px solid #d4dce8;
  border-radius: 8px;
  background: #fff;
  color: #475467;
  text-decoration: none;
  font-size: 11px;
}

.studio-steps {
  display: grid;
  grid-template-columns: repeat(9, minmax(78px, 1fr));
  gap: 1px;
  overflow-x: auto;
  padding: 0 28px 18px;
  background: #fff;
}

.studio-step {
  display: flex;
  gap: 7px;
  align-items: center;
  justify-content: center;
  padding: 9px 5px;
  border: 0;
  border-bottom: 3px solid transparent;
  background: #f8fafc;
  color: #7b8799;
  cursor: pointer;
}

.studio-step span {
  display: grid;
  width: 19px;
  height: 19px;
  place-items: center;
  border-radius: 50%;
  background: #e5eaf2;
  font-size: 9px;
}

.studio-step strong {
  font-size: 11px;
}

.studio-step.active {
  border-bottom-color: #3157d5;
  background: #eef2ff;
  color: #2949b8;
}

.studio-step.active span {
  background: #3157d5;
  color: #fff;
}

.studio-step.complete span {
  background: #dff5e8;
  color: #16805c;
}

.studio-content {
  padding: 22px 28px 34px;
}

.studio-loading {
  display: grid;
  min-height: 300px;
  place-items: center;
  color: #7b8799;
}

@media (max-width: 900px) {
  .studio-header {
    align-items: flex-start;
    padding: 18px;
  }

  .studio-steps {
    padding: 0 18px 14px;
  }

  .studio-content {
    padding: 18px;
  }
}

@media (max-width: 560px) {
  .studio-header {
    align-items: stretch;
    flex-direction: column;
  }

  .studio-library-link {
    width: fit-content;
  }
}
</style>
