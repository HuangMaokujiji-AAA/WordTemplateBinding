# 第一、第二阶段架构设计

## 1. 分层与持久化模式

### Core

Core 定义持久化无关的领域模型、`ulong` 数据库 ID、仓储接口和业务编排：

- `TemplateCatalogService`：逻辑模板、版本、解析和重扫；
- `ProjectChapterService`：项目与章节；
- `DataConnectionService`：业务连接、连接测试和元数据浏览；
- `DataSourceWorkspaceService`：数据源、快照和字段目录；
- `BindingWorkspaceService`：绑定草稿、明细、建议、校验和单项预览；
- `BindingCandidateResolver`：高置信度候选恢复；
- `BindingSetDocumentService`：基于固定模板版本与绑定集生成报告/复用模板。

Core 不依赖 ASP.NET Core、MySQL 驱动或 `IFormFile`。

### Infrastructure

Infrastructure 提供：

- MySQL `rp_*` 仓储；
- 数据库分片文件服务；
- 开发/测试用内存适配器；
- 业务 MySQL 只读连接工厂与 `information_schema` 内省；
- OpenXML 扫描、定位、图表分析、文字/图表渲染和复用模板 Manifest；
- 条件式依赖注入。

### API

API 负责 multipart/JSON 适配、数据库 ID 字符串解析、ProblemDetails、文件流响应和 Minimal API 路由。API DTO 不输出 JSON 数值型数据库 ID。

### Vue

Vue 管理项目、章节、模板、数据源和绑定集上下文。`templateElementId` 用于绑定 API；`locatorId` 用于 `docx-preview` 渲染后的 DOM 标记、选中和拖放。

## 2. 数据库文件服务

`IFileStorageService` 的 MySQL 实现只使用：

- `rp_file_object`：元数据、状态、完整哈希和分片参数；
- `rp_file_chunk`：4 MiB `MEDIUMBLOB` 分片；
- `rp_file_upload_session`：上传进度、过期、校验与失败状态。

上传状态机：

```text
file object: UPLOADING → VERIFYING → READY
upload session: UPLOADING → VERIFYING → COMPLETED
失败/取消: FAILED 或 CANCELLED
```

完成条件：

- 实际分片数等于 `expected_chunks`；
- 序号从 0 连续到 `total_chunks - 1`；
- 分片字节和等于 `file_size`；
- 每片写入时计算 SHA-256；
- 重组流完整 SHA-256 与声明值（如有）一致。

下载按 `chunk_no` 排序读取，使用 `CommandBehavior.SequentialAccess` 和 `GetStream`，边写响应边复算完整哈希。OpenXML 需要可定位流时，文件服务物化到随机临时文件，调用结束后通过 `TemporaryFileLease` 删除。

## 3. 模板、版本与元素

逻辑模板保存于 `rp_template`，每次 DOCX 上传创建不可变 `rp_template_version`。文件进入数据库文件服务后才创建版本；解析期间状态更新为 `PARSING`，成功为 `READY` 或 `READY_WITH_WARNINGS`，失败为 `FAILED`。

解析结果保存于 `parse_result_json`，可绑定目标逐条写入 `rp_template_element`：

- `locator_json` 保存当前版本的 `locatorId` 与结构化 OpenXML 定位；
- `binding_schema_json` 保存允许类型、目标属性和身份策略；
- `element_key` 优先取内容控件稳定 Tag，其次取显式占位符上下文，最后取 Locator；
- 图表元素保存 ChartPart、关系 ID 和文档顺序。

没有标记是合法状态：版本保存、元素数为 0，并记录 `NO_BINDABLE_ELEMENTS` 警告。

## 4. 扫描识别

扫描器以可读、可定位流打开 DOCX，先流式计算内容哈希，再扫描：

- `/word/document.xml` 正文和表格；
- HeaderPart；
- FooterPart；
- FootnotesPart；
- EndnotesPart；
- 各部件内 TextBoxContent；
- 主文档 Word 原生 ChartPart。

文本框段落从普通部件段落枚举中排除，再以独立 `PartKind=TextBox` 和 `partKey#textbox:n` 扫描，避免重复。

识别器按配置注册。优先级：

```text
显式占位符 > 黄色高亮/底纹 > 自动小数 > 自动整数
```

黄色底纹只接受配置中的明确 RGB `w:fill`；无法安全解析的主题色不自动识别，并产生警告。

## 5. 项目、业务数据源与快照

项目和章节分别使用 `rp_project`、`rp_chapter`。创建项目需要配置服务端行为人 ID，以满足数据字典中 `created_by NOT NULL`。

`rp_data_connection.config_json` 只保存 Host、Port、Database、SslMode 等非敏感信息；`credential_ref` 目前只接受 `config:` 引用。连接测试结果写回 `last_test_result`，异常对外只返回安全摘要。

数据库数据源在 `rp_data_source.config_json` 中保存已选择的 schema、对象类型和对象名。对象列表和列元数据来自 `information_schema`。读取样例前再次确认对象存在且类型允许，再对经过反引号转义的标识符执行固定形态：

```sql
SELECT <validated columns>
FROM `<validated schema>`.`<validated table or view>`
LIMIT @limit
```

不接收用户 SQL、WHERE、ORDER BY、函数或表达式。

刷新创建不可变 `rp_data_snapshot`，内容限制为最多 20 行的小型 JSON；字段目录写入 `rp_data_field`。Binary/BLOB 不进入样例和可绑定字段。

## 6. 绑定

绑定配置以章节为单位保存：

- `rp_binding_set` 固定 `template_version_id`；
- `rp_binding_item` 的目标是 `template_element_id + target_property`；
- 草稿可修改，写入后校验状态重置；
- 模板元素必须属于绑定集固定版本；
- 数据源必须与章节属于同一项目；
- 字段必须来自最新 READY 快照并可绑定；
- 文本拒绝 Array/Object/Binary，数字模板元素只接受 Integer/Decimal，图表只接受 Array；
- `target_property` 使用白名单，不执行任意表达式。

建议评分由显示名、字段末级名、归一化名称、配置同义词和类型兼容性组成。候选恢复只自动保存高分且无并列的结果。

生成报告前执行全量校验。报告与复用模板均从数据库文件服务取得固定版本 DOCX，并使用绑定项对应快照值，保证输入可复现。

## 7. 兼容模式

生产 `appsettings.json` 默认 `MySql`。`appsettings.Development.json` 使用 `InMemory`，保留旧 GUID API 供已有演示与回归测试使用；正式工作区始终调用数据库 ID API。内存适配器与 MySQL 仓储实现同一组新接口，因此核心工作流测试不依赖远程数据库。

## 8. 测试边界

自动化覆盖：

- 跨 Run、高亮、`w:shd`、页脚、图表和损坏 DOCX；
- 无标记版本；
- 文件分片、哈希、复制和临时物化契约；
- 模板元素、项目章节、快照字段、绑定、建议、校验、预览和报告；
- 正式 HTTP 数值字符串 ID、文件下载和绑定集；
- 旧报告/复用模板/图表往返；
- Vue 类型检查、组件与 OOXML 图表解析。

真实 MySQL 部署仍需在目标 `report_platform` 上执行连接、权限、事务竞争、大文件和恢复演练；应用不会为测试自动建表或改表。
