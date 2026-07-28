<script setup lang="ts">
import { reactive, ref } from "vue";
import { uploadPersistentTemplate } from "../../../api/client";
import type { TemplateStudioContext } from "../types";

const props = defineProps<{
  context: TemplateStudioContext;
}>();

const emit = defineEmits<{
  complete: [patch: { templateId: string; versionId: string }];
}>();

const form = reactive({
  templateCode: `TPL_${Date.now()}`,
  templateName: "",
  templateType: "SECTION",
  categoryCode: "",
  description: "",
});
const file = ref<File | null>(null);
const uploading = ref(false);
const message = ref("");
const isError = ref(false);

function selectFile(event: Event): void {
  const input = event.target as HTMLInputElement;
  file.value = input.files?.[0] || null;
  if (file.value && !form.templateName) {
    form.templateName = file.value.name.replace(/\.docx$/i, "");
  }
}

async function createTemplate(): Promise<void> {
  if (
    !file.value ||
    !form.templateCode.trim() ||
    !form.templateName.trim()
  ) {
    message.value = "请填写模板编码和名称，并选择 DOCX 文件。";
    isError.value = true;
    return;
  }

  uploading.value = true;
  message.value = "正在上传 DOCX 并扫描模板元素…";
  isError.value = false;
  try {
    const result = await uploadPersistentTemplate(file.value, null, {
      templateCode: form.templateCode.trim(),
      templateName: form.templateName.trim(),
      templateType: form.templateType,
      categoryCode: form.categoryCode.trim() || null,
      description: form.description.trim() || null,
    });
    message.value = `模板 v${result.version.versionNo} 已创建并完成扫描。`;
    emit("complete", {
      templateId: result.template.id,
      versionId: result.version.id,
    });
  } catch (error) {
    message.value =
      error instanceof Error ? error.message : "创建模板失败。";
    isError.value = true;
  } finally {
    uploading.value = false;
  }
}
</script>

<template>
  <section class="studio-step-card">
    <header class="studio-step-header">
      <div>
        <h2>创建模板</h2>
        <p>
          填写必要信息并上传 Word 报告。上传成功后会立即创建不可变版本并扫描文本、表格和图表。
        </p>
      </div>
    </header>
    <div class="studio-step-body">
      <div
        v-if="context.templateId"
        class="existing-template"
      >
        <div>
          <strong>当前工作台已经选择模板 {{ context.templateId }}</strong>
          <span>可以继续使用当前版本，也可以在下方创建另一份模板。</span>
        </div>
        <button
          type="button"
          class="studio-button"
          @click="emit('complete', {
            templateId: context.templateId,
            versionId: context.versionId,
          })"
        >
          继续当前模板
        </button>
      </div>

      <div class="studio-form-grid">
        <label class="studio-field">
          <span>模板编码</span>
          <input
            v-model="form.templateCode"
            maxlength="64"
            placeholder="例如：ANNUAL_REPORT"
          />
        </label>
        <label class="studio-field">
          <span>模板名称</span>
          <input
            v-model="form.templateName"
            maxlength="255"
            placeholder="例如：年度质量报告"
          />
        </label>
        <label class="studio-field">
          <span>模板类型</span>
          <select v-model="form.templateType">
            <option value="SECTION">章节模板</option>
            <option value="MASTER">主模板</option>
            <option value="COMPONENT">组件模板</option>
          </select>
        </label>
        <label class="studio-field">
          <span>分类编码</span>
          <input
            v-model="form.categoryCode"
            maxlength="64"
            placeholder="可选"
          />
        </label>
        <label class="studio-field full">
          <span>简要说明</span>
          <textarea
            v-model="form.description"
            rows="3"
            maxlength="1000"
            placeholder="说明模板适用范围和数据要求"
          ></textarea>
        </label>
        <label class="studio-field full">
          <span>DOCX 文件</span>
          <input
            type="file"
            accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            @change="selectFile"
          />
        </label>
      </div>

      <div class="studio-actions">
        <button
          type="button"
          class="studio-button primary"
          :disabled="uploading"
          @click="createTemplate"
        >
          {{ uploading ? "正在创建…" : "创建模板并扫描" }}
        </button>
        <span v-if="file" class="selected-file">{{ file.name }}</span>
      </div>
      <div
        v-if="message"
        class="studio-message"
        :class="{ error: isError }"
      >
        {{ message }}
      </div>
    </div>
  </section>
</template>

<style scoped>
.existing-template {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  align-items: center;
  margin-bottom: 20px;
  padding: 13px 15px;
  border: 1px solid #cdd8fb;
  border-radius: 9px;
  background: #f2f5ff;
}

.existing-template strong,
.existing-template span {
  display: block;
}

.existing-template strong {
  color: #2949b8;
  font-size: 12px;
}

.existing-template span,
.selected-file {
  margin-top: 3px;
  color: #667085;
  font-size: 11px;
}
</style>
