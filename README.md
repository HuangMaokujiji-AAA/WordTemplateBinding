# WordTemplateBinding

WordTemplateBinding 是一个基于 .NET 7、ASP.NET Core Minimal API、Open XML SDK、
Vue 3 和 MySQL 8 的 Word 模板数据绑定平台。

支持上传 DOCX、识别可绑定元素、预览 Word 原生图表、管理模板版本、项目管理、
章节管理、测试 JSON 数据源、配置字段绑定，并生成报告或导出可复用模板。

## 1. 产品定位与改造方向

系统围绕两个核心任务逐步重组：

1. **制作可复用报告模板**：创建模板、确认结构、标记动态内容、设置区块规则、连接数据、绑定、校验、测试和发布。
2. **使用模板批量生成报告**：选择已发布模板、年度和范围，设置输出规则，完成生成前检查和样例确认，再批量生成、下载和追踪记录。

目标一级业务入口：

- **模板制作中心**
- **报告生成中心**

当前一级导航已启用“首页、模板制作中心、报告生成中心”。旧的项目、模板和绑定页面路由仍保持兼容。改造采用渐进迁移方式，不重写项目、不更换 Vue 3、ASP.NET Core、Open XML SDK 或 MySQL，也不会在新流程稳定前删除旧路由和接口。

详细计划和改造前基线：

- [两大中心项目改造计划](plan/04-two-center-project-refactoring-plan.md)
- [两大中心改造基线](docs/refactor-baseline.md)

## 2. 快速启动

### 2.1 环境要求

- .NET SDK 7.0
- MySQL 8.0+（已初始化 `report_platform` 数据库）
- Node.js 20+（仅修改前端时需要）

检查版本：

```powershell
dotnet --version   # 7.0.x
node --version     # 20+（可选）
```

### 2.2 初始化数据库

首次使用前，在 MySQL 中执行建表脚本：

```sql
-- 1. 建表
source sql/report_platform_v2_schema.sql;

-- 2. 数据库文件存储迁移
source sql/report_platform_v2_to_v2_1_database_file_storage_migration.sql;
```

### 2.3 配置数据库连接

实际运行固定使用 MySQL。不要把真实地址、账号或密码写入
`src/WordTemplateBinding.Api/appsettings.json`，开发机使用 .NET User Secrets：

```powershell
dotnet user-secrets set "Database:Host" "127.0.0.1" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "Database:Port" "3306" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "Database:Database" "report_platform" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "Database:Username" "report_app" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "Database:Password" "请替换为真实密码" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "Database:SslMode" "Required" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "ApplicationIdentity:DefaultActorUserId" "1" `
  --project .\src\WordTemplateBinding.Api
```

本机不支持 TLS 的 MySQL 可将 `SslMode` 改为 `None`；远程数据库建议保持
`Required` 或使用更严格的证书校验。生产部署应使用部署平台的密钥管理器和
`Database__Host`、`Database__Password` 等环境变量。

### 2.4 启动

```powershell
cd D:\Code\WordTemplateBinding
dotnet run --project .\src\WordTemplateBinding.Api
```

看到以下输出即启动成功：

```text
Now listening on: http://127.0.0.1:5080
Application started. Press Ctrl+C to shut down.
```

浏览器访问 `http://127.0.0.1:5080`，按 `Ctrl+F5` 强制刷新。

检查当前进程是否真的连接 MySQL：

```powershell
Invoke-RestMethod "http://127.0.0.1:5080/api/system/database/health"
```

返回的 `status` 必须是 `healthy`，`database` 必须是 `report_platform`。模板、
文件分片、项目、章节、数据源、快照和绑定都会写入该数据库；应用重启后数据仍然存在。

### 2.5 当前版本首次使用流程

1. 从首页进入 **模板制作中心**；已有项目首次配置时可继续打开兼容入口 `/projects`
2. 在项目管理中点击 **新建项目**
3. 填写项目编码和名称，保持默认勾选“创建默认章节”和“初始化测试 JSON 数据”
4. 创建成功后进入项目详情，可以看到默认章节和测试数据源；这些记录同样保存在 MySQL
5. 在 **模板制作中心 → 制作工作台** 创建模板并上传 DOCX
6. 确认报告结构；需要拆分时可先加入多个边界，再一次保存到一个新模板版本
7. 确认系统识别的文本、表格和 Word 原生图表，选择项目、章节和数据源
8. 在三栏绑定界面将字段绑定到文档动态内容，然后运行完整性校验
9. 生成并下载样例 DOCX；模板正式发布能力将在阶段 4 接入

## 3. 构建和测试

```powershell
# 后端
dotnet restore
dotnet build
dotnet test                                      # 当前基线：162 单元测试 + 16 集成测试

# 前端（修改前端代码后）
cd frontend
npm ci
npm run typecheck
npm test                                         # 当前基线：164 前端测试
npm run test:e2e                                # 4 条核心旅程，需要本机 Google Chrome
npm run build                                    # 构建到 wwwroot
cd ..
```

> 自动化测试通过专用 `integration.runsettings` 使用隔离的 InMemory 仓储，不会连接、
> 读取或修改真实 MySQL。该设置只作用于测试进程；`dotnet run` 始终使用 MySQL。

## 4. 前端热更新开发

窗口 A — 后端：

```powershell
dotnet run --project .\src\WordTemplateBinding.Api

mac：dotnet run --project ./src/WordTemplateBinding.Api
```

窗口 B — Vite：

```powershell
cd frontend
npm ci
npm run dev
```

访问 `http://127.0.0.1:5173`，Vite 自动代理 `/api` 到后端 5080。

## 5. 当前页面结构

| 路由                                               | 页面         | 功能                                       |
| -------------------------------------------------- | ------------ | ------------------------------------------ |
| `/`                                              | 首页         | 引导“制作模板”和“生成报告”两项核心任务 |
| `/template-center/templates`                     | 模板库       | 模板列表、上传、搜索、归档/恢复            |
| `/template-center/templates/:id`                 | 模板详情     | 模板编辑、版本管理、下载和重扫             |
| `/template-center/studio`                        | 制作工作台   | 九步制作流程、批量片段边界、绑定、校验和样例测试 |
| `/report-center/jobs`                            | 生成任务     | 报告生成中心任务入口，异步任务能力后续接入 |
| `/report-center/new`                             | 新建生成任务 | 九步生成向导入口                           |
| `/report-center/history`                         | 生成记录     | 历史任务和产物入口                         |
| `/projects`、`/projects/:id`                   | 兼容项目页面 | 项目、章节和测试数据源管理                 |
| `/templates`、`/templates/:id`、`/workspace` | 兼容旧页面   | 保证旧收藏链接继续可用                     |

## 6. 当前页面使用流程

1. **首页** — 选择“制作可复用报告模板”或“使用模板批量生成报告”
2. **模板制作中心 → 制作工作台** — 创建模板并上传 DOCX，系统自动扫描动态内容
3. **确认结构** — 浏览真实片段预览，批量保存或删除 DOCX 内容控件边界
4. **标记与数据** — 确认识别的文本、表格、图表，选择项目、章节和数据源快照
5. **绑定、校验与测试** — 拖拽字段完成绑定，修复校验问题并下载样例 DOCX

当前工作台第 9 步不会把 READY 解析版本当作已发布版本。正式发布门禁、发布记录和模板标准绑定配置将在阶段 4 通过数据库迁移接入。

## 7. 模板标记

默认识别：

- `{{path}}` 显式占位符
- `w:highlight w:val="yellow"` 黄色高亮
- Run 级 `w:shd w:fill="FFFF00"` 黄色底纹
- Word 原生图表

扫描范围：正文、表格、页眉、页脚、脚注、尾注、文本框。

## 8. 数据库表结构

| 领域     | 核心表                                                            |
| -------- | ----------------------------------------------------------------- |
| 文件存储 | `rp_file_object`、`rp_file_chunk`、`rp_file_upload_session` |
| 模板     | `rp_template`、`rp_template_version`、`rp_template_element` |
| 项目章节 | `rp_project`、`rp_chapter`                                    |
| 数据源   | `rp_data_source`、`rp_data_snapshot`、`rp_data_field`       |
| 绑定     | `rp_binding_set`、`rp_binding_item`                           |
| 审计     | `rp_audit_log`                                                  |

数据结构说明见 `sql/report_platform_v2_1_database_design_and_dictionary.md`。

## 9. 相关文档

- [数据库设计与数据字典](sql/report_platform_v2_1_database_design_and_dictionary.md)
- [HTTP API](docs/api.md)
- [第一第二阶段架构设计](docs/phase1-design.md)
- [前端图表解析设计](docs/chart-analysis-design.md)
- [两大中心项目改造计划](plan/04-two-center-project-refactoring-plan.md)
- [两大中心改造基线](docs/refactor-baseline.md)
