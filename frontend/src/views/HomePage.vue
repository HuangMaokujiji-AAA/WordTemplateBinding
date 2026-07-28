<script setup lang="ts">
const templateSteps = [
  "创建模板",
  "确认结构",
  "标记动态内容",
  "设置区块规则",
  "连接数据源",
  "绑定字段",
  "检查校验",
  "测试模板",
  "发布模板",
];

const reportSteps = [
  "选择模板",
  "选择年度",
  "选择范围",
  "设置输出",
  "生成前检查",
  "生成样例",
  "批量生成",
  "生成结果",
  "生成记录",
];
</script>

<template>
  <div class="home-page">
    <section class="hero">
      <span class="eyebrow">WORD TEMPLATE BINDING</span>
      <h1>今天要完成哪项任务？</h1>
      <p>
        系统围绕两条核心旅程组织。先制作并发布可复用模板，再使用固定版本批量生成报告。
      </p>
    </section>

    <section class="task-grid" aria-label="核心任务">
      <article class="task-card template-task">
        <div class="task-heading">
          <span class="task-number">01</span>
          <div>
            <small>模板制作中心</small>
            <h2>制作可复用报告模板</h2>
          </div>
        </div>
        <p>
          上传 Word 报告，确认结构并标记动态内容，连接数据、完成绑定和校验后发布。
        </p>
        <ol class="step-list">
          <li v-for="(step, index) in templateSteps" :key="step">
            <span>{{ index + 1 }}</span>{{ step }}
          </li>
        </ol>
        <div class="task-actions">
          <router-link class="primary-action" to="/template-center/templates">
            进入模板制作中心
          </router-link>
          <router-link class="secondary-action" to="/template-center/studio">
            打开制作工作台
          </router-link>
        </div>
      </article>

      <article class="task-card report-task">
        <div class="task-heading">
          <span class="task-number">02</span>
          <div>
            <small>报告生成中心</small>
            <h2>使用模板批量生成报告</h2>
          </div>
        </div>
        <p>
          选择已发布模板和目标范围，完成生成前检查与样例确认，再执行批量任务。
        </p>
        <ol class="step-list">
          <li v-for="(step, index) in reportSteps" :key="step">
            <span>{{ index + 1 }}</span>{{ step }}
          </li>
        </ol>
        <div class="task-actions">
          <router-link class="primary-action" to="/report-center/new">
            新建生成任务
          </router-link>
          <router-link class="secondary-action" to="/report-center/jobs">
            查看生成任务
          </router-link>
        </div>
      </article>
    </section>

    <section class="home-principles">
      <div>
        <strong>版本可追溯</strong>
        <span>模板、绑定和生成参数都有明确版本。</span>
      </div>
      <div>
        <strong>过程可恢复</strong>
        <span>每一步都有状态，返回后可以继续。</span>
      </div>
      <div>
        <strong>结果可验证</strong>
        <span>先检查和生成样例，再开始批量任务。</span>
      </div>
    </section>
  </div>
</template>

<style scoped>
.home-page {
  min-height: calc(100vh - 58px);
  padding: 58px clamp(32px, 6vw, 92px) 46px;
  background:
    radial-gradient(circle at 16% 3%, rgb(49 87 213 / 12%), transparent 30%),
    radial-gradient(circle at 86% 12%, rgb(35 139 87 / 11%), transparent 28%),
    #f5f8fc;
}

.hero {
  max-width: 760px;
  margin: 0 auto 34px;
  text-align: center;
}

.eyebrow {
  color: #3157d5;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.16em;
}

.hero h1 {
  margin: 10px 0 9px;
  color: #172033;
  font-size: 34px;
  letter-spacing: -0.03em;
}

.hero p {
  margin: 0;
  color: #667085;
  font-size: 15px;
}

.task-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 22px;
  max-width: 1180px;
  margin: 0 auto;
}

.task-card {
  padding: 27px;
  border: 1px solid #dbe3ef;
  border-radius: 18px;
  background: rgb(255 255 255 / 94%);
  box-shadow: 0 16px 42px rgb(25 39 67 / 7%);
}

.template-task {
  border-top: 4px solid #3157d5;
}

.report-task {
  border-top: 4px solid #238b57;
}

.task-heading {
  display: flex;
  gap: 13px;
  align-items: center;
}

.task-number {
  display: grid;
  width: 44px;
  height: 44px;
  flex: 0 0 auto;
  place-items: center;
  border-radius: 13px;
  background: #edf1ff;
  color: #3157d5;
  font-size: 12px;
  font-weight: 800;
}

.report-task .task-number {
  background: #eaf7ee;
  color: #238b57;
}

.task-heading small {
  color: #7b8799;
  font-size: 11px;
}

.task-heading h2 {
  margin: 2px 0 0;
  color: #1f2a3d;
  font-size: 20px;
}

.task-card > p {
  min-height: 44px;
  margin: 18px 0;
  color: #667085;
  font-size: 13px;
}

.step-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.step-list li {
  display: flex;
  gap: 7px;
  align-items: center;
  padding: 8px;
  border-radius: 7px;
  background: #f7f9fc;
  color: #475467;
  font-size: 11px;
}

.step-list li span {
  display: grid;
  width: 18px;
  height: 18px;
  place-items: center;
  border-radius: 50%;
  background: #e7ecf7;
  color: #52627a;
  font-size: 9px;
  font-weight: 700;
}

.task-actions {
  display: flex;
  gap: 9px;
  margin-top: 22px;
}

.primary-action,
.secondary-action {
  padding: 9px 14px;
  border-radius: 8px;
  text-decoration: none;
  font-size: 12px;
  font-weight: 650;
}

.primary-action {
  background: #3157d5;
  color: #fff;
}

.report-task .primary-action {
  background: #238b57;
}

.secondary-action {
  border: 1px solid #d1d9e6;
  background: #fff;
  color: #475467;
}

.home-principles {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1px;
  max-width: 920px;
  margin: 26px auto 0;
  overflow: hidden;
  border: 1px solid #dbe3ef;
  border-radius: 12px;
  background: #dbe3ef;
}

.home-principles div {
  display: grid;
  gap: 3px;
  padding: 14px 18px;
  background: #fff;
}

.home-principles strong {
  color: #344054;
  font-size: 12px;
}

.home-principles span {
  color: #7b8799;
  font-size: 10px;
}
</style>
