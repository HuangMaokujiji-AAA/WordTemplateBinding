
# WordTemplateBinding 超长模板绑定范围拆分功能完整实施任务

你现在需要修改当前仓库，实现“超长 Word 模板按绑定范围虚拟拆分、分片预览、分片独立绑定，最终仍基于完整原始模板生成报告”的完整功能。

## 一、项目与参考文档

当前项目仓库：

```text
https://github.com/HuangMaokujiji-AAA/WordTemplateBinding
```

核心技术栈：

```text
后端：
- .NET 7
- ASP.NET Core Minimal API
- Open XML SDK 3.0.2
- MySqlConnector 2.6.1
- MySQL 8.0
- System.Text.Json

前端：
- Vue 3
- TypeScript
- Vite
- docx-preview 0.4.0
- JSZip 3.10.1
- ECharts 6.1.0
- Vitest
```

本次任务的主要设计依据是：

```text
01-template-binding-scope-splitting.md
```

开始修改前，必须完整阅读该文档，并结合当前仓库实际代码进行实现。

如果设计文档与当前代码细节存在冲突：

1. 优先保证现有项目可以正常编译、运行和测试；
2. 保留设计文档的核心架构意图；
3. 不要机械照抄文档中的类名或 SQL；
4. 应当复用当前仓库已有模型、服务、仓储、文件服务、异常体系和 API 风格；
5. 在最终说明中列出实际实现与设计文档之间的合理差异。

---

# 二、核心业务目标

需要解决的是数百页 Word 模板难以一次性预览、绑定和多人分工的问题。

必须采用：

```text
拆分绑定范围，不拆分原始模板；
拆分工作视图，不拆分最终源文件。
```

禁止把原始 DOCX 真正拆成多个正式 DOCX，然后再通过普通文档合并恢复。

正确架构应当是：

```text
完整不可变 TemplateVersion
├─ TemplateSegment：封面
├─ TemplateSegment：总体概览
├─ TemplateSegment：专业监测结果
├─ TemplateSegment：改进建议
└─ TemplateSegment：附录
```

每个 `TemplateSegment` 只是完整模板中的一个逻辑范围，保存：

* 稳定业务键；
* 显示名称；
* 开始和结束锚点；
* 文档顺序；
* 父子关系；
* 所属模板元素；
* 预览缓存；
* 结构指纹；
* 状态；
* 乐观锁版本。

最终报告生成时：

```text
读取所有片段的绑定配置
→ 按完整模板中的文档顺序汇总
→ 在完整原始 DOCX 副本上一次性执行所有绑定
→ 输出最终完整报告
```

不要把片段预览 DOCX 当作正式模板或最终组装来源。

---

# 三、开始实施前的代码调查要求

先进行针对性代码调查，不要无目的地扫描整个仓库。

重点检查并理解以下内容：

```text
src/WordTemplateBinding.Core
src/WordTemplateBinding.Infrastructure
src/WordTemplateBinding.Api
frontend/src
sql
tests
docs
```

重点定位：

```text
TemplateCatalogService
BindingWorkspaceService
BindingSetDocumentService
ProjectChapterService
WordReportRenderer
WordReusableTemplateRenderer
OpenXML 模板扫描器
TemplateElementIdentityResolver
IFileStorageService
TemporaryFileLease
MySQL Repository 实现
WorkspaceEndpoints
PersistentTemplateEndpoints
前端 Workspace 页面
docx-preview 调用位置
绑定 Locator 装饰逻辑
```

确认以下现有数据结构和行为：

* `rp_template`
* `rp_template_version`
* `rp_template_element`
* `rp_project`
* `rp_chapter`
* `rp_binding_set`
* `rp_binding_item`
* `rp_file_object`
* `rp_file_chunk`
* `parse_result_json`
* `locator_json`
* `binding_schema_json`

先输出一个简短实施计划，然后直接开始修改代码。

除非遇到无法根据代码合理判断的致命歧义，否则不要中途反复询问。

---

# 四、实施范围

本次需要完成以下闭环：

```text
1. 数据库增加模板片段模型
2. 后端识别 Word 内容控件定义的片段
3. 将模板元素归属到对应片段
4. 提供片段列表与详情 API
5. 为片段生成独立预览 DOCX
6. 前端增加片段树和片段切换
7. 前端只加载当前片段预览
8. 当前片段只展示自己的可绑定元素和绑定状态
9. 保持现有完整模板生成报告功能兼容
10. 最终报告仍从完整原始 DOCX 生成
11. 增加单元测试、集成测试和前端测试
12. 增加数据库迁移和必要文档
```

本次暂时不要实现：

```text
- 多人协作租约锁
- Repeat Block
- 循环表格
- 小组件 Blueprint
- 独立章节 DOCX 的物理合并
- Redis
- 消息队列
- 商业 Word SDK
```

这些属于后续阶段。

但当前设计应为后续锁、重复块和组件复用保留扩展空间。

---

# 五、片段标记协议

## 5.1 首选标记：Word 块级内容控件

使用 WordprocessingML 块级内容控件：

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

Tag 协议：

```text
wtb:segment:{segment-key}
```

例如：

```text
wtb:segment:cover
wtb:segment:school-overview
wtb:segment:major-monitoring
wtb:segment:recommendations
wtb:segment:appendix
```

规则：

```text
- segment-key 在同一个 template_version 内唯一
- 仅允许小写字母、数字和短横线
- 标题修改不能改变 segment-key
- segment-key 不能使用数据库自增 ID
- 显示名称优先读取 w:alias
- 没有 alias 时可从第一个非空段落推导
```

## 5.2 兼容策略

第一版至少完整支持内容控件。

如果实现成本合理，可以增加书签兼容：

```text
WTB_SEG_START_{segment-key}
WTB_SEG_END_{segment-key}
```

但不要为了书签兼容拖慢核心闭环。

如果暂时不实现书签，必须在文档和诊断中明确说明。

## 5.3 未标记模板

没有 Segment 标记的旧模板仍然必须可用。

建议自动创建一个虚拟根片段：

```text
segment_key = full-document
segment_name = 完整模板
segment_type = ROOT
```

这样旧模板可以继续使用现有完整工作区，不得因没有 Segment 而上传失败。

第一版不必自动分析标题生成正式拆分建议。

---

# 六、数据库修改

根据现有 SQL 命名、字符集、索引、外键和迁移风格，新建迁移文件。

建议增加：

```text
rp_template_segment
```

核心字段至少包括：

```text
id
template_version_id
parent_segment_id
segment_key
segment_name
segment_type
anchor_type
start_anchor_json
end_anchor_json
document_order_start
document_order_end
segment_status
segment_fingerprint
preview_file_object_id
preview_status
preview_error_message
sort_no
row_version
created_by
created_at
updated_by
updated_at
```

必须包含：

```text
PRIMARY KEY(id)
UNIQUE(template_version_id, segment_key)
INDEX(template_version_id, document_order_start)
FOREIGN KEY template_version_id
FOREIGN KEY parent_segment_id
FOREIGN KEY preview_file_object_id
```

给 `rp_template_element` 增加：

```text
segment_id
segment_local_order
```

要求：

```text
- segment_id 可空，用于页眉、页脚等全局元素
- 正文元素优先归属最内层 Segment
- 迁移脚本必须兼容已有数据库
- 不要删除已有数据
- 不要修改已有模板版本不可变原则
```

同时更新：

```text
sql/report_platform_v2_1_database_design_and_dictionary.md
```

或仓库中实际使用的数据字典文件。

---

# 七、Core 层设计

根据当前项目风格新增或扩展以下领域模型。

推荐但不强制完全照搬的模型：

```csharp
TemplateSegmentRecord
TemplateSegmentAnchor
TemplateSegmentView
TemplateSegmentScanResult
TemplateSegmentDiagnostic
TemplateSegmentPreviewResult
```

推荐接口：

```csharp
ITemplateSegmentRepository
IWordTemplateSegmentScanner
IWordSegmentPreviewRenderer
```

建议增加服务：

```csharp
TemplateSegmentService
```

职责：

```text
- 查询模板版本片段
- 查询单个片段
- 扫描并保存片段
- 将模板元素归属到片段
- 校验片段结构
- 生成或获取片段预览
- 统计片段绑定进度
```

Core 层禁止直接依赖：

```text
- ASP.NET Core
- IFormFile
- MySqlConnector
- DocumentFormat.OpenXml
- Vue
```

OpenXML 具体实现放在 Infrastructure。

---

# 八、OpenXML 片段扫描实现

建议新增目录：

```text
src/WordTemplateBinding.Infrastructure/OpenXml/Segments
```

可新增：

```text
OpenXmlTemplateSegmentScanner
SegmentTagParser
OpenXmlSegmentAnchorResolver
OpenXmlSegmentElementAssigner
OpenXmlSegmentFingerprintBuilder
OpenXmlSegmentPreviewRenderer
```

## 8.1 扫描内容控件

使用 Open XML SDK：

```csharp
WordprocessingDocument
MainDocumentPart
Body
SdtBlock
SdtProperties
Tag
Alias
OpenXmlReader
```

优先使用 `OpenXmlReader` 或有边界的 DOM 遍历，避免无必要地把超大文档中的全部内容复制多次。

但不要为了流式扫描牺牲正确性。如果现有扫描器已经完整加载文档，可以在保证可维护性的前提下复用现有打开流程。

扫描结果至少包含：

```text
segmentKey
segmentName
parentSegmentKey
anchorType
partKey
documentOrderStart
documentOrderEnd
fingerprint
diagnostics
```

## 8.2 嵌套 Segment

允许：

```text
专业监测结果
├─ 总体情况
├─ 黄牌预警
└─ 优势特色
```

规则：

```text
- 内容控件可嵌套
- 元素归属最内层 Segment
- 父 Segment 用于树形导航
- 同级 Segment 不能交叉
- segment-key 不能重复
- Segment 不允许形成循环父子关系
```

## 8.3 结构诊断

至少识别：

```text
SEGMENT_TAG_INVALID
SEGMENT_TAG_DUPLICATED
SEGMENT_ALIAS_MISSING
SEGMENT_RANGE_INVALID
SEGMENT_OVERLAP
SEGMENT_EMPTY
SEGMENT_ELEMENT_UNASSIGNED
```

除严重破坏文档结构的错误外，尽量使用：

```text
READY_WITH_WARNINGS
```

不要因为一个 Segment 缺少 Alias 就让整个模板版本解析失败。

---

# 九、模板元素归属

扩展模板元素解析结果，使每个正文元素拥有统一文档顺序。

至少考虑：

```text
文本元素
表格中的文本元素
图表元素
文本框中的元素
页眉
页脚
脚注
尾注
```

归属原则：

```text
正文元素：
选择同一 Part 中覆盖该元素，
并且层级最深、范围最小的 Segment。

全局元素：
页眉、页脚、脚注、尾注等暂时 segment_id = NULL。
```

不要根据页码归属，因为页码会随数据替换和内容扩展变化。

建议在现有 `TemplateCatalogService.BuildElements` 或等效流程中加入：

```text
Segment 扫描
→ Element 顺序计算
→ Segment 分配
→ 保存 TemplateElement.segment_id
```

不要创建第二套重复的模板元素扫描器。

---

# 十、片段预览实现

## 10.1 预览不是正式模板

预览文件只用于前端 `docx-preview`，必须标记为临时或缓存产物。

不得：

```text
- 用片段预览文件生成最终报告
- 用片段预览文件创建新的模板版本
- 将片段预览当作用户正式上传模板
```

## 10.2 推荐实现

采用：

```text
复制完整原始 DOCX 到临时文件
→ 打开副本
→ 只保留目标 Segment 正文
→ 保留原文档的 Style、Theme、Numbering、图片、图表和关系
→ 保存预览 DOCX
→ 存入现有数据库文件服务
```

优先复用：

```text
IFileStorageService
TemporaryFileLease
rp_file_object
rp_file_chunk
```

不要新增本地永久目录。

## 10.3 预览文件清理

第一版可以保留未使用的媒体和 ChartPart，以保证关系完整。

不要在第一版中做激进的未使用部件清理。

## 10.4 预览缓存

预览缓存键应由以下内容组成：

```text
template content hash
segment fingerprint
preview renderer version
```

如果缓存未失效，直接复用 `preview_file_object_id`。

模板重扫或 Segment 指纹变化时，旧预览必须失效。

## 10.5 分节属性

片段预览需要尽量保留：

```text
页面方向
页边距
纸张大小
分页符
分节符
页眉页脚引用
```

如果目标 Segment 中没有最终 `sectPr`，可以复制原模板中适用的节属性用于预览。

不得修改正式原始 DOCX。

---

# 十一、API 设计

遵循当前 Minimal API、DTO、ProblemDetails 和数据库 ID 字符串输出风格。

至少增加：

## 11.1 片段列表

```http
GET /api/template-versions/{versionId}/segments
```

返回：

```json
{
  "items": [
    {
      "id": "21",
      "templateVersionId": "8",
      "parentSegmentId": null,
      "segmentKey": "major-monitoring",
      "segmentName": "专业监测结果",
      "segmentType": "SECTION",
      "documentOrderStart": 120,
      "documentOrderEnd": 480,
      "elementCount": 67,
      "previewStatus": "READY",
      "bindingProgress": {
        "total": 67,
        "bound": 49,
        "requiredMissing": 3
      },
      "rowVersion": 4
    }
  ]
}
```

## 11.2 单个片段

```http
GET /api/template-segments/{segmentId}
```

## 11.3 片段预览

```http
GET /api/template-segments/{segmentId}/preview
```

如果预览不存在，可以：

```text
第一版：同步生成并返回
```

或：

```text
生成后保存文件对象，再返回文件
```

不必为了预览引入消息队列。

## 11.4 片段元素

可以新增：

```http
GET /api/template-segments/{segmentId}/elements
```

或者扩展现有模板版本详情接口支持：

```text
?segmentId=...
```

选择与当前 API 风格最一致的方式。

## 11.5 错误码

至少增加：

```text
template_segment_not_found
segment_anchor_not_found
segment_anchor_duplicated
segment_range_invalid
segment_preview_failed
segment_fingerprint_changed
```

---

# 十二、前端实现

先理解当前 `/workspace` 页面结构，不要重写整个工作区。

新增或拆分组件：

```text
TemplateSegmentTree.vue
TemplateSegmentWorkspace.vue
TemplateSegmentStatusBadge.vue
TemplateSegmentNavigator.vue
```

## 12.1 页面布局

目标：

```text
左侧：
- Segment 树
- 每个 Segment 的绑定进度
- 当前选中状态

中间：
- 当前 Segment 的 docx-preview
- 当前 Segment 中的图表预览
- 当前 Segment 的绑定目标

右侧：
- 数据字段树
- 当前绑定属性
- 校验结果
```

## 12.2 懒加载

点击 Segment 后才执行：

```text
1. 加载片段详情
2. 加载片段预览 DOCX
3. 调用 docx-preview
4. 加载片段模板元素
5. 加载对应绑定项
6. 装饰文本和图表绑定目标
```

切换片段时必须：

```text
- 销毁旧 ECharts 实例
- 释放旧 Blob URL
- 清除旧 DOM
- 取消或忽略旧异步请求
- 防止旧请求覆盖新片段状态
```

建议使用请求序号或 `AbortController`。

## 12.3 路由

建议支持：

```text
/workspace?projectId=...&chapterId=...&segmentId=...
```

或：

```text
/workspace/:projectId/:chapterId/:segmentId
```

应当兼容现有工作区入口。

## 12.4 旧模板兼容

没有 Segment 的模板应显示：

```text
完整模板
```

用户体验不能比当前版本退化。

---

# 十三、绑定逻辑调整

本次不要为每个 Segment 创建一套完全独立且重复的数据模型。

可以继续使用现有：

```text
rp_binding_set
rp_binding_item
```

绑定目标仍然是：

```text
template_element_id
```

Segment 只是：

```text
模板元素的归属范围
工作区过滤范围
进度统计范围
后续锁定范围
```

这样现有生成报告流程可以保持兼容。

需要增加的查询能力：

```text
按 segment_id 查询模板元素
按 segment_id 统计绑定数量
按 segment_id 校验必填绑定
```

不应把同一个模板元素复制成多条记录。

---

# 十四、最终报告生成兼容

当前 `BindingSetDocumentService` 和 `WordReportRenderer` 已经基于完整模板版本生成报告。

必须保持：

```text
完整原始 DOCX
→ 创建副本
→ 执行所有文本和图表绑定
→ 返回完整 DOCX
```

Segment 功能完成后，报告生成不能改成：

```text
片段预览分别生成
→ 把多个片段预览简单拼接
```

本阶段建议仅增加：

```text
- 生成前验证所有启用 Segment 的必填绑定
- 按 Segment 输出更明确的校验错误
- 保持最终 Binding 列表仍使用完整模板 Locator
```

如果当前项目尚未实现 ChapterRevision 和项目级 RenderPlan，不要在本次强行重构全部生成系统。

可以保留当前 BindingSet 生成入口，只确保 Segment 不破坏完整模板生成。

---

# 十五、性能与文件处理

当前代码可能存在：

```text
File.ReadAllBytesAsync
MemoryStream
ToArray()
```

本次不要无边界重构整个文件系统，但片段预览必须尽量使用：

```text
TemporaryFileLease
FileStream
数据库文件服务
```

要求：

```text
- 不把 DOCX 转 Base64 存入 JSON
- 不新增本地永久文件目录
- 临时文件使用 finally/await using 清理
- 预览文件进入 rp_file_object/rp_file_chunk
- 日志不能输出完整模板正文或文件内容
```

---

# 十六、测试要求

所有实现必须配套测试。

## 16.1 后端单元测试

至少增加：

```text
SegmentTagParserTests
TemplateSegmentScannerTests
SegmentElementAssignerTests
SegmentCoverageValidatorTests
SegmentFingerprintTests
SegmentPreviewRendererTests
```

覆盖：

```text
1. 单个 Segment
2. 多个同级 Segment
3. 嵌套 Segment
4. 重复 segment-key
5. 非法 Tag
6. 缺少 Alias
7. Segment 中包含段落
8. Segment 中包含表格
9. Segment 中包含图片
10. Segment 中包含 Word 原生图表
11. Segment 中包含分页符
12. Segment 中包含分节符
13. 页眉页脚全局元素
14. 无 Segment 的旧模板
15. 模板重扫后指纹变化
16. 片段预览可以重新用 OpenXML SDK 打开
```

## 16.2 Repository 测试

如果当前测试主要使用 InMemory Repository：

* 为接口增加对应 InMemory 实现；
* 保持现有自动化测试不连接真实 MySQL；
* SQL 迁移和 MySQL Repository 仍要实现；
* 能合理增加 MySQL 集成测试时再增加；
* 不要让普通 `dotnet test` 依赖远程数据库。

## 16.3 前端测试

Vitest 至少覆盖：

```text
- 片段树加载
- 选择片段
- 切换片段时重新加载预览
- 旧请求不能覆盖新片段
- 无 Segment 时显示完整模板
- 绑定进度显示
- 预览失败提示
- Blob URL 和图表实例被清理
```

## 16.4 必须执行

修改完成后执行：

```powershell
dotnet restore
dotnet build
dotnet test
```

前端：

```powershell
cd frontend
npm ci
npm run typecheck
npm test
npm run build
```

不能只报告“理论上应该通过”。

如果测试失败：

1. 分析根因；
2. 修复本次改动导致的问题；
3. 重新运行；
4. 不能通过删除测试、跳过测试或降低断言来掩盖问题。

---

# 十七、向后兼容要求

不得破坏：

```text
- 现有模板上传
- 模板版本管理
- 模板重扫
- 黄色文字识别
- 显式占位符识别
- Word 原生图表扫描
- 图表绑定
- 文本绑定
- 生成测试报告
- 导出复用模板
- MySQL 文件分片存储
- 项目和章节管理
- 无 Segment 的普通模板
```

旧模板默认作为一个 `full-document` Segment 使用。

现有 API 能保持不变的尽量保持不变。

---

# 十八、代码质量要求

1. 禁止写一个数千行的万能类；
2. Segment 扫描、定位、归属、预览和持久化必须分离；
3. Core 不依赖 OpenXML；
4. Infrastructure 不处理前端 DTO；
5. API Endpoint 不直接写复杂业务事务；
6. 使用现有异常和 ProblemDetails 风格；
7. 使用现有数据库 ID 字符串映射风格；
8. 所有公共异步方法接收 `CancellationToken`；
9. 不吞掉异常；
10. 不使用空 `catch`；
11. 不静默回退到错误结果；
12. 不在日志输出数据库密码、DOCX 正文和完整绑定数据；
13. 不增加无必要的第三方依赖；
14. 不引入 Redis、RabbitMQ、Hangfire、Aspose 或 Spire；
15. 注释解释设计原因，不要重复代码字面意思。

---

# 十九、建议的实施顺序

严格按照以下顺序推进，避免一次性同时修改所有模块。

## 步骤 1：数据库和领域模型

```text
- migration
- TemplateSegmentRecord
- Repository 接口
- MySQL Repository
- InMemory Repository
```

## 步骤 2：Segment 扫描

```text
- SegmentTagParser
- OpenXmlTemplateSegmentScanner
- 嵌套关系
- 诊断
- 指纹
```

## 步骤 3：模板元素归属

```text
- 统一文档顺序
- element.segment_id
- segment_local_order
- 全局元素
```

## 步骤 4：API

```text
- Segment 列表
- Segment 详情
- Segment 元素
```

## 步骤 5：片段预览

```text
- 临时复制
- 保留目标范围
- 数据库文件缓存
- 下载 API
```

## 步骤 6：前端

```text
- Segment 树
- 当前片段工作区
- 懒加载
- 清理旧资源
- 进度显示
```

## 步骤 7：生成兼容和校验

```text
- 完整模板生成不变
- 按 Segment 展示校验信息
- 回归测试
```

---

# 二十、最终验收标准

实现完成后必须满足以下验收条件：

```text
1. 上传带多个 wtb:segment:* 内容控件的 DOCX 后，后端能正确识别片段。
2. Segment 支持父子嵌套。
3. 同一模板版本中的 segment-key 唯一。
4. 模板元素能稳定归属到最内层 Segment。
5. 页眉、页脚等全局元素不会被错误归入正文 Segment。
6. 前端可以显示 Segment 树。
7. 点击不同 Segment 时只加载对应片段预览。
8. 当前工作区只展示对应片段的绑定元素。
9. Segment 预览包含原有文字、表格、图片和 Word 原生图表。
10. Segment 预览不会成为正式模板。
11. 完整原始模板字节保持不可变。
12. 最终报告仍从完整原始模板生成。
13. 无 Segment 的旧模板仍可正常绑定和生成。
14. 模板重扫后可检测片段指纹变化。
15. 所有新增 API 使用统一 ProblemDetails。
16. 所有数据库 ID 对外仍使用字符串。
17. 后端测试通过。
18. 前端类型检查、测试和构建通过。
19. 原有文本、图表和复用模板功能没有回归。
20. 最终代码中没有为了通过测试而保留明显临时代码。
```

---

# 二十一、最终输出要求

完成代码修改后，给出完整实施报告，必须包括：

## 1. 实际修改文件

按模块列出：

```text
Core
Infrastructure
API
Frontend
SQL
Tests
Docs
```

## 2. 数据库变更

说明：

```text
- 新增了哪些表
- 修改了哪些字段
- 应按什么顺序执行迁移
- 是否兼容已有数据
```

## 3. 关键实现说明

重点说明：

```text
- Segment 如何识别
- Segment 如何嵌套
- 模板元素如何归属
- 片段预览如何生成
- 预览缓存如何失效
- 前端如何懒加载
- 最终报告为什么仍不会破坏原模板
```

## 4. API 清单

列出新增和修改的 API。

## 5. 测试结果

必须给出真实命令和结果摘要：

```text
dotnet build
dotnet test
npm run typecheck
npm test
npm run build
```

## 6. 已知限制

如实说明仍未实现的内容，例如：

```text
- 书签范围兼容
- 自动按标题拆分
- 多人锁
- 独立章节物理合并
- Repeat Block
```

## 7. 手工验收步骤

提供一套从：

```text
上传测试 DOCX
→ 查看 Segment
→ 切换预览
→ 完成绑定
→ 生成完整报告
```

的可执行验收步骤。

---

现在开始：

1. 阅读 `01-template-binding-scope-splitting.md`；
2. 调查当前仓库相关实现；
3. 输出简短实施计划；
4. 直接修改代码；
5. 执行数据库、后端、前端和测试闭环；
6. 不要只输出设计方案，必须实际完成代码实现。
