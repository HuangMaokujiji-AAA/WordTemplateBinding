# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概览

WordTemplateBinding 是一个基于 .NET 7 / ASP.NET Core Minimal API + Vue 3 + MySQL 8 的 Word 模板数据绑定平台。核心能力：上传 DOCX、识别可绑定元素（黄色高亮、`{{path}}` 占位符、原生图表）、预览 Word 图表、管理模板版本/项目/章节、配置字段绑定、生成报告或导出可复用模板。

详细使用流程、页面路由、模板标记规则、数据库表结构见 [README.md](README.md)。

## 环境与配置

- .NET SDK 7.0（`Directory.Build.props` 强制 `TreatWarningsAsErrors`、`Nullable=enable`、`LangVersion=11.0`）
- Node.js 20+（仅修改前端时需要）
- MySQL 8.0+（实际运行依赖）
- 数据库 schema 在 `sql/report_platform_v2_schema.sql`；文件存储迁移脚本 `sql/report_platform_v2_to_v2_1_database_file_storage_migration.sql`
- 数据库连接通过 .NET User Secrets 注入（参考 `README.md` 1.3 节）；**真实凭据不要写入 `appsettings.json`**（当前文件中的 `Database` 节仅为开发占位）
- `appsettings.json` 中 `Persistence:Mode` 决定仓储实现：实际运行必须是 `MySql`；`InMemory` 仅允许 `Testing` 环境，否则 `Program.cs` 直接抛 `InvalidOperationException`
- `tests/WordTemplateBinding.IntegrationTests/integration.runsettings` 会强制测试走 `InMemory` 仓储，不会触碰真实数据库

## 常用命令

```powershell
# 后端
dotnet restore
dotnet build
dotnet test                                         # 运行所有测试
dotnet test tests\WordTemplateBinding.UnitTests     # 仅单元测试
dotnet test tests\WordTemplateBinding.IntegrationTests   # 仅集成测试
dotnet run --project .\src\WordTemplateBinding.Api   # 启动后端（http://127.0.0.1:5080）

# 前端
cd frontend
npm ci
npm run dev          # Vite 开发服务器（默认 :5173，自动代理 /api -> :5080）
npm run typecheck    # vue-tsc --noEmit
npm test             # vitest run（src/tests/**/*.test.ts）
npm run build        # 输出到 src/WordTemplateBinding.Api/wwwroot

# 跳过前端构建发布后端
dotnet publish -p:SkipFrontendBuild=true
```

健康检查：`Invoke-RestMethod "http://127.0.0.1:5080/api/system/database/health"` 必须返回 `status=healthy`。

## 解决方案结构

`WordTemplateBinding.sln` 五个项目，自下而上：

- `src/WordTemplateBinding.Core` — 领域模型、接口、业务编排服务（无 IO 实现）。`Services/` 下四个 `*WorkflowService`（`Template`、`Binding`、`Report`、`ReusableTemplate`）+ `TemplateAutoBindingResolver`、`BindingCandidateResolver`、`BindingSetDocumentService`、`TemplateCatalogService`、各种 `WorkspaceServices`。`Interfaces/` 是仓储/服务契约，`Options/` 是 `TemplateProcessingOptions` / `PersistenceOptions` 等配置类。
- `src/WordTemplateBinding.Infrastructure` — `OpenXml/` 实现 Word 扫描/写入（`WordTemplateScanner`、`WordReportRenderer`、`WordReusableTemplateRenderer`、`OpenXmlChartReader/Writer`、各种 `*Recognizer`）；`Database/` 实现 `MySql*Repository`、`MySqlAuditLogWriter`、`MySqlDataAccess`、`ReportPlatformDatabaseConnectionFactory`；`DataSchema/` 演示数据源；`Stores/InMemoryStores.cs` 是测试/开发用的内存仓储；`DependencyInjection/ServiceCollectionExtensions.cs` 是 DI 入口（`AddWordTemplateBinding` + `AddReportPlatformDatabase`）。
- `src/WordTemplateBinding.Api` — ASP.NET Core Minimal API。`Program.cs` 装配管线；`Endpoints/` 按领域拆 `BindingEndpoints` / `ChartAnalysisEndpoints` / `DataSchemaEndpoints` / `ReportEndpoints` / `TemplateEndpoints` / `Persistent*Endpoints` / `WorkspaceEndpoints` / `DatabaseEndpoints`；`Middleware/ApiExceptionHandler.cs` 统一异常 → ProblemDetails；`Contracts/` 是 DTO；`Infrastructure/DatabaseIdParser.cs` 解析路径里的数据库 ID。
- `tests/WordTemplateBinding.UnitTests`（xUnit，纯逻辑）+ `tests/WordTemplateBinding.IntegrationTests`（`Microsoft.AspNetCore.Mvc.Testing` + `WebApplicationFactory`，通过 runsettings 切到 InMemory）。

## 后端架构要点

- **两种持久化模式**通过 `Persistence:Mode` 切换：`InMemory` 走 `Stores/InMemoryStores.cs` 与一组 `InMemory*Repository`，只用于自动化测试；`MySql` 走 `Database/MySql*Repository` 系列。`Program.cs` 在非 Testing 环境下禁止 InMemory；MySQL 模式下只映射 `Persistent*Endpoints` / `WorkspaceEndpoints` / `DatabaseEndpoints`，旧的 `Template/Binding/DataSchema/Report/ChartAnalysis` endpoints 仅在 InMemory 下挂载。
- **模板识别流水线**：`WordTemplateScanner.ScanAsync` 打开 `WordprocessingDocument` 扫描正文/页眉/页脚/脚注/尾注/文本框，把每段文本/表格交给一组 `IMockDataRecognizer`（`DecimalNumberRecognizer`、`IntegerNumberRecognizer`、`YellowHighlightRecognizer`、`ExplicitTextRecognizer`，注册与否由 `TemplateProcessingOptions.Enable*` 控制）。结果 `TemplateScanResult` 包含 `MockItems`（文本绑定槽）+ `Charts`（图表绑定槽）+ `Warnings`，统一用 `ILocatorIdGenerator` 生成稳定 ID。
- **图表绑定后端**：`OpenXmlChartReader` + `Charts/ChartBindingContractBuilder`、`ChartDataBindingResolver`、`ChartDataTableBuilder`、`EmbeddedChartWorkbookWriter` —— 把图表缓存（`c:numCache` / `c:strCache`）写回 OOXML 包；图表数据契约定义在 `Core/Models/ChartBindingModels.cs`、`ChartAnalysisModels.cs`。
- **写入流程**：`BindingWorkflowService.UpsertAsync` 校验模板/Locator/字段存在并区分文本 vs 图表绑定；`WordReportRenderer` 生成最终 DOCX，`WordReusableTemplateRenderer` 输出含占位符的可复用模板。
- **配置校验**：`Program.cs` 用 `Validator.ValidateObject` 强校验 `TemplateProcessingOptions` / `PersistenceOptions`；缺关键 MySQL 配置会立即抛错。

## 前端架构要点

Vite + Vue 3 + TypeScript（`vue-tsc`）。`vite.config.ts`：
- `npm run build` 输出到 `src/WordTemplateBinding.Api/wwwroot`，所以后端 `dotnet run` 直接托管同一套页面；`Api.csproj` 的 `BuildVueFrontend` target 在 publish 阶段（且 `node_modules` 存在时）自动跑 `npm run build`，可通过 `-p:SkipFrontendBuild=true` 跳过。
- dev 模式 `server.proxy['/api']` 转发到 `WTB_API_ORIGIN`（默认 `http://127.0.0.1:5080`）。
- 测试：`src/tests/**/*.test.ts`，`vitest` + `jsdom`。

`frontend/src/` 顶层组织：
- `App.vue` + `router/` + `main.ts`
- `views/` — `ProjectListView` / `ProjectDetailView` / `TemplateListView` / `TemplateDetailView` / `WorkspaceView`（绑定工作台）
- `components/` — 上传面板、DocxViewer、SchemaTreeNode、ParseStatusPanel、ChartStructurePanel、JsonStructureViewer、LoadingOverlay
- `features/binding/` — `chartWorkspace.ts` / `renderedChartBindings.ts` / `renderedDocumentBindings.ts` / `importSummaryStatus.ts`（绑定工作台核心状态）
- `features/docx/` — DOCX 处理流水线，分三大子模块：
  - `ooxml/` — OOXML 工具：命名空间、关系解析、文档图表定位 `documentChartLocator`、Marker 注入 `docxMarkerInjector`、路径/解析辅助
  - `chart-analysis/` — **深度 OOXML 解析的统一入口**。`parsers/chartXmlAnalyzer.ts` 单次解析出 `ParsedWordChart`（结构化模型：分类/系列/公式/坐标轴/图例/样式/嵌入工作簿/绑定槽/诊断）；`normalizers/` 把数据归一为 `ChartDataTable` 与 `ChartBindingSchema`；`render/toWordChartModel.ts` 把模型投影到 ECharts 输入的 `WordChartModel`。`models/types.ts` 是核心 JSON-safe 类型（约束：禁止引用 DOM、XML Node、Map、ECharts）。
  - `chart-recognition/` — **按图表类型分发的 Handler + ECharts 渲染管线**。`detectors/analyzerBridge.ts` 桥接 `chart-analysis`；每个图表类型一个 `Handler`（bar/line/pie/doughnut/area/scatter/combo/unsupported），`canHandle` 判定、`parse` 统一走 bridge（不再各自解析 XML）。`mappers/wordXChartToECharts.ts` 是纯函数映射，`renderers/EChartsXRenderer.ts` 挂载 DOM。`utils/` 提供 EMU/数值/调色板兜底。
  - `rendering/` — `renderDocx.ts` 用 `docx-preview` 渲染正文+表格+图片+近似分页；`replaceChartMarkers.ts` / `chartInstanceManager.ts` 把图表占位符替换为 ECharts 实例；`groupChartWithCaption.ts` 处理图表与外部标题的合并（识别 `w:keepNext` 与相邻居中短标题）。
  - `file-validator/` — DOCX 文件合法性校验
  - `processDocx.ts` — **总入口**：校验 → JSZip 解压 → `documentChartLocator` 定位图表 → `docxMarkerInjector` 注入文本占位 → `docx-preview` 渲染 → 在 DOM 中找 marker → `chartXmlAnalyzer` 解析 → `toWordChartModel` + ECharts 渲染。详见 `processDocx.ts` 顶部注释。

后端响应里 `partKind/partKey/paragraphIndex/startOffset/length` 是最终替换的唯一真相；前端先把 Locator 映射到 `docx-preview` 输出的 HTML Text Node（正文黄色、FooterPart 紫色），匹配不上时回退到左右模拟值导航。

## 约定与提醒

- **不要**把真实数据库凭据写入 `appsettings.json`；使用 User Secrets 或环境变量（`Database__Host`、`Database__Password` 等）
- 修改前端时记得 `npm run build` 让生产资源同步到 `wwwroot`（生产资源已提交到仓库）
- 新增可预览图表类型（例如雷达图）参考 [frontend/src/features/docx/chart-recognition/README.md](frontend/src/features/docx/chart-recognition/README.md) 的扩展指南：不需要新增 Handler 或再次解析 XML，只在 `chart-analysis` 的 `chartTypeElements.ts`/`toWordChartModel.ts` 增加投影，再写一个 mapper + renderer 即可
- 修改图表解析时只改 `chart-analysis` 模块，避免在 `chart-recognition` 中重新遍历 Chart XML
- 测试覆盖：后端 110 单测 + 12 集成测试；前端 139 测试。CI 上跑测试前确保 `IntegrationTests` 走的是 `integration.runsettings`
- 相关文档：[docs/api.md](docs/api.md)、[docs/phase1-design.md](docs/phase1-design.md)、[frontend/src/features/docx/chart-recognition/README.md](frontend/src/features/docx/chart-recognition/README.md)、[frontend/README.md](frontend/README.md)