# WordTemplateBinding

WordTemplateBinding 是一个基于 .NET 7、ASP.NET Core Minimal API、Open XML SDK、
Vue 3 和 MySQL 8 的 Word 模板数据绑定平台。

支持上传 DOCX、识别可绑定元素、预览 Word 原生图表、管理模板版本、项目管理、
章节管理、测试 JSON 数据源、配置字段绑定，并生成报告或导出可复用模板。

## 1. 快速启动

### 1.1 环境要求

- .NET SDK 7.0
- MySQL 8.0+（已初始化 `report_platform` 数据库）
- Node.js 20+（仅修改前端时需要）

检查版本：

```powershell
dotnet --version   # 7.0.x
node --version     # 20+（可选）
```

### 1.2 初始化数据库

首次使用前，在 MySQL 中执行建表脚本：

```sql
-- 1. 建表
source sql/report_platform_v2_schema.sql;

-- 2. 数据库文件存储迁移
source sql/report_platform_v2_to_v2_1_database_file_storage_migration.sql;
```

### 1.3 配置数据库连接

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

### 1.4 启动

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

### 1.5 首次使用流程

1. 打开 **项目管理** → 点击 **新建项目**
2. 填写项目编码和名称，保持默认勾选“创建默认章节”和“初始化测试 JSON 数据”
3. 创建成功后进入项目详情，可以看到默认章节和测试数据源；这些记录同样保存在 MySQL
4. 打开 **模板管理** → 点击 **上传模板**，上传 DOCX 文件
5. 模板解析完成后，可以在详情页查看版本和解析元素
6. 打开 **模板绑定** → 选择项目 → 章节 → 模板 → 数据源
7. 从左侧字段树拖拽字段到文档黄色高亮区域完成绑定
8. 点击 **生成测试报告** 导出 DOCX

## 2. 构建和测试

```powershell
# 后端
dotnet restore
dotnet build
dotnet test                                      # 110 单元测试 + 12 集成测试

# 前端（修改前端代码后）
cd frontend
npm ci
npm run typecheck
npm test                                         # 139 前端测试
npm run build                                    # 构建到 wwwroot
cd ..
```

> 自动化测试通过专用 `integration.runsettings` 使用隔离的 InMemory 仓储，不会连接、
> 读取或修改真实 MySQL。该设置只作用于测试进程；`dotnet run` 始终使用 MySQL。

## 3. 前端热更新开发

窗口 A — 后端：

```powershell
dotnet run --project .\src\WordTemplateBinding.Api
```

窗口 B — Vite：

```powershell
cd frontend
npm ci
npm run dev
```

访问 `http://127.0.0.1:5173`，Vite 自动代理 `/api` 到后端 5080。

## 4. 页面结构

| 路由               | 页面     | 功能                                        |
| ------------------ | -------- | ------------------------------------------- |
| `/projects`      | 项目管理 | 项目列表、搜索、创建、归档/恢复             |
| `/projects/:id`  | 项目详情 | 项目信息编辑、章节管理、测试数据源初始化    |
| `/templates`     | 模板管理 | 模板列表、上传、搜索、归档/恢复             |
| `/templates/:id` | 模板详情 | 模板编辑、版本管理、上传新版本、下载/重扫   |
| `/workspace`     | 模板绑定 | DOCX 预览、字段拖拽绑定、图表绑定、生成报告 |

## 5. 页面使用流程

1. **项目管理** — 新建项目（自动创建默认章节 + 初始化测试 JSON 数据源）
2. **模板管理** — 上传 DOCX，系统自动解析黄色标记、占位符和图表
3. **模板绑定** — 依次选择项目 → 章节 → 模板 → 模板版本 → 数据源
4. 从左侧字段树拖拽标量字段到黄色/紫色文本，集合字段到橙色图表区域
5. 点击"校验绑定"确认配置完整，点击"生成测试报告"导出 DOCX

## 6. 模板标记

默认识别：

- `{{path}}` 显式占位符
- `w:highlight w:val="yellow"` 黄色高亮
- Run 级 `w:shd w:fill="FFFF00"` 黄色底纹
- Word 原生图表

扫描范围：正文、表格、页眉、页脚、脚注、尾注、文本框。

## 7. 数据库表结构

| 领域     | 核心表                                                            |
| -------- | ----------------------------------------------------------------- |
| 文件存储 | `rp_file_object`、`rp_file_chunk`、`rp_file_upload_session` |
| 模板     | `rp_template`、`rp_template_version`、`rp_template_element` |
| 项目章节 | `rp_project`、`rp_chapter`                                    |
| 数据源   | `rp_data_source`、`rp_data_snapshot`、`rp_data_field`       |
| 绑定     | `rp_binding_set`、`rp_binding_item`                           |
| 审计     | `rp_audit_log`                                                  |

数据结构说明见 `sql/report_platform_v2_1_database_design_and_dictionary.md`。

## 8. 相关文档

- [数据库设计与数据字典](sql/report_platform_v2_1_database_design_and_dictionary.md)
- [HTTP API](docs/api.md)
- [第一第二阶段架构设计](docs/phase1-design.md)
- [前端图表解析设计](docs/chart-analysis-design.md)
