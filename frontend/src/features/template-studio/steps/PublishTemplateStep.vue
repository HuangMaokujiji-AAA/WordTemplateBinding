<script setup lang="ts">
import { onMounted, ref } from "vue";
import { listPublishedTemplates } from "../../../api/client";
import type { PublishedTemplateList } from "../../../api/types";
import type { TemplateStudioContext } from "../types";

defineProps<{ context: TemplateStudioContext }>();
const emit = defineEmits<{ "go-step": [step: number] }>();

const releases = ref<PublishedTemplateList | null>(null);
const loading = ref(false);
const message = ref("");

async function load(): Promise<void> {
  loading.value = true;
  try {
    releases.value = await listPublishedTemplates();
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "加载发布能力失败。";
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
        <h2>发布模板</h2>
        <p>只有完成发布门禁并产生发布记录的不可变版本，才会出现在报告生成中心。</p>
      </div>
    </header>

    <div class="studio-step-body">
      <div class="release-panel">
        <div class="release-icon">9</div>
        <div>
          <span>阶段 4 发布闭环</span>
          <h3>{{ releases?.publishingAvailable ? "发布能力已启用" : "发布能力尚未启用" }}</h3>
          <p>
            {{
              releases?.message ||
              message ||
              "正在读取发布能力…"
            }}
          </p>
        </div>
      </div>

      <div class="studio-message">
        当前 READY 或校验通过状态不等于“已发布”。本阶段不会把草稿版本暴露给报告生成中心。
      </div>
      <div class="studio-actions">
        <button type="button" class="studio-button" @click="emit('go-step', 8)">
          返回模板测试
        </button>
        <router-link class="studio-button" to="/template-center/templates">
          返回模板库
        </router-link>
      </div>
    </div>
  </section>
</template>

<style scoped>
.release-panel {
  display: flex;
  gap: 16px;
  align-items: center;
  padding: 22px;
  border: 1px solid #dfe6f0;
  border-radius: 11px;
  background: #f9fbfd;
}

.release-icon {
  display: grid;
  width: 44px;
  height: 44px;
  flex: 0 0 auto;
  place-items: center;
  border-radius: 50%;
  background: #e9eeff;
  color: #3157d5;
  font-weight: 800;
}

.release-panel span {
  color: #7b8799;
  font-size: 10px;
}

.release-panel h3 {
  margin: 5px 0;
  color: #1e293b;
}

.release-panel p {
  margin: 0;
  color: #667085;
  font-size: 12px;
}

.studio-actions a {
  text-decoration: none;
}
</style>
