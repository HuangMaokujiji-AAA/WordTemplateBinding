<script setup lang="ts">
import { computed, ref } from "vue";
import {
  getOrCreateBindingSet,
  getTemplateStudioWorkspace,
  validateBindingSet,
} from "../../../api/client";
import type { TemplateStudioWorkspace } from "../../../api/types";
import type {
  TemplateStudioContext,
  TemplateStudioContextPatch,
} from "../types";

interface ValidationItem {
  code: string;
  level: string;
  message: string;
}

interface ValidationResult {
  status: string;
  summary: Record<string, number>;
  items: ValidationItem[];
}

const props = defineProps<{ context: TemplateStudioContext }>();
const emit = defineEmits<{
  "update-context": [patch: TemplateStudioContextPatch];
  complete: [];
  "go-step": [step: number];
}>();

const result = ref<ValidationResult | null>(null);
const workspace = ref<TemplateStudioWorkspace | null>(null);
const loading = ref(false);
const message = ref("");

const groupedIssues = computed(() => {
  const groups = new Map<string, ValidationItem[]>();
  for (const item of result.value?.items || []) {
    const key = item.level.toUpperCase();
    groups.set(key, [...(groups.get(key) || []), item]);
  }
  return [...groups.entries()];
});

const canValidate = computed(
  () =>
    Boolean(props.context.templateId) &&
    Boolean(props.context.versionId) &&
    Boolean(props.context.chapterId)
);

const hasErrors = computed(
  () =>
    (result.value?.items || []).some(
      (item) => item.level.toUpperCase() === "ERROR"
    )
);

async function validate(): Promise<void> {
  if (!canValidate.value) return;
  loading.value = true;
  message.value = "";
  try {
    const bindingSet = await getOrCreateBindingSet(
      props.context.chapterId,
      props.context.versionId
    );
    [result.value, workspace.value] = await Promise.all([
      validateBindingSet(bindingSet.id),
      getTemplateStudioWorkspace(props.context.templateId, {
        versionId: props.context.versionId,
        bindingSetId: bindingSet.id,
      }),
    ]);
    message.value = hasErrors.value
      ? "校验发现阻断问题，请先修复后再测试。"
      : "校验完成，当前模板可以进入测试。";
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "模板校验失败。";
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>检查与校验</h2>
        <p>对当前模板版本和章节绑定集执行完整性、必填项与字段可用性检查，并把问题归到可修复的步骤。</p>
      </div>
      <button
        type="button"
        class="studio-button primary"
        :disabled="loading || !canValidate"
        @click="validate"
      >
        {{ loading ? "正在校验…" : "运行校验" }}
      </button>
    </header>

    <div class="studio-step-body">
      <div v-if="!canValidate" class="studio-empty">
        请先在第 5 步选择项目章节和数据源，再完成字段绑定。
      </div>
      <template v-else>
        <div v-if="workspace" class="studio-metrics">
          <div class="studio-metric">
            <strong>{{ workspace.summary.elementCount }}</strong>
            <span>动态元素</span>
          </div>
          <div class="studio-metric">
            <strong>{{ workspace.summary.boundElementCount }}</strong>
            <span>已绑定</span>
          </div>
          <div class="studio-metric">
            <strong>{{ workspace.summary.requiredMissingCount }}</strong>
            <span>必填缺失</span>
          </div>
          <div class="studio-metric">
            <strong>{{ result?.items.length || 0 }}</strong>
            <span>校验问题</span>
          </div>
        </div>

        <div v-if="result" class="validation-result">
          <div class="validation-status" :class="{ blocked: hasErrors }">
            <strong>{{ result.status }}</strong>
            <span>{{ hasErrors ? "存在阻断问题" : "可以进入模板测试" }}</span>
          </div>
          <section v-for="[level, items] in groupedIssues" :key="level">
            <h3>{{ level }} · {{ items.length }}</h3>
            <article v-for="(item, index) in items" :key="`${item.code}-${index}`">
              <code>{{ item.code }}</code>
              <span>{{ item.message }}</span>
              <button type="button" @click="emit('go-step', 6)">去修复</button>
            </article>
          </section>
          <div v-if="result.items.length === 0" class="studio-empty">
            未发现校验问题。
          </div>
        </div>

        <div v-if="message" class="studio-message" :class="{ error: hasErrors }">
          {{ message }}
        </div>
        <div class="studio-actions">
          <button type="button" class="studio-button" @click="emit('go-step', 6)">
            返回绑定
          </button>
          <button
            type="button"
            class="studio-button primary"
            :disabled="!result || hasErrors"
            @click="emit('complete')"
          >
            校验通过，进入测试
          </button>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.validation-result {
  display: grid;
  gap: 13px;
  margin-top: 16px;
}

.validation-status {
  display: flex;
  justify-content: space-between;
  padding: 13px 15px;
  border: 1px solid #b9dfcc;
  border-radius: 9px;
  background: #f1fbf5;
  color: #16805c;
}

.validation-status.blocked {
  border-color: #fecaca;
  background: #fef2f2;
  color: #b42318;
}

.validation-result section {
  overflow: hidden;
  border: 1px solid #e0e6ef;
  border-radius: 9px;
}

.validation-result h3 {
  margin: 0;
  padding: 9px 13px;
  background: #f6f8fb;
  color: #475467;
  font-size: 11px;
}

.validation-result article {
  display: grid;
  grid-template-columns: 140px minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  padding: 10px 13px;
  border-top: 1px solid #edf0f5;
  font-size: 11px;
}

.validation-result article button {
  border: 0;
  background: none;
  color: #3157d5;
  cursor: pointer;
}
</style>
