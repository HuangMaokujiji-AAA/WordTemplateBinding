<script setup lang="ts">
import { computed, ref } from "vue";
import {
  downloadBindingSetReport,
  getOrCreateBindingSet,
  validateBindingSet,
} from "../../../api/client";
import type { TemplateStudioContext } from "../types";

const props = defineProps<{ context: TemplateStudioContext }>();
const emit = defineEmits<{
  complete: [];
  "go-step": [step: number];
}>();

const loading = ref(false);
const message = ref("");
const generatedFile = ref("");
const validationStatus = ref("");

const canGenerate = computed(
  () => Boolean(props.context.versionId && props.context.chapterId)
);

async function generateSample(): Promise<void> {
  if (!canGenerate.value) return;
  loading.value = true;
  generatedFile.value = "";
  message.value = "正在执行生成前校验…";
  try {
    const bindingSet = await getOrCreateBindingSet(
      props.context.chapterId,
      props.context.versionId
    );
    const validation = await validateBindingSet(bindingSet.id);
    validationStatus.value = validation.status;
    const errors = validation.items.filter(
      (item) => item.level.toUpperCase() === "ERROR"
    );
    if (errors.length > 0) {
      message.value = `存在 ${errors.length} 个阻断问题，请返回第 7 步查看。`;
      return;
    }
    generatedFile.value = await downloadBindingSetReport(bindingSet.id);
    message.value = `样例报告已生成并下载：${generatedFile.value}`;
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "生成样例报告失败。";
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>测试模板</h2>
        <p>使用当前模板版本、章节绑定和数据快照生成一份真实 DOCX，验证文字、表格、原生图表和边界数据。</p>
      </div>
    </header>

    <div class="studio-step-body">
      <div v-if="!canGenerate" class="studio-empty">
        当前工作台缺少模板版本或章节，请返回第 5 步补齐数据上下文。
      </div>
      <template v-else>
        <div class="test-grid">
          <article>
            <span>测试输入</span>
            <strong>当前数据源快照</strong>
            <small>{{ context.dataSourceId || "未选择" }}</small>
          </article>
          <article>
            <span>模板版本</span>
            <strong>{{ context.versionId }}</strong>
            <small>生成时固定，不自动切换</small>
          </article>
          <article>
            <span>校验状态</span>
            <strong>{{ validationStatus || "尚未测试" }}</strong>
            <small>每次生成前重新校验</small>
          </article>
        </div>

        <div v-if="message" class="studio-message">{{ message }}</div>
        <div class="studio-actions">
          <button type="button" class="studio-button" @click="emit('go-step', 7)">
            返回校验
          </button>
          <button
            type="button"
            class="studio-button primary"
            :disabled="loading"
            @click="generateSample"
          >
            {{ loading ? "正在生成…" : "生成并下载样例 DOCX" }}
          </button>
          <button
            type="button"
            class="studio-button"
            :disabled="!generatedFile"
            @click="emit('complete')"
          >
            样例结果已确认
          </button>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.test-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.test-grid article {
  display: grid;
  gap: 7px;
  padding: 17px;
  border: 1px solid #e0e6ef;
  border-radius: 10px;
  background: #f9fbfd;
}

.test-grid span,
.test-grid small {
  color: #7b8799;
  font-size: 10px;
}

.test-grid strong {
  overflow: hidden;
  color: #1e293b;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
