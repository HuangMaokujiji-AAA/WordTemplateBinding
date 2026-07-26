# WordTemplateBinding

WordTemplateBinding 是一个基于 .NET 7、ASP.NET Core Minimal API、Open XML SDK、
Vue 3 和 MySQL 8 的 Word 模板数据绑定平台。

系统支持上传 DOCX、识别可绑定元素、预览 Word 原生图表、管理模板版本、连接业务
MySQL 数据源、配置字段绑定，并生成报告或导出可复用模板。

## 1. 选择启动方式

| 场景            | 持久化                   | 是否需要 MySQL | 是否需要 Node.js     | 入口                      |
| --------------- | ------------------------ | -------------- | -------------------- | ------------------------- |
| 本地快速体验    | InMemory                 | 否             | 否                   | `http://127.0.0.1:5080` |
| 前端热更新开发  | InMemory 或 MySQL        | 可选           | 是                   | `http://127.0.0.1:5173` |
| MySQL 联调/生产 | MySQL`report_platform` | 是             | 仅重新构建前端时需要 | 自定义                    |

第一次运行建议先使用“本地快速体验”。该模式无需填写数据库账号，关闭进程后数据会
清空。

## 2. 环境要求

必需：

- .NET SDK 7.0。仓库的 `global.json` 会在已安装的 7.0 Feature Band 中向前滚动。
- Windows PowerShell 7 或常规 PowerShell。

按需安装：

- Node.js 20+：仅修改、测试或重新构建 Vue 前端时需要。
- MySQL 8.0+：仅使用真实持久化与业务数据源时需要。
- Microsoft Word 或 WPS Office：人工检查最终 DOCX 时建议安装。

检查版本：

```powershell
dotnet --version
node --version
npm --version
```

如果只运行仓库已构建好的页面，`node` 和 `npm` 可以不安装。

## 3. 本地快速启动（推荐）

### 3.1 进入仓库

```powershell
cd D:\Code\WordTemplateBinding
```

### 3.2 还原、构建和测试

```powershell
dotnet restore WordTemplateBinding.sln
dotnet build WordTemplateBinding.sln
dotnet test WordTemplateBinding.sln
```

当前基线应通过：

- 110 个单元测试；
- 12 个集成测试。

只想立即体验页面时，可以在成功还原后直接启动；但首次运行建议至少执行一次构建。

### 3.3 启动 API 和内置页面

```powershell
dotnet run --project .\src\WordTemplateBinding.Api
```

`Properties/launchSettings.json` 会自动设置：

- 地址：`http://127.0.0.1:5080`
- 环境：`Development`
- 持久化：`InMemory`

看到类似以下输出即表示启动成功：

```text
Now listening on: http://127.0.0.1:5080
Application started. Press Ctrl+C to shut down.
```

然后访问：

```text
http://127.0.0.1:5080
```

页面和 API 由同一个 ASP.NET Core 进程提供，无需再启动 Vue。按 `Ctrl+C` 停止服务。

可以在另一个 PowerShell 窗口检查 API：

```powershell
Invoke-RestMethod "http://127.0.0.1:5080/api/templates?page=1&pageSize=5"
```

本地 InMemory 模式的特点：

- 不需要 MySQL；
- 可以上传、扫描和预览 DOCX；
- 同时保留旧版演示 API，便于兼容既有测试；
- 数据只存在当前进程内，重启后清空；
- 数据库健康接口返回 `not_configured`/503 是正常现象。

## 4. 端口 5080 已被占用

如果看到：

```text
Failed to bind to address http://127.0.0.1:5080: address already in use
SocketException (10048)
```

说明已有程序正在监听 5080。先检查它是否就是已经启动的本项目：

```powershell
Get-NetTCPConnection -LocalPort 5080 -State Listen |
  Select-Object LocalAddress, LocalPort, OwningProcess
```

将输出的 `OwningProcess` 数字代入：

```powershell
$processIdToInspect = 12345
Get-Process -Id $processIdToInspect
```

如果浏览器打开 `http://127.0.0.1:5080` 已能正常使用，就不需要重复执行
`dotnet run`。

只有确认该 PID 是自己要关闭的旧开发进程时才执行：

```powershell
$processIdToStop = 12345
Stop-Process -Id $processIdToStop
```

也可以保留原进程，换一个端口：

```powershell
dotnet run --project .\src\WordTemplateBinding.Api `
  --urls http://127.0.0.1:5081
```

此时访问 `http://127.0.0.1:5081`。

## 5. 前端热更新开发

仓库已经提交了生产静态资源。只有修改 `frontend/src` 时才需要启动 Vite。

打开两个 PowerShell 窗口。

窗口 A，启动后端：

```powershell
cd D:\Code\WordTemplateBinding
dotnet run --project .\src\WordTemplateBinding.Api
```

窗口 B，启动前端：

```powershell
cd D:\Code\WordTemplateBinding\frontend
npm ci
npm run dev
```

访问 Vite 输出的地址，通常是：

```text
http://127.0.0.1:5173
```

Vite 会把 `/api` 代理到 `http://127.0.0.1:5080`。如果后端改用了 5081：

```powershell
$env:WTB_API_ORIGIN = "http://127.0.0.1:5081"
npm run dev
```

前端检查命令：

```powershell
npm run typecheck
npm test
npm run build
```

`npm run build` 会把生产资源写入：

```text
src/WordTemplateBinding.Api/wwwroot
```

构建完成后，对 ASP.NET Core 页面执行一次 `Ctrl+F5`，避免浏览器标签页继续运行旧
JavaScript。

## 6. 使用真实 MySQL

### 6.1 初始化 `report_platform`

应用不会自动建表或修改表结构。请先备份数据库，再由数据库管理员按场景执行仓库 SQL：

1. 新数据库先执行 `sql/report_platform_v2_schema.sql`；
2. 再执行一次
   `sql/report_platform_v2_to_v2_1_database_file_storage_migration.sql`；
3. 已经是 V2 的数据库只执行第二个迁移脚本；
4. 已经完成 V2.1 数据库文件存储迁移的数据库不要重复执行。

数据结构说明：

- `sql/report_platform_v2_1_database_design_and_dictionary.md`
- `sql/report_platform_v2_1_data_dictionary.md`

应用账号需要对平台所使用的 `rp_*` 表具备最小必要的读写权限。

### 6.2 本机 MySQL 联调

项目已配置 `UserSecretsId`。在 Development 环境中，可用 User Secrets 保存平台库
凭据，避免把密码写入仓库：

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
dotnet user-secrets set "Database:SslMode" "None" `
  --project .\src\WordTemplateBinding.Api
dotnet user-secrets set "ApplicationIdentity:DefaultActorUserId" "1" `
  --project .\src\WordTemplateBinding.Api
```

`SslMode=None` 仅适合不支持 TLS 的本机 MySQL。远程和生产环境应使用 `Required` 或更
严格的证书校验配置。

在当前 PowerShell 会话中切换为 MySQL 并启动：

```powershell
$env:Persistence__Mode = "MySql"
dotnet run --project .\src\WordTemplateBinding.Api
```

停止后清除本次会话覆盖：

```powershell
Remove-Item Env:Persistence__Mode -ErrorAction SilentlyContinue
```

检查平台库：

```powershell
Invoke-RestMethod "http://127.0.0.1:5080/api/system/database/health"
```

成功时 `status` 为 `healthy`。如果是 `not_configured` 或 `unavailable`，检查 Host、
端口、账号、密码、TLS、防火墙以及数据库授权。

查看或清理本地 User Secrets：

```powershell
dotnet user-secrets list --project .\src\WordTemplateBinding.Api
dotnet user-secrets clear --project .\src\WordTemplateBinding.Api
```

### 6.3 生产启动

发布或生产启动时不要使用 Development 启动配置，也不要依赖 User Secrets。使用部署
平台的密钥管理器注入环境变量：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Persistence__Mode = "MySql"
$env:Database__Host = "db.example.internal"
$env:Database__Port = "3306"
$env:Database__Database = "report_platform"
$env:Database__Username = "report_app"
$env:Database__Password = "<由密钥系统注入>"
$env:Database__SslMode = "Required"
$env:ApplicationIdentity__DefaultActorUserId = "1"

dotnet run --no-launch-profile `
  --project .\src\WordTemplateBinding.Api `
  --urls http://0.0.0.0:5080
```

生产配置默认值位于 `src/WordTemplateBinding.Api/appsettings.json`。其中
`Persistence:Mode` 默认为 `MySql`，但 Host、Username 和 Password 故意留空。

### 6.4 业务 MySQL 数据源

平台库账号与“报告要读取的业务数据库账号”是两套独立凭据。

数据连接记录只保存引用：

```text
credential_ref = config:DataSourceCredentials:schoolDb
```

对应账号密码由服务端配置或密钥系统注入：

```powershell
$env:DataSourceCredentials__schoolDb__Username = "reader"
$env:DataSourceCredentials__schoolDb__Password = "<由密钥系统注入>"
```

业务账号建议只授予目标 Schema 的 `SELECT` 以及必要的 `information_schema` 元数据
读取权限。系统不接受用户提交任意 SQL。

## 7. 发布

先构建前端，再发布后端：

```powershell
cd D:\Code\WordTemplateBinding\frontend
npm ci
npm run build

cd D:\Code\WordTemplateBinding
dotnet publish .\src\WordTemplateBinding.Api `
  --configuration Release `
  --output .\publish `
  -p:SkipFrontendBuild=true
```

发布目录已经包含 `wwwroot`。配置好生产环境变量后运行：

```powershell
.\publish\WordTemplateBinding.Api.exe --urls http://0.0.0.0:5080
```

如果不传 `-p:SkipFrontendBuild=true` 且 `frontend/node_modules` 存在，项目的 Publish
目标也会自动执行一次 `npm run build`。

## 8. 页面使用流程

1. 选择项目和章节，确定绑定配置归属。
2. 选择已有模板，或者保持“上传时新建模板”并上传 DOCX。
3. 选择已有模板后再次上传，会创建新的不可变版本。
4. 选择数据源并刷新快照，字段树来自最新 READY 快照。
5. 将字段拖到文档高亮、占位符或图表区域。
6. 查看单项预览并执行绑定校验。
7. 生成报告，或从当前绑定集导出复用模板。

前端网络请求使用数据库 `templateElementId`；DOCX 预览 DOM 定位使用当前版本的
`locatorId`，两者不要混用。

## 9. 模板标记

默认识别：

- `{{path}}` 和 `{{text:path}}` 显式占位符；
- `w:highlight w:val="yellow"` 黄色高亮；
- Run 级 `w:shd w:fill="FFFF00"` 等配置黄色底纹；
- 内容控件 `rtb-marker:*` Tag；
- Word 原生图表。

扫描范围包括正文、表格、页眉、页脚、脚注、尾注和文本框。没有任何标记的合法 DOCX
也能上传，状态为 `READY_WITH_WARNINGS`。

生产默认关闭全局整数和小数正则识别，避免把年份、页码和普通正文数字误判为绑定目标。
Development 兼容模式会开启这两项。

## 10. 常见问题

### 上传返回 405 Method Not Allowed

如果浏览器网络面板仍请求：

```text
POST /api/templates/upload
```

说明当前标签页还在运行旧版前端。正式持久化上传接口是：

```text
POST /api/templates
```

处理方法：

1. 确认后端是当前代码启动的；
2. 执行 `npm run build`，或使用仓库已提交的最新 `wwwroot`；
3. 在浏览器按 `Ctrl+F5`；
4. 必要时关闭旧标签页后重新打开。

### `/api/templates` 返回 500

通常是误以 Production/MySQL 模式启动，但数据库参数没有配置。快速体验请直接执行：

```powershell
dotnet run --project .\src\WordTemplateBinding.Api
```

不要添加 `--no-launch-profile`，这样会使用 Development/InMemory 配置。

### 数据库健康接口返回 503

- InMemory 模式下属于预期结果，不影响本地模板体验；
- MySQL 模式下请根据响应中的 `status`、`missingSettings` 和 `message` 检查配置。

### MySQL 报表或字段目录为空

检查：

- 数据连接类型是否为 `MYSQL`；
- `credential_ref` 是否对应服务端 `DataSourceCredentials` 配置；
- 业务账号能否读取目标 Schema、表/视图和 `information_schema`；
- 数据源是否已经执行刷新并产生 READY 快照；
- BLOB、Binary、JSON 等不可绑定字段会被排除。

### 前端改动没有生效

执行：

```powershell
cd .\frontend
npm run build
```

然后重新启动后端，并在浏览器按 `Ctrl+F5`。

## 11. 数据与安全约束

- 平台数据库只使用既有 `rp_*` 表，运行时不执行建表或改表。
- DOCX 内容保存在 `rp_file_object`、`rp_file_chunk` 和
  `rp_file_upload_session`，默认分片 4 MiB。
- `BIGINT UNSIGNED` 在后端使用 `ulong`，HTTP JSON 中数据库 ID 均为十进制字符串。
- 文件上传、下载和完整性校验按流处理，并验证分片及完整 SHA-256。
- 文件名不作为服务器路径，临时物化使用随机文件并在租约释放时删除。
- SQL 值全部参数化；动态标识符必须来自 `information_schema` 已验证对象。
- 数据样例最多 20 行，排除 BLOB/Binary，并限制单值与快照 JSON 大小。
- 密码、连接串、DOCX 正文和快照内容不会写入 API ProblemDetails。

## 12. 核心链路

```text
DOCX 上传
→ rp_file_object / rp_file_chunk / rp_file_upload_session
→ rp_template / rp_template_version
→ OpenXML 扫描正文、页眉、页脚、脚注、尾注、文本框和图表
→ rp_template_element
→ 项目 / 章节 / MySQL 业务数据源 / 快照 / 字段目录
→ rp_binding_set / rp_binding_item
→ 建议、校验、预览
→ 生成报告或导出复用模板
```

模板元素稳定身份优先使用内容控件 `rtb-marker:*` Tag，其次使用显式占位符上下文，
最后回退到结构化 Locator。重新扫描时稳定 `element_key` 会保留数据库元素 ID。

## 13. 进一步文档

- [第一、第二阶段架构设计](docs/phase1-design.md)
- [HTTP API](docs/api.md)
- [数据库设计与数据字典](sql/report_platform_v2_1_database_design_and_dictionary.md)
- [前端图表解析设计](docs/chart-analysis-design.md)
