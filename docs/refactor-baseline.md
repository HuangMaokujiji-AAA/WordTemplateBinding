# 两大中心改造基线

> 基线日期：2026-07-28  
> 基线分支：`HuangMao`  
> 基线提交：`0e02c0a`  
> 对应计划：`plan/04-two-center-project-refactoring-plan.md`  
> 状态：阶段 3 已完成

## 1. 基线目的

本文档记录“两大中心”改造开始前的代码结构、路由、HTTP API、数据库、自动化测试和主要页面行为。

后续每个阶段开始前更新“阶段记录”，每个阶段完成后记录构建、测试、截图和已知差异，避免把预期改造与意外回归混在一起。

## 2. 统一业务术语

| 术语 | 定义 |
|---|---|
| 模板制作中心 | 制作可复用报告模板的一级业务入口 |
| 报告生成中心 | 使用已发布模板批量生成报告的一级业务入口 |
| 模板 | 用户识别的逻辑模板，不等同于某个 DOCX 文件 |
| 模板版本 | 一份不可变 DOCX 及其解析结果 |
| 模板片段 | 完整模板中的逻辑绑定范围，不拆分正式源文件 |
| 模板绑定配置 | 模板可复用的标准字段映射，目标方案中不依赖具体项目章节 |
| 模板发布 | 将通过校验和测试的固定模板版本开放给报告生成中心 |
| 生成任务 | 固定模板、绑定、数据快照、目标范围和输出参数的一次异步执行 |
| 生成目标项 | 生成任务中一个“年度 × 学校/单位”的独立报告目标 |
| 产物 | 生成任务产生的 DOCX、PDF、ZIP、日志或预览文件 |

### 2.1 模板制作九步

1. 创建模板
2. 确认报告结构
3. 标记动态内容
4. 设置区块规则
5. 连接数据源
6. 拖拽绑定字段
7. 检查与校验
8. 测试模板
9. 发布模板

### 2.2 报告生成九步

1. 选择模板
2. 选择年度
3. 选择生成范围
4. 设置输出规则
5. 生成前检查
6. 生成样例
7. 批量生成
8. 生成结果
9. 生成记录

## 3. 运行环境

| 项目 | 基线值 |
|---|---|
| .NET SDK | 7.0.410 |
| Node.js | v25.4.0 |
| npm | 11.7.0 |
| 后端框架 | ASP.NET Core Minimal API / .NET 7 |
| Word 处理 | Open XML SDK 3.0.2 |
| 前端 | Vue 3.5 / Vue Router 4 / Vite 8 |
| 数据库 | MySQL 8 |
| 浏览器基线工具 | Playwright CLI |

项目 README 要求 Node.js 20+。当前基线环境是 Node.js 25；如出现只在此版本可复现的问题，需要在 Node.js 20 LTS 再验证。

## 4. 当前代码结构

### 4.1 解决方案

```text
WordTemplateBinding.Api             HTTP 适配、Minimal API、静态前端
WordTemplateBinding.Core            模型、接口和业务编排
WordTemplateBinding.Infrastructure  MySQL、文件存储和 OpenXML 实现
WordTemplateBinding.UnitTests
WordTemplateBinding.IntegrationTests
frontend                            Vue 3 前端
```

### 4.2 重点大文件

| 文件 | 基线行数 | 当前职责 |
|---|---:|---|
| `frontend/src/views/WorkspaceView.vue` | 1409 | 模板加载、片段、预览、绑定、校验、测试生成和 UI |
| `frontend/src/views/ProjectDetailView.vue` | 1141 | 项目、章节和开发数据源 |
| `frontend/src/views/TemplateDetailView.vue` | 1127 | 模板信息、版本、上传、下载和重扫 |
| `frontend/src/views/TemplateListView.vue` | 958 | 模板列表、上传和版本上传 |
| `frontend/src/api/client.ts` | 807 | 所有前端 HTTP 调用和部分 DTO 转换 |
| `frontend/src/api/types.ts` | 547 | 多个业务领域的前端类型 |
| `src/WordTemplateBinding.Core/Services/WorkspaceServices.cs` | 1699 | 项目章节、数据连接、数据源和绑定服务 |
| `src/WordTemplateBinding.Core/Services/TemplateCatalogService.cs` | 646 | 模板目录、版本、扫描和元素同步 |
| `src/WordTemplateBinding.Api/Endpoints/WorkspaceEndpoints.cs` | 501 | 项目、章节、连接、数据源和绑定端点 |
| `src/WordTemplateBinding.Core/Services/TemplateSegmentService.cs` | 421 | 片段查询、预览和边界编辑 |

阶段 1 先进行无行为变更的职责拆分，不以行数减少作为唯一目标。

## 5. 当前前端路由与导航

### 5.1 路由

| 路由 | 页面 | 当前用途 |
|---|---|---|
| `/` | 重定向 | 重定向到 `/projects` |
| `/projects` | `ProjectListView.vue` | 项目管理 |
| `/projects/:projectId` | `ProjectDetailView.vue` | 项目、章节和测试数据源 |
| `/templates` | `TemplateListView.vue` | 模板管理 |
| `/templates/:templateId` | `TemplateDetailView.vue` | 模板详情和版本 |
| `/workspace` | `WorkspaceView.vue` | 模板绑定工作区 |

### 5.2 一级导航

当前 `App.vue` 展示三个一级入口：

1. 项目管理
2. 模板管理
3. 模板绑定

阶段 2 的目标是替换为：首页、模板制作中心、报告生成中心，同时保留旧路由兼容入口。

## 6. 当前 HTTP API 基线

### 6.1 主程序始终启用

`Program.cs` 在 MySQL 和 InMemory 模式都映射：

- `PersistentTemplateEndpoints`
- `WorkspaceEndpoints`
- `DemoEndpoints`
- `DatabaseEndpoints`

主要路由组：

| 路由组 | 当前能力 |
|---|---|
| `/api/templates` | 模板列表、上传、详情、更新、归档、恢复和版本 |
| `/api/template-versions` | 版本详情、元素、片段、边界、文件和重扫 |
| `/api/template-segments` | 片段详情、元素和预览 |
| `/api/projects` | 项目和章节 |
| `/api/chapters` | 章节详情、更新和删除 |
| `/api/data-connections` | 连接、测试和元数据 |
| `/api/data-sources` | 数据源、快照、字段和结构 |
| `/api/binding-sets` | 绑定项、校验、候选、报告和复用模板 |
| `/api/template-elements` | 字段建议 |
| `/api/system/database/health` | 数据库健康检查 |
| `/api/demo` | 重复行、重复块和测试页面 |

### 6.2 仅 InMemory 模式启用

- `TemplateEndpoints`
- `BindingEndpoints`
- `DataSchemaEndpoints`
- `ReportEndpoints`
- `ChartAnalysisEndpoints`

这些端点属于旧演示或内存工作流，阶段 1～2 不删除。

### 6.3 尚未接入主程序

`BlueprintEndpoints.cs` 已定义报告蓝图端点，但当前 `Program.cs` 未调用 `MapBlueprintEndpoints()`。阶段 7 再决定接入或清理，不在阶段 0 改变行为。

完整端点清单应通过以下命令重新生成：

```bash
rg -n 'Map(Get|Post|Put|Patch|Delete)\(' src/WordTemplateBinding.Api/Endpoints
```

## 7. 当前数据库基线

### 7.1 基础表

`sql/report_platform_v2_schema.sql` 定义：

- 文件：`rp_file_object`
- 模板：`rp_template`、`rp_template_version`、`rp_template_element`
- 项目：`rp_project`、`rp_project_member`、`rp_project_context_version`
- 章节：`rp_chapter`、`rp_chapter_revision`、`rp_chapter_lock`
- 数据：`rp_data_connection`、`rp_data_source`、`rp_data_snapshot`、`rp_data_field`
- 绑定：`rp_binding_set`、`rp_binding_item`
- 生成：`rp_generation_job`、`rp_generation_job_snapshot`、`rp_generation_job_chapter`
- 产物与发布：`rp_artifact`、`rp_release`、`rp_release_chapter`
- 审计：`rp_audit_log`

### 7.2 已有迁移

| 迁移 | 主要内容 |
|---|---|
| `report_platform_v2_to_v2_1_database_file_storage_migration.sql` | 文件分片和上传会话 |
| `report_platform_v2_1_to_v2_2_template_segments_migration.sql` | 模板片段、元素片段归属和顺序 |
| `report_platform_v2_2_to_v2_3_component_and_blueprint_migration.sql` | 组件契约和报告蓝图 |

### 7.3 阶段 0 限制

阶段 0～3 不修改数据库结构。模板发布、模板绑定配置和生成目标项必须在阶段 4～5 通过独立迁移评审后实施。

## 8. 自动化测试基线

### 8.1 测试源

| 测试层 | 基线源文件情况 |
|---|---|
| 后端单元测试 | 14 个 `*Tests.cs` 文件 |
| 后端集成测试 | 2 个主要 `*Tests.cs` 文件 |
| xUnit 测试声明 | 152 个 `[Fact]`/`[Theory]` |
| 前端测试 | 27 个 `*.test.ts` 文件 |
| Vitest 测试定义 | 153 个 `it`/`test` 定义 |
| 浏览器端到端测试 | 阶段 0 开始前不存在；本阶段新增 1 个文件、2 条冒烟测试 |

部分 Theory 和参数化测试会产生多个实际用例，最终数量以测试运行器结果为准。

### 8.2 基线执行结果

| 命令 | 结果 | 实际测试数 |
|---|---|---:|
| `dotnet build WordTemplateBinding.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 通过，0 警告、0 错误 | — |
| `dotnet test tests/WordTemplateBinding.UnitTests/... --no-build` | 通过 | 159 |
| `dotnet test tests/WordTemplateBinding.IntegrationTests/... --no-build` | 通过 | 14 |
| `npm run typecheck` | 通过 | — |
| `npm test` | 通过，27 个测试文件 | 156 |
| `npm run build` | 通过；存在工作区 chunk 大于 500 kB 的既有警告 | — |
| `npm run test:e2e` | 通过 | 2 |

## 9. 核心用户旅程基线

### 9.1 制作模板

当前成功路径：

```text
模板管理
→ 上传 DOCX
→ 模板详情查看解析版本
→ 模板绑定选择项目/章节/模板/版本/数据源
→ 选择片段
→ 拖拽字段
→ 校验绑定
→ 生成测试报告
```

当前缺口：

- 操作分散在模板管理、模板详情和绑定工作区；
- 没有统一步骤完成度；
- 模板解析成功不等于正式发布；
- 模板标准绑定仍依赖项目章节。

### 9.2 生成报告

当前成功路径：

```text
模板绑定工作区
→ 选择已有绑定集
→ 校验
→ 同步生成并下载一份 DOCX
```

当前缺口：

- 没有独立报告生成中心；
- 没有正式模板选择门禁；
- 没有年度和学校范围；
- 没有生成前检查和样例确认；
- 没有异步批量任务、进度、失败重试和历史结果页面。

阶段 0 的浏览器冒烟测试只固化当前可达行为，不假装尚未实现完整九步流程。

## 10. 页面截图基线

截图统一保存到：

```text
output/playwright/baseline/screenshots/
```

页面行为说明保存到：

```text
output/playwright/baseline/README.md
```

已采集：

- `01-project-list.png`：项目管理；
- `02-project-detail.png`：项目详情；
- `03-template-list.png`：模板管理；
- `04-template-detail-current-error.png`：模板详情当前渲染错误；
- `05-workspace-empty.png`：模板绑定工作区初始状态。

截图使用现有数据执行只读浏览，没有为截图创建、更新、归档或删除业务记录。行为说明见 `output/playwright/baseline/README.md`。

### 10.1 截图阶段发现的既有缺陷

模板详情页当前会在渲染版本列表时抛出：

```text
TypeError: Cannot read properties of undefined (reading 'id')
```

页面按 `TemplateVersionView` 访问 `version.version.id`，但版本列表接口当前返回形状与该预期不一致，结果是详情页主体空白。阶段 0 只记录此缺陷；进入阶段 1 前应先新增失败回归测试，再修复契约或页面适配。

## 11. 阶段记录

### 阶段 0：建立基线与冻结术语

状态：已完成

计划变更：

- 更新 README 中的目标术语和当前/目标流程说明；
- 固化本文档；
- 新增两条核心旅程浏览器冒烟测试；
- 保存当前主要页面截图与行为说明；
- 运行全量构建和自动化测试。

数据库变更：无。

实际输出：

- README 统一了两大中心和两条九步流程术语；
- 建立当前路由、API、数据库、测试和页面基线；
- 新增两条不写数据库的 Playwright 浏览器冒烟测试；
- 保存五张主要页面截图和行为说明；
- 后端构建、159 个单元测试、14 个集成测试、156 个前端测试及 2 个端到端测试全部通过。

已知基线问题：

- 模板详情页因版本列表契约不一致而渲染空白；
- `WorkspaceView` 生产 chunk 约 1.7 MB、gzip 约 572 kB，构建产生大于 500 kB 警告；
- 当前仍无独立模板发布和异步批量生成用户流程。

### 阶段 1：代码结构瘦身，不改变业务

状态：已完成

阶段开始前约束：

- 保持现有前端路由、HTTP 路由、请求与响应结构不变；
- 保持现有数据库结构和读写行为不变；
- 保留旧入口和兼容导出，避免一次性迁移所有调用方；
- 先为阶段 0 发现的模板详情版本契约问题补充回归测试，再做前端适配；
- 拆分后端工作区服务与端点、前端 API 与类型模块，并提取工作区状态和共享组件；
- 完成后重新运行后端构建、单元测试、集成测试、前端类型检查、单元测试、构建和浏览器冒烟测试。

数据库变更：无。

预期结构：

```text
WordTemplateBinding.Core
├── TemplateCenter
├── ReportCenter
└── Services（仅保留兼容说明与尚未迁移的服务）

frontend/src
├── api（按 templates / projects / dataSources / bindings 等领域拆分）
├── composables（片段与绑定编辑状态）
└── shared/components（状态、分页和错误提示）
```

实际输出：

- `WorkspaceServices.cs` 缩减为 4 行迁移说明，原有公开服务类型和命名空间不变；
- 核心服务已按 `TemplateCenter`、`ReportCenter` 及其子领域归档，项目章节、数据连接、数据源和绑定实现分别进入独立文件；
- `WorkspaceEndpoints.cs` 缩减为 19 行兼容编排器，原有路由分别由 `ProjectEndpoints`、`DataConnectionEndpoints`、`DataSourceEndpoints` 和 `BindingSetEndpoints` 注册；
- `api/client.ts` 和 `api/types.ts` 已变为兼容出口，调用与类型按模板、项目、数据源、绑定、报告等领域拆分；
- `WorkspaceView.vue` 的片段边界状态和绑定选择状态分别提取到 `useSegmentEditor`、`useBindingEditor`；
- `StatusBadge`、`PaginationControls`、`ErrorBanner` 已由项目列表和模板列表共同复用；
- 修复阶段 0 记录的模板详情空白问题：前端保留版本摘要接口契约，并按版本 ID 读取完整视图；
- 新增模板详情契约、片段编辑状态和绑定编辑状态回归测试。

兼容性结论：

- 前端路由无变化；
- HTTP 路由与后端响应结构无变化；
- 数据库结构和读写行为无变化；
- 旧的 `../api/client`、`../api/types` 导入方式继续有效；
- Core 服务仍使用 `WordTemplateBinding.Core.Services` 命名空间，现有依赖注入注册无需修改。

验证结果：

| 验证项 | 结果 |
|---|---|
| `dotnet build WordTemplateBinding.sln --no-restore -m:1 -p:UseSharedCompilation=false` | 通过，0 警告、0 错误 |
| 后端单元测试 | 159/159 通过 |
| 后端集成测试 | 14/14 通过 |
| `npm run typecheck` | 通过 |
| 前端 Vitest | 29 个文件、161/161 通过 |
| `npm run build` | 通过；工作区大 chunk 警告仍为既有问题 |
| Playwright 核心旅程 | 3/3 通过 |

阶段 1 未解决项：

- `WorkspaceView` 生产 chunk 约 1.75 MB、gzip 约 573 kB；阶段 2～3 在引入新页面壳和步骤组件时继续按路由/步骤拆包；
- 模板详情为兼容现有后端契约，会按摘要逐个读取完整版本；后续聚合工作台 API 落地后统一消除多请求；
- 尚未引入两大中心的新首页和一级导航，该工作属于阶段 2。

### 阶段 2：应用外壳与两大中心入口

状态：已完成

阶段开始前约束：

- 首页只负责引导两条核心任务，不在本阶段提前实现九步业务能力；
- 一级导航调整为“首页、模板制作中心、报告生成中心”；
- 新增两大中心布局，统一中心内导航、面包屑和内容出口；
- 保留 `/projects`、`/projects/:projectId`、`/templates`、`/templates/:templateId`、`/workspace` 旧链接；
- 旧链接直接访问时应继续工作，必要时提供迁移提示，不删除旧页面；
- 不修改 HTTP API 和数据库结构。

数据库变更：无。

预期新入口：

```text
/
├── /template-center
│   ├── /template-center/templates
│   └── /template-center/studio
└── /report-center
    ├── /report-center/jobs
    ├── /report-center/new
    └── /report-center/history
```

实际输出：

- `/` 已由旧的项目列表重定向改为任务导向首页，明确展示“两项核心任务”及两套九步流程；
- 一级导航已调整为“首页、模板制作中心、报告生成中心”；
- 新增 `TemplateCenterLayout.vue`，提供模板库、制作工作台、面包屑和制作流程提示；
- 新增 `ReportCenterLayout.vue`，提供生成任务、新建任务、生成记录、面包屑和生成流程提示；
- 模板库、模板详情和现有绑定工作区已接入模板制作中心；
- 报告生成中心已建立三个稳定路由和页面骨架，后续阶段在这些入口内接入发布模板、任务和产物能力；
- `/projects`、`/projects/:projectId`、`/templates`、`/templates/:templateId`、`/workspace` 旧地址继续可访问；
- 模板库内部操作已优先导航到新中心地址，收藏的旧地址仍保持兼容。
- README 已同步新的首页、中心路由、兼容入口、首次使用流程和测试基线。

验证结果：

| 验证项 | 结果 |
|---|---|
| 后端构建 | 通过，0 警告、0 错误 |
| 后端单元测试 | 159/159 通过 |
| 后端集成测试 | 14/14 通过 |
| `npm run typecheck` | 通过 |
| 前端 Vitest | 30 个文件、163/163 通过 |
| `npm run build` | 通过；工作区大 chunk 警告仍为既有问题 |
| Playwright 核心旅程 | 4/4 通过 |

兼容性结论：

- HTTP API 和数据库无变化；
- 旧页面和旧书签路由仍可访问；
- 新中心使用独立懒加载页面块，首页和中心外壳未增加工作区主 chunk；
- 报告生成中心当前仅为清晰入口和骨架，未伪造尚不存在的发布模板或异步任务数据。

### 阶段 3：模板制作工作台基础流程

状态：已完成

阶段开始前约束：

- 在一个连续工作台中覆盖第 1～3 步和第 5～8 步，第 4 步只接入已经稳定的规则入口；
- 复用现有 DOCX 上传、片段扫描、图表预览、数据源、绑定、校验和测试生成能力；
- `WorkspaceView.vue` 继续作为兼容页面，但不再承担九步工作台容器和全部步骤状态；
- 片段边界支持一次提交多个待保存范围，只创建一个新模板版本；
- 当前步骤、模板版本和片段选择可从 URL 恢复，未保存边界草稿提供离开保护；
- 新增工作台聚合查询，减少首屏串行请求；
- 发布版本查询只提供后续生成中心所需的只读契约，本阶段不新增发布表或伪造发布状态；
- 不修改数据库结构。

数据库变更：无。

预期新结构：

```text
frontend/src/features/template-studio
├── TemplateStudioView.vue
├── composables
└── steps
    ├── CreateTemplateStep.vue
    ├── ConfirmStructureStep.vue
    ├── MarkDynamicContentStep.vue
    ├── ConfigureRulesStep.vue
    ├── ConnectDataSourceStep.vue
    ├── BindFieldsStep.vue
    ├── ValidateTemplateStep.vue
    ├── TestTemplateStep.vue
    └── PublishTemplateStep.vue

WordTemplateBinding.Api
└── TemplateStudioEndpoints
```

实际输出：

- `/template-center/studio` 已切换为九步工作台容器，当前步骤和模板、版本、项目、章节、数据源、片段均保存在 URL，并将最近工作上下文写入浏览器本地恢复点；
- 第 1 步可填写模板信息、上传 DOCX、创建不可变版本并触发扫描；
- 第 2 步使用真实 DOCX 片段预览，支持同时编辑多个不重叠边界、一次保存并只创建一个新版本，也支持删除已写入边界且保留正文；
- 第 3 步按文本、图表、表格和其他类型展示稳定元素键、定位方式、片段归属与解析状态，并支持重新扫描；
- 第 4 步只开放当前已稳定的固定区块说明，条件与重复规则明确保留到阶段 7，不伪造持久化能力；
- 第 5 步统一选择项目、章节、数据源和字段快照；第 6 步自动继承这些上下文并复用原有三栏绑定、DOCX/图表预览和自动保存能力；
- 第 7 步按等级展示绑定校验问题并提供返回绑定修复入口；第 8 步生成前重新校验并下载真实样例 DOCX；
- 第 9 步接入只读发布能力契约；在阶段 4 发布表落地前返回不可发布状态，READY 解析版本不会出现在报告生成中心；
- 原 `WorkspaceView.vue` 缩减为兼容包装器，`/workspace` 旧路由继续加载原绑定工作区能力；
- 新增 `GET /api/template-studio/{templateId}` 聚合接口、`POST /api/template-versions/{versionId}/segment-boundaries/batch` 批量边界接口和 `GET /api/template-releases` 发布查询契约；
- 工作台聚合查询在片段扫描可能修复元素归属后重新读取版本，保证元素列表与片段统计处于同一状态；
- 阶段 3 未修改数据库结构。

验证结果：

| 验证项 | 结果 |
|---|---|
| `dotnet test WordTemplateBinding.sln --no-restore` | 162/162 单元测试、16/16 集成测试通过 |
| `npm run typecheck` | 通过 |
| 前端 Vitest | 30 个文件、164/164 通过 |
| `npm run build` | 通过；DOCX 处理 chunk 大于 500 kB 的警告仍存在 |
| Playwright 核心旅程 | 4/4 通过 |
| Playwright CLI 目视回归 | 通过；控制台 0 错误、0 警告 |

目视回归截图：

- `output/playwright/phase3-template-studio-step1.png`

阶段 3 保留项：

- 模板发布记录、发布门禁和可复用模板绑定配置属于阶段 4；
- 条件区块、重复区块和蓝图的可视化规则编辑属于阶段 7；
- 报告生成中心的异步批量生成闭环属于阶段 5～6。

## 12. 测试文件清单

### 12.1 后端单元测试

- `ChartAnalysisTests.cs`
- `ConditionEvaluatorTests.cs`
- `DataValueFormatterTests.cs`
- `FileStorageTests.cs`
- `JsonDataContextResolverTests.cs`
- `PersistentWorkflowTests.cs`
- `RadarChartTests.cs`
- `RendererTests.cs`
- `ReportPlatformDatabaseConnectionFactoryTests.cs`
- `ReusableTemplateRendererTests.cs`
- `ScannerTests.cs`
- `SchemaStoreAndBindingTests.cs`
- `TemplateAutoBindingResolverTests.cs`
- `TemplateSegmentTests.cs`
- 测试公共工厂：`OpenXmlTestDocumentFactory.cs`、`TestServiceFactory.cs`

### 12.2 后端集成测试

- `ApiWorkflowTests.cs`
- `PersistentApiWorkflowTests.cs`
- 测试宿主：`IntegrationWebApplicationFactory.cs`
- DOCX 工厂：`TestDocumentFactory.cs`

### 12.3 前端 Vitest

- 组件：`ChartStructurePanel.test.ts`、`DocxUploadPanel.test.ts`
- API：`apiClient.test.ts`
- DOCX：`documentChartLocator.test.ts`、`markerInjector.test.ts`、`processDocx.integration.test.ts`、`sampleDocument.integration.test.ts`
- 绑定：`chartWorkspace.test.ts`、`importSummaryStatus.test.ts`、`renderedChartBindings.test.ts`、`renderedDocumentBindings.test.ts`
- 图表识别与渲染：`barChartDetector.test.ts`、`groupChartWithCaption.test.ts`、`radarChartHandler.test.ts`、`radarRenderer.test.ts`、`wordRadarChartToECharts.test.ts`
- 图表分析：`axisAnalyzer.test.ts`、`bindingSchema.test.ts`、`cacheParser.test.ts`、`categoryAnalyzer.test.ts`、`chartRelationshipReader.test.ts`、`chartXmlAnalyzer.test.ts`、`dataTable.test.ts`、`embeddedWorkbookReader.test.ts`、`seriesAnalyzer.test.ts`
- 应用结构：`router.test.ts`、`useSegmentEditor.test.ts`、`useBindingEditor.test.ts`
- 工具：`numberUtils.test.ts`、`relationshipParser.test.ts`

### 12.4 浏览器冒烟测试

`frontend/e2e/core-journeys.smoke.spec.ts`：

1. 新首页和两大中心入口，并验证旧模板路由兼容；
2. 制作模板当前旅程：从模板库进入片段与绑定工作区；
3. 模板详情版本摘要契约回归；
4. 生成报告当前旅程：校验绑定并下载单份报告。

冒烟测试拦截 `/api/` 请求并使用固定数据，不读取或修改真实 MySQL；DOCX 预览和下载使用仓库已有样例文件。
