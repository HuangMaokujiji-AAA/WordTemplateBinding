# 超长 Word 模板绑定范围拆分设计

> 适用仓库：`HuangMaokujiji-AAA/WordTemplateBinding`  
> 适用技术栈：.NET 7、ASP.NET Core Minimal API、Open XML SDK 3.0.2、MySqlConnector 2.6.1、Vue 3、docx-preview 0.4.0、JSZip 3.10.1、MySQL 8.0  
> 文档目标：把数百页 DOCX 按可协作的绑定范围拆分，同时保证最终报告仍严格基于原始完整模板生成，不破坏图表、页眉页脚、样式、编号和关系 ID。

---

## 1. 问题定义

当前仓库采用以下模型：

```text
逻辑模板 rp_template
  └─ 不可变模板版本 rp_template_version
       ├─ 原始 DOCX 文件
       ├─ 解析结果 parse_result_json
       └─ 模板元素 rp_template_element
```

绑定配置以章节为单位保存：

```text
rp_chapter
  └─ rp_binding_set
       └─ rp_binding_item
```

目前模板扫描、预览和报告生成仍以“完整 DOCX”为主要输入。对于 200～300 页模板，会出现：

1. 前端一次性渲染完整 DOCX，加载慢、内存占用高；
2. 所有绑定元素混在同一工作区，难以分工；
3. 多人只能围绕同一完整模板工作；
4. 如果真的把 DOCX 拆成多个文件再合并，会破坏 OpenXML 关系、编号和图表定位；
5. 绑定完成后缺少统一的片段校验、发布和最终生成流程。

本设计的核心结论是：

> **拆分绑定范围，不拆分原始模板；拆分工作视图，不拆分最终源文件。**

---

## 2. 总体设计

### 2.1 核心对象

新增 `TemplateSegment`，表示模板版本中的一个“虚拟片段”。

```text
完整 TemplateVersion
├─ Segment：封面
├─ Segment：总体概览
├─ Segment：专业监测结果
├─ Segment：改进建议
└─ Segment：附录
```

每个 Segment 只保存：

- 在原 DOCX 中的开始锚点；
- 在原 DOCX 中的结束锚点；
- 文档顺序；
- 所属模板元素；
- 预览缓存；
- 状态和版本。

Segment 不保存一份新的正式模板，不改变原始 DOCX。

### 2.2 最终生成原则

```text
各 Segment 独立绑定
    ↓
发布各 Segment 的 BindingSet/ChapterRevision
    ↓
RenderPlanBuilder 汇总所有绑定
    ↓
按原始文档顺序排序
    ↓
在完整原始 DOCX 副本上一次性渲染
    ↓
生成最终报告
```

因此，完成绑定后不需要重新拼接原模板。最终报告天然保持原模板结构。

---

## 3. 使用的方法和库

## 3.1 后端库

### Open XML SDK 3.0.2

仓库已使用：

```xml
<PackageVersion Include="DocumentFormat.OpenXml" Version="3.0.2" />
```

主要使用以下类型：

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
```

关键对象：

| 对象 | 用途 |
|---|---|
| `WordprocessingDocument` | 打开、复制和修改 DOCX |
| `MainDocumentPart` | 访问主文档 |
| `Body` | 枚举正文块级元素 |
| `Paragraph` | 段落范围和标题识别 |
| `Table` | 表格范围识别 |
| `SdtBlock` | 块级内容控件，推荐作为 Segment 边界 |
| `SdtRun` | 行内内容控件 |
| `SdtProperties` | 读取内容控件属性 |
| `Tag` | 保存稳定 Segment Key |
| `Alias` | 保存前端显示名称 |
| `BookmarkStart/BookmarkEnd` | 兼容旧模板的备用锚点 |
| `SectionProperties` | 分节和页面设置 |
| `OpenXmlReader` | 大文档流式扫描，减少 DOM 常驻内存 |

### MySqlConnector 2.6.1

继续使用现有 MySQL 驱动，不引入新的 ORM。

用途：

- 保存 Segment；
- 保存 Segment 与模板元素的归属；
- 保存预览文件引用；
- 事务内更新 Segment 状态；
- 通过 `row_version` 进行乐观锁控制。

### System.Text.Json

用途：

- 序列化锚点；
- 保存片段规则；
- 保存解析诊断；
- 保存自动拆分建议；
- 保存预览 Manifest。

不允许在 JSON 中保存任意可执行脚本。

## 3.2 前端库

### docx-preview 0.4.0

只渲染当前 Segment 的预览 DOCX，不再默认渲染完整 300 页模板。

### JSZip 3.10.1

用途限定为：

- 读取当前片段预览包；
- 辅助前端图表识别；
- 读取 `word/document.xml`、ChartPart 和媒体关系。

正式拆分、绑定和生成必须在后端完成，不能依赖浏览器 JSZip 修改正式模板。

### Vue 3

新增片段树、片段状态和懒加载工作区。

建议组件：

```text
TemplateSegmentTree.vue
TemplateSegmentWorkspace.vue
TemplateSegmentStatusBadge.vue
TemplateSegmentNavigator.vue
```

---

## 4. Segment 边界标记方法

## 4.1 首选：Word 内容控件

在 Word 或 WPS 中选中一个完整章节，插入“富文本内容控件”，设置：

```text
Tag: wtb:segment:major-monitoring
Alias: 专业监测结果
```

DOCX 中对应：

```xml
<w:sdt>
  <w:sdtPr>
    <w:alias w:val="专业监测结果"/>
    <w:tag w:val="wtb:segment:major-monitoring"/>
  </w:sdtPr>
  <w:sdtContent>
    ...
  </w:sdtContent>
</w:sdt>
```

优点：

1. 内容范围天然明确；
2. 用户可在 Word/WPS 中看到；
3. 内容移动后 Tag 不变；
4. 不依赖页码；
5. 可包含段落、表格、图表；
6. 与现有 `element_key` 的稳定 Tag 策略一致。

### 命名规范

```text
wtb:segment:{segment-key}
```

示例：

```text
wtb:segment:cover
wtb:segment:school-overview
wtb:segment:major-monitoring
wtb:segment:recommendations
wtb:segment:appendix
```

限制：

- `segment-key` 仅允许小写字母、数字和短横线；
- 同一 TemplateVersion 内必须唯一；
- 一旦发布，不因标题修改而变化；
- 不使用中文标题作为业务键。

## 4.2 备用：书签

旧模板无法插入内容控件时，可使用：

```text
WTB_SEG_START_major-monitoring
WTB_SEG_END_major-monitoring
```

后端读取 `BookmarkStart` 与 `BookmarkEnd`。

书签只作为兼容方案，因为：

- 用户容易误删；
- 跨复杂表格的范围处理较困难；
- 书签 ID 在复制过程中可能变化；
- Word 编辑行为可能自动调整边界。

## 4.3 自动建议：标题与分节符

对于完全未标记的模板，后端可根据以下信息给出拆分建议：

1. `Heading 1` / `标题 1`；
2. 大纲级别 `w:outlineLvl`；
3. 分节符 `w:sectPr`；
4. 分页符；
5. 标题编号模式；
6. 内容长度；
7. 图表密度；
8. 表格密度。

自动拆分只产生“建议”，不能直接发布为正式 Segment。

### 自动建议算法

```text
遍历 Body 中块级元素
→ 提取段落样式、文本、大纲级别、分节符
→ 标记候选起点
→ 计算每个候选区间的元素数量和绑定元素数量
→ 合并过短区间
→ 拆分过长区间
→ 返回 SegmentSuggestion[]
→ 用户确认后写入正式 Segment
```

推荐阈值仅作为初始配置：

```text
目标绑定元素数：20～80
最大绑定元素数：150
目标预览页数：10～30
```

不要使用固定页码作为最终边界。

---

## 5. 数据库设计

## 5.1 新增 `rp_template_segment`

```sql
CREATE TABLE rp_template_segment (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    template_version_id BIGINT UNSIGNED NOT NULL,
    parent_segment_id BIGINT UNSIGNED NULL,

    segment_key VARCHAR(128) NOT NULL,
    segment_name VARCHAR(255) NOT NULL,
    segment_type VARCHAR(32) NOT NULL DEFAULT 'SECTION',

    anchor_type VARCHAR(32) NOT NULL,
    start_anchor_json JSON NOT NULL,
    end_anchor_json JSON NULL,

    document_order_start INT UNSIGNED NOT NULL,
    document_order_end INT UNSIGNED NOT NULL,

    segment_status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
    segment_fingerprint CHAR(64) NULL,

    preview_file_object_id BIGINT UNSIGNED NULL,
    preview_status VARCHAR(32) NOT NULL DEFAULT 'NOT_CREATED',
    preview_error_message VARCHAR(1000) NULL,

    sort_no INT NOT NULL DEFAULT 0,
    row_version INT UNSIGNED NOT NULL DEFAULT 0,

    created_by BIGINT UNSIGNED NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by BIGINT UNSIGNED NULL,
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
        ON UPDATE CURRENT_TIMESTAMP(3),

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_template_segment (
        template_version_id,
        segment_key
    ),
    KEY idx_rp_segment_order (
        template_version_id,
        document_order_start
    ),
    KEY idx_rp_segment_parent (
        parent_segment_id,
        sort_no
    ),

    CONSTRAINT fk_rp_segment_version
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_rp_segment_parent
        FOREIGN KEY (parent_segment_id)
        REFERENCES rp_template_segment(id)
        ON DELETE RESTRICT,

    CONSTRAINT fk_rp_segment_preview_file
        FOREIGN KEY (preview_file_object_id)
        REFERENCES rp_file_object(id)
        ON DELETE SET NULL
);
```

## 5.2 修改 `rp_template_element`

推荐直接增加：

```sql
ALTER TABLE rp_template_element
    ADD COLUMN segment_id BIGINT UNSIGNED NULL AFTER template_version_id,
    ADD COLUMN segment_local_order INT UNSIGNED NOT NULL DEFAULT 0
        AFTER sort_no,
    ADD KEY idx_rp_element_segment (
        segment_id,
        segment_local_order
    ),
    ADD CONSTRAINT fk_rp_element_segment
        FOREIGN KEY (segment_id)
        REFERENCES rp_template_segment(id)
        ON DELETE SET NULL;
```

一个模板元素只归属于一个最内层 Segment。

页眉、页脚和全局字段可设置：

```text
segment_id = NULL
scope = GLOBAL
```

如果后续需要一个元素出现在多个工作视图，再改为中间表，不建议第一阶段增加复杂度。

## 5.3 Segment 锚点 JSON

内容控件锚点：

```json
{
  "partKind": "MainDocument",
  "partKey": "/word/document.xml",
  "locatorType": "CONTENT_CONTROL",
  "tag": "wtb:segment:major-monitoring",
  "sdtId": "172839",
  "contextHash": "sha256..."
}
```

书签锚点：

```json
{
  "partKind": "MainDocument",
  "partKey": "/word/document.xml",
  "locatorType": "BOOKMARK_RANGE",
  "startBookmarkName": "WTB_SEG_START_major-monitoring",
  "endBookmarkName": "WTB_SEG_END_major-monitoring",
  "contextHash": "sha256..."
}
```

---

## 6. 后端类设计

## 6.1 Core 模型

```csharp
public sealed record TemplateSegmentRecord
{
    public ulong Id { get; init; }
    public ulong TemplateVersionId { get; init; }
    public ulong? ParentSegmentId { get; init; }

    public string SegmentKey { get; init; } = string.Empty;
    public string SegmentName { get; init; } = string.Empty;
    public string SegmentType { get; init; } = "SECTION";

    public string AnchorType { get; init; } = string.Empty;
    public string StartAnchorJson { get; init; } = "{}";
    public string? EndAnchorJson { get; init; }

    public uint DocumentOrderStart { get; init; }
    public uint DocumentOrderEnd { get; init; }

    public string SegmentStatus { get; init; } = "DRAFT";
    public string? SegmentFingerprint { get; init; }

    public ulong? PreviewFileObjectId { get; init; }
    public string PreviewStatus { get; init; } = "NOT_CREATED";

    public int SortNo { get; init; }
    public uint RowVersion { get; init; }
}
```

## 6.2 Core 接口

```csharp
public interface ITemplateSegmentRepository
{
    Task<IReadOnlyList<TemplateSegmentRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken);

    Task<TemplateSegmentRecord?> GetAsync(
        ulong segmentId,
        CancellationToken cancellationToken);

    Task ReplaceForVersionAsync(
        ulong templateVersionId,
        IReadOnlyCollection<TemplateSegmentRecord> segments,
        CancellationToken cancellationToken);

    Task<TemplateSegmentRecord> UpdateAsync(
        ulong segmentId,
        uint expectedRowVersion,
        TemplateSegmentUpdate update,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IWordTemplateSegmentScanner
{
    Task<TemplateSegmentScanResult> ScanAsync(
        Stream docx,
        TemplateScanResult fullScanResult,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IWordSegmentPreviewRenderer
{
    Task<RenderedSegmentPreview> RenderAsync(
        string sourceDocxPath,
        TemplateSegmentRecord segment,
        CancellationToken cancellationToken);
}
```

## 6.3 Infrastructure 类

建议目录：

```text
src/WordTemplateBinding.Infrastructure/OpenXml/Segments/
├─ OpenXmlTemplateSegmentScanner.cs
├─ OpenXmlSegmentAnchorResolver.cs
├─ OpenXmlSegmentElementAssigner.cs
├─ OpenXmlSegmentPreviewRenderer.cs
├─ OpenXmlSegmentFingerprintBuilder.cs
└─ SegmentTagParser.cs
```

## 6.4 Core 服务

```text
TemplateSegmentService
├─ ScanAndPersistAsync
├─ ListAsync
├─ ConfirmSuggestionsAsync
├─ UpdateAsync
├─ GeneratePreviewAsync
└─ ValidateCoverageAsync
```

---

## 7. Segment 扫描算法

## 7.1 扫描顺序

```text
TemplateCatalogService.ParseAsync
  1. 扫描完整模板元素
  2. 扫描 Segment 边界
  3. 校验 Segment
  4. 为元素分配 Segment
  5. 保存 rp_template_segment
  6. 保存 rp_template_element.segment_id
  7. 保存解析摘要
```

## 7.2 内容控件扫描

优先使用 `OpenXmlReader` 流式读取：

```csharp
using WordprocessingDocument document =
    WordprocessingDocument.Open(path, false);

MainDocumentPart mainPart = document.MainDocumentPart
    ?? throw new InvalidDataException("缺少 MainDocumentPart。");

using OpenXmlReader reader = OpenXmlReader.Create(
    mainPart.Document.Body!);

uint documentOrder = 0;

while (reader.Read())
{
    if (reader.ElementType == typeof(SdtBlock) && reader.IsStartElement)
    {
        SdtBlock sdt = (SdtBlock)reader.LoadCurrentElement();
        string? tag = sdt.SdtProperties?
            .GetFirstChild<Tag>()?
            .Val?
            .Value;

        if (SegmentTagParser.TryParse(tag, out SegmentTag segmentTag))
        {
            // 保存边界、标题、顺序和指纹
        }
    }

    if (reader.IsStartElement)
    {
        documentOrder++;
    }
}
```

注意：

- `LoadCurrentElement()` 会加载当前内容控件子树；
- 不能对整份文档所有节点都调用；
- 大文档只在发现 Segment 时加载局部子树；
- 扫描完整模板元素时仍可沿用现有扫描器。

## 7.3 嵌套 Segment

允许：

```text
专业监测结果
├─ 总体情况
├─ 黄牌预警
└─ 优势特色
```

规则：

1. 内容控件可以嵌套；
2. 元素归属于最内层 Segment；
3. 父 Segment 用于树形导航和汇总；
4. 不能出现交叉范围；
5. 同级范围不能重叠；
6. `document_order_start <= document_order_end`。

## 7.4 元素归属算法

每个模板元素已有 Locator 和文档顺序信息。应扩展扫描结果，为文本和图表记录统一的：

```text
partKey
documentOrder
containerPath
```

归属规则：

```text
候选 Segment =
  同一 partKey
  且 segment.start <= element.order <= segment.end

选择范围最小、层级最深的 Segment。
```

对页眉页脚：

```text
PartKind = Header/Footer
segment_id = NULL
scope = GLOBAL
```

第一阶段不允许在一个正文 Segment 中独立编辑页眉页脚。

---

## 8. Segment 预览实现

## 8.1 推荐方法：复制完整包后删除非目标正文

不要从空 DOCX 开始逐个复制样式、图片和图表。MVP 使用：

```text
复制原始 DOCX 到临时文件
→ 打开副本
→ 保留目标 Segment 的正文元素
→ 删除其他正文块
→ 保留原有 Styles、Theme、Numbering、ChartPart、ImagePart
→ 保存为预览 DOCX
```

优点：

- 不需要自己重建复杂关系；
- 图表和图片保持可用；
- 页眉页脚、主题和字体保持一致；
- 实现风险最低。

示意代码：

```csharp
public async Task<RenderedSegmentPreview> RenderAsync(
    string sourcePath,
    TemplateSegmentRecord segment,
    CancellationToken cancellationToken)
{
    string tempPath = Path.Combine(
        Path.GetTempPath(),
        $"{Guid.NewGuid():N}.docx");

    File.Copy(sourcePath, tempPath, overwrite: false);

    using WordprocessingDocument document =
        WordprocessingDocument.Open(tempPath, true);

    Body body = document.MainDocumentPart!
        .Document
        .Body!;

    OpenXmlElement target = ResolveSegmentRoot(body, segment);

    List<OpenXmlElement> preserved = new()
    {
        target.CloneNode(true)
    };

    SectionProperties? finalSectPr =
        body.GetFirstChild<SectionProperties>()?.CloneNode(true)
        as SectionProperties;

    body.RemoveAllChildren();

    foreach (OpenXmlElement element in preserved)
    {
        body.Append(element);
    }

    if (finalSectPr is not null)
    {
        body.Append(finalSectPr);
    }

    document.MainDocumentPart.Document.Save();

    byte[] bytes = await File.ReadAllBytesAsync(
        tempPath,
        cancellationToken);

    File.Delete(tempPath);

    return new RenderedSegmentPreview(bytes, "segment-preview.docx");
}
```

实际代码还应：

- 使用 `TemporaryFileLease`；
- 处理 Segment 本身为多个兄弟节点的情况；
- 保留段前分页和分节属性；
- 处理空正文；
- 进行 ZIP/DOCX 完整性校验。

## 8.2 后续优化：清理未使用部件

初期不必清理未使用媒体，因为预览缓存可接受一定冗余。

性能压测后，再实现：

```text
OpenXmlUnusedPartCleaner
```

清理：

- 未引用 ImagePart；
- 未引用 ChartPart；
- 未引用 EmbeddedPackagePart；
- 未引用 ExternalRelationship。

不要在第一阶段做激进清理，避免破坏预览。

## 8.3 预览缓存键

```text
SHA256(
  templateVersion.contentHash
  + segment.segmentFingerprint
  + previewRendererVersion
)
```

如果缓存键相同，直接复用 `preview_file_object_id`。

---

## 9. API 设计

## 9.1 获取片段

```http
GET /api/template-versions/{versionId}/segments
```

响应：

```json
{
  "items": [
    {
      "id": "21",
      "segmentKey": "major-monitoring",
      "segmentName": "专业监测结果",
      "parentSegmentId": null,
      "documentOrderStart": 120,
      "documentOrderEnd": 480,
      "elementCount": 67,
      "bindingProgress": {
        "total": 67,
        "bound": 49,
        "requiredMissing": 3
      },
      "previewStatus": "READY",
      "rowVersion": 4
    }
  ]
}
```

## 9.2 获取预览

```http
GET /api/template-segments/{segmentId}/preview
```

返回 DOCX 文件流，或返回：

```http
202 Accepted
```

表示预览正在生成。

第一阶段也可以同步生成，但正式环境建议写入文件对象后返回。

## 9.3 更新片段

```http
PATCH /api/template-segments/{segmentId}
If-Match: "4"
```

请求：

```json
{
  "segmentName": "专业监测分析",
  "sortNo": 300
}
```

## 9.4 确认自动拆分建议

```http
POST /api/template-versions/{versionId}/segment-suggestions/confirm
```

---

## 10. 前端工作区设计

## 10.1 页面布局

```text
左侧：Segment 树
中间：当前 Segment 的 docx-preview
右侧：字段树、绑定属性、校验结果
顶部：锁状态、进度、上一片段、下一片段
```

## 10.2 懒加载

只有用户点击 Segment 时才：

1. 请求 Segment 元数据；
2. 请求预览 DOCX；
3. 请求 Segment 内模板元素；
4. 请求该 Segment 的绑定项；
5. 调用 docx-preview；
6. 执行 Locator 装饰。

切换片段时：

- 销毁 ECharts 实例；
- 释放 Blob URL；
- 清理 DOM；
- 取消旧请求；
- 保留最近 2～3 个片段缓存。

## 10.3 前端路由

```text
/workspace/:projectId/:chapterId/:segmentId
```

Segment ID 必须进入 URL，便于刷新和协作分享。

---

## 11. 最终报告生成

## 11.1 RenderPlan

新增：

```csharp
public sealed record ReportRenderPlan
{
    public ulong ProjectId { get; init; }
    public ulong TemplateVersionId { get; init; }
    public IReadOnlyList<SegmentRenderPlan> Segments { get; init; }
        = Array.Empty<SegmentRenderPlan>();
}

public sealed record SegmentRenderPlan
{
    public ulong SegmentId { get; init; }
    public ulong ChapterRevisionId { get; init; }
    public ulong BindingSetId { get; init; }
    public uint DocumentOrderStart { get; init; }
    public IReadOnlyList<TemplateBinding> Bindings { get; init; }
        = Array.Empty<TemplateBinding>();
}
```

## 11.2 汇总流程

```text
ProjectReportGenerationService.GenerateAsync
  1. 验证项目状态
  2. 读取固定 TemplateVersion
  3. 读取启用 Segment/Chapter
  4. 每个 Segment 必须有已发布 ChapterRevision
  5. 读取固定 BindingSet
  6. 固定 DataSnapshot
  7. 校验 Locator 唯一性
  8. 按文档顺序合并绑定
  9. 调用 WordReportRenderer 一次
 10. 保存 Artifact
```

不能：

- 在最终生成时自动取最新模板版本；
- 在最终生成时自动取最新绑定草稿；
- 在最终生成中途刷新数据源；
- 生成到一半接受绑定修改。

---

## 12. 什么时候使用物理 DOCX 拼接

只有以下场景使用物理拼接：

1. 各章节原本就是独立 DOCX；
2. 不同部门维护完全不同的模板；
3. 每个章节需要独立并行生成；
4. 章节可以独立发布；
5. 主模板只负责封面、目录和样式。

此时新增：

```text
OpenXmlDocumentAssembler
StyleImportService
NumberingRemapper
RelationshipPartCloner
BookmarkIdRemapper
DrawingIdRemapper
```

但这属于后续阶段，不应替代虚拟 Segment。

---

## 13. 异常与诊断

错误码建议：

| 错误码 | 含义 |
|---|---|
| `segment_anchor_not_found` | 找不到内容控件或书签 |
| `segment_anchor_duplicated` | Segment Tag 重复 |
| `segment_range_invalid` | 开始和结束顺序错误 |
| `segment_range_overlap` | 同级范围交叉 |
| `segment_element_unassigned` | 元素不属于任何片段 |
| `segment_preview_failed` | 预览生成失败 |
| `segment_fingerprint_changed` | 模板结构变化导致片段失效 |
| `segment_binding_incomplete` | 片段绑定未完成 |
| `segment_revision_not_published` | 最终生成使用了未发布片段 |

所有解析警告写入 `parse_result_json`，不要因为单个 Segment 失败而删除原模板版本。

---

## 14. 测试设计

## 14.1 单元测试

新增：

```text
TemplateSegmentScannerTests
SegmentTagParserTests
SegmentElementAssignerTests
SegmentCoverageValidatorTests
SegmentPreviewRendererTests
SegmentFingerprintTests
```

至少覆盖：

1. 单个内容控件；
2. 多个同级 Segment；
3. 嵌套 Segment；
4. 重复 Tag；
5. 交叉书签；
6. Segment 中含表格；
7. Segment 中含图片；
8. Segment 中含图表；
9. Segment 中含分节符；
10. 未分配元素；
11. 页眉页脚全局元素；
12. 模板重扫后指纹变化。

## 14.2 集成测试

现有 InMemory 集成测试不能覆盖 MySQL 约束。建议增加独立数据库测试项目，并可选使用：

```text
Testcontainers.MySql
```

验证：

- 唯一键；
- 外键；
- 事务；
- `row_version` 冲突；
- Segment 替换；
- 大文件预览缓存。

## 14.3 回归测试

必须保证现有能力不退化：

```text
dotnet test
cd frontend
npm run typecheck
npm test
npm run build
```

---

## 15. 分阶段实施

### 阶段 A：稳定 Segment 标记

- 增加数据表；
- 扫描内容控件 Tag；
- 保存 Segment；
- 给模板元素分配 `segment_id`；
- 增加 Segment 列表 API；
- 不改报告生成。

### 阶段 B：片段预览与工作区

- 生成片段预览 DOCX；
- 前端 Segment 树；
- 按 Segment 加载元素和绑定；
- 当前片段进度统计；
- 保留完整模板生成方式。

### 阶段 C：片段发布与统一 RenderPlan

- BindingSet 版本化；
- ChapterRevision；
- Segment 发布校验；
- RenderPlanBuilder；
- 完整模板一次性生成。

### 阶段 D：性能优化

- `OpenXmlReader` 流式扫描；
- 预览缓存；
- 未使用部件清理；
- 异步生成任务；
- Artifact 流式写入数据库分片。

---

## 16. 验收标准

完成后应满足：

1. 上传 300 页 DOCX 后，原始模板字节保持不可变；
2. 用户可按章节查看和绑定，不加载完整 300 页；
3. 每个模板元素稳定归属于某个 Segment；
4. 多个 Segment 可分别保存、校验和发布；
5. 修改一个 Segment 不会改变其他 Segment 的绑定；
6. 最终报告从完整原模板生成；
7. 图表、图片、页眉页脚、编号和样式不因虚拟拆分变化；
8. Segment Tag 重复或范围异常时有明确诊断；
9. 模板重扫后能检测 Segment 指纹变化；
10. 原有单模板生成与复用模板导出功能仍可使用。
