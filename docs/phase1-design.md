# 第一阶段架构设计

## 1. 分层

### Core

Core 不依赖 ASP.NET Core HTTP 类型或 `IFormFile`，包含：

- 模板、扫描、定位、预览、绑定和数据字段模型；
- 模板存储、绑定存储、扫描、渲染、Schema、数据值和格式化接口；
- 可映射到 ProblemDetails 的业务异常；
- `TemplateWorkflowService`、`BindingWorkflowService`、`ReportWorkflowService`、`ReusableTemplateWorkflowService` 和 `TemplateAutoBindingResolver`。

### Infrastructure

Infrastructure 引用 Core 和 Open XML SDK，包含：

- 段落 Text 节点映射；
- 小数、整数和显式文字标记识别器；
- LocatorId 生成；
- 主文档扫描器；
- 报告与复用模板共用的 Word 局部范围替换服务；
- 可复用模板渲染器及版本化 CustomXmlPart Manifest 读写；
- 线程安全内存存储；
- 演示 Schema 和数据值；
- 依赖注入注册。

### API

API 负责：

- multipart 上传和 JSON 请求适配；
- DTO 与领域模型映射；
- Minimal API 端点；
- ProblemDetails 与状态码；
- 安全文件名和 DOCX 文件响应；
- 静态原生前端。

## 2. OpenXML 扫描

扫描器打开 `MainDocumentPart`，按文档顺序枚举正文和表格单元格中的 `Paragraph`，并按部件 URI 顺序扫描全部 FooterPart。存在 `TextBoxContent` 祖先的段落被过滤；页眉、脚注、尾注和文本框仍不在当前范围内。

每个段落构建：

```text
ParagraphTextMap
├── FullText
└── TextSegment[]
    ├── NodeIndex
    ├── StartOffset
    ├── Length
    └── TextNode
```

`FullText` 是段落中全部 `Text` 节点按文档顺序拼接的结果。正则只针对 `FullText` 执行，因此能识别跨 Run 的数字和文字标记。

默认启用 `DecimalNumberRecognizer`、`IntegerNumberRecognizer` 和 `ExplicitTextRecognizer`。文字必须使用 `{{text:示例文字}}` 显式标记。识别结果重叠时优先保留起点靠前、范围更长的候选，防止标记内部的数字被重复识别。

## 3. 结构化定位和 LocatorId

`TextLocator` 包含：

- `PartKind`
- `PartKey`
- `ParagraphIndex`
- `StartOffset`
- `Length`
- `OccurrenceIndex`
- `OriginalValue`
- `ContextHash`

当前文本定位范围：

- 正文使用 `PartKind = MainDocument`、`PartKey = /word/document.xml`；
- 页脚使用 `PartKind = Footer` 和具体 `/word/footerN.xml`；
- `OccurrenceIndex` 是段落内从 0 开始的匹配序号。

`ContextHash` 对匹配前后配置长度范围内的原始段落文本计算 SHA-256。

LocatorId 生成时对模板内容哈希和所有定位字段进行 UTF-8 长度前缀编码，再计算 SHA-256，最终输出无填充 Base64Url。该方案：

- 能区分相同值的不同位置；
- 避免 XML/Base64 标识过长；
- 不依赖 Run XML 完全相同；
- 相同模板重复扫描结果稳定。

## 4. 跨 Run 局部替换

报告生成与复用模板导出共用的文本替换流程：

```text
复制原始 byte[]
→ 新建可扩展 MemoryStream
→ 打开 DOCX 副本
→ 按 Locator 找到段落
→ 重建 ParagraphTextMap
→ 校验原值和 ContextHash
→ 同段落替换按 StartOffset 降序
→ 修改相关 Text.Text
→ 保存并返回副本
```

替换范围可能覆盖多个 Text 节点：

1. 首节点保留目标前缀并写入新值；
2. 中间节点只删除被覆盖字符；
3. 末节点保留目标之后的后缀；
4. 不删除 Run 或其他 XML 元素；
5. 新值继承首个 Run 的字体、字号、颜色和强调属性；
6. 必要时设置 `xml:space="preserve"`。

`OpenXmlTextReplacementService` 会先解析并校验所有目标部件和段落，再执行任何写入。它统一校验支持的 PartKind/PartKey、段落索引、范围、原始值、ContextHash 和范围重叠。校验失败时，报告或复用模板都不会返回部分结果。

## 5. 原模板不可变

`TemplateDocument` 使用私有字节数组：

- 构造时复制输入；
- 读取时返回新数组；
- 内存存储保存和返回模板快照；
- 报告渲染器和复用模板渲染器每次重新复制原始字节。

扫描结果与绑定关系独立保存。重新扫描先生成完整新结果，成功后才更新模板并删除失效绑定。

## 6. Schema 与数据值

内存 Schema 包含：

- 学生统计：平均成绩、人数、及格率；
- 报告信息：标题、日期、是否定稿；
- 学生数组示例，当前不可绑定；
- 100 个部门，每个部门 30 个小数指标。

字段路径保存在扁平字典中用于 O(1) 精确查找。搜索忽略大小写，最多返回 200 个扁平结果。

数据合并规则：

1. 获取完整演示值；
2. 使用请求 `values` 按字段路径覆盖；
3. 检查每个绑定字段是否仍有值；
4. 使用 `IDataValueFormatter` 按字段类型和 `InvariantCulture` 格式化。

Decimal 和 Integer 模拟数据允许绑定 Integer 或 Decimal 字段；String 模拟数据只允许绑定 String 字段。

## 7. 可复用模板导出与恢复

文本绑定导出为 `{{完整数据路径}}`。路径来自服务器已保存的 `TemplateBinding.DataPath`，禁止双花括号、换行和控制字符。已是相同占位符时只保留原值，因此重复导出幂等。未绑定扫描项不修改。

图表不写占位文本。`WordReusableTemplateRenderer` 保留 ChartPart，并在固定命名空间 `urn:word-template-binding:bindings:v1` 的 CustomXmlPart 中保存 `dataPath`、`chartPart`、`relationshipId`、`documentOrder` 和版本。只更新本系统有效清单，其他自定义 XML 部件保持不变。

上传/重扫流程为：

```text
扫描新 DOCX 并生成新 ContentHash/LocatorId
→ 读取固定命名空间 Manifest
→ 保存不可变模板快照
→ TemplateAutoBindingResolver 查询 Schema 的 Ordinal 精确索引
→ 标量字段恢复文本绑定
→ Array 字段按 Part+Relationship、Part+Order、Order 的优先级恢复图表绑定
→ 保存 TemplateImportSummary
→ API 返回已绑定状态和非阻断警告
```

同一 DataPath 可对应任意多个 Locator。字段不存在、大小写变化、类型不兼容、图表不能唯一定位或 Manifest 损坏时保持未绑定，模板仍可正常使用。

## 8. 前端数据流

```text
上传模板
→ 保存 TemplateResponse
→ 按 paragraph.text/startOffset/length 创建文本节点和高亮
→ 字段节点写入 Drag and Drop 自定义 MIME 数据
→ 高亮 drop 调用绑定 API
→ 重新获取模板状态并刷新三个区域
→ 可分别生成报告或导出复用模板并下载 Blob
```

上传响应中的 `importSummary` 用于状态栏展示恢复数量、未知路径和警告。前端不根据字符串自行创建绑定，只刷新后端返回的 `isBound/boundDataPath` 状态。

字段树默认只创建根节点 DOM，用户展开时才创建子节点。搜索由后端执行并使用 280 ms 防抖。

模板正文、字段路径和错误信息均通过 `textContent` 或 `createTextNode` 显示。

## 9. 后续扩展点

保留但未实现：

- 新的 `IMockDataRecognizer` 实现；
- Header、Footer、Footnote、Endnote、TextBox 段落来源；
- 数据库模板和绑定存储；
- JSON Schema、Excel 或 HTTP 数据来源；
- 显式模板语法编译器；
- PDF 页面图片和真实坐标预览。

第一阶段没有创建 Lexer、Parser、AST、循环渲染器或图片/图表空壳。
