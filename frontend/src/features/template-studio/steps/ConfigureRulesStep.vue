<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { getTemplateStudioWorkspace } from "../../../api/client";
import type { TemplateStudioWorkspace } from "../../../api/types";
import type { TemplateStudioContext } from "../types";

const props = defineProps<{ context: TemplateStudioContext }>();
const emit = defineEmits<{
  complete: [];
  "go-step": [step: number];
}>();

const workspace = ref<TemplateStudioWorkspace | null>(null);
const loading = ref(false);
const message = ref("");

const fixedSegments = computed(
  () =>
    workspace.value?.segments.filter(
      (segment) => segment.segmentType === "FIXED"
    ) || []
);

async function load(): Promise<void> {
  if (!props.context.templateId) return;
  loading.value = true;
  try {
    workspace.value = await getTemplateStudioWorkspace(
      props.context.templateId,
      { versionId: props.context.versionId || undefined }
    );
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "加载区块规则失败。";
  } finally {
    loading.value = false;
  }
}

onMounted(() => void load());
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>设置区块规则</h2>
        <p>
          先确认固定片段的生成行为。条件区块和重复区块将在高级规则阶段接入，不会把尚未保存的配置当成有效规则。
        </p>
      </div>
    </header>

    <div class="studio-step-body">
      <div v-if="!context.templateId" class="studio-empty">
        请先创建模板。
      </div>
      <template v-else>
        <div class="rule-grid">
          <article class="rule-card ready">
            <span>当前可用</span>
            <h3>固定区块</h3>
            <p>按文档顺序输出一次，适用于标题、正文、表格和图表所在的普通片段。</p>
            <strong>{{ fixedSegments.length }} 个片段</strong>
          </article>
          <article class="rule-card planned">
            <span>高级规则</span>
            <h3>条件区块</h3>
            <p>根据数据条件决定是否输出。规则编辑与持久化将在阶段 7 接入。</p>
            <strong>暂不可配置</strong>
          </article>
          <article class="rule-card planned">
            <span>高级规则</span>
            <h3>重复区块</h3>
            <p>按数据集合重复生成。规则编辑与持久化将在阶段 7 接入。</p>
            <strong>暂不可配置</strong>
          </article>
        </div>

        <div v-if="message" class="studio-message error">{{ message }}</div>
        <div class="studio-actions">
          <button
            type="button"
            class="studio-button"
            @click="emit('go-step', 2)"
          >
            返回调整片段
          </button>
          <button
            type="button"
            class="studio-button primary"
            :disabled="loading || !workspace"
            @click="emit('complete')"
          >
            固定区块规则已确认，继续
          </button>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.rule-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
}

.rule-card {
  min-height: 160px;
  padding: 18px;
  border: 1px solid #dfe6f0;
  border-radius: 11px;
}

.rule-card.ready {
  border-color: #b9dfcc;
  background: #f1fbf5;
}

.rule-card.planned {
  background: #f8fafc;
}

.rule-card span {
  color: #667085;
  font-size: 10px;
  font-weight: 750;
}

.rule-card h3 {
  margin: 8px 0;
  color: #1e293b;
}

.rule-card p {
  min-height: 54px;
  color: #667085;
  font-size: 12px;
  line-height: 1.6;
}

.rule-card strong {
  color: #3157d5;
  font-size: 12px;
}

@media (max-width: 900px) {
  .rule-grid {
    grid-template-columns: 1fr;
  }
}
</style>
