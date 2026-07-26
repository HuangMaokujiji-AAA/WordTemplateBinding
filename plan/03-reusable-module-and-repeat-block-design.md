# 小模板组件复用、重复块与报告蓝图设计

> 适用仓库：`HuangMaokujiji-AAA/WordTemplateBinding`  
> 适用技术栈：Open XML SDK 3.0.2、ASP.NET Core、MySQL 8.0、Vue 3、docx-preview、JSZip、ECharts  
> 文档目标：实现大量小模板组件的版本化复用，根据学校、专业等集合数据自动生成对应数量的表格行、文字区块和 Word 原生图表，并通过可视化蓝图构建新报告。

---

## 1. 当前能力与缺口

当前仓库已经具备：

- 逻辑模板与不可变版本；
- 模板元素扫描；
- 文本绑定；
- Word 原生图表识别和数据写入；
- 复用模板导出；
- `template_type` 可表示主模板、章节模板和组件；
- 前端图表预览；
- 图表分类数量和公式范围更新。

但当前渲染器主要执行：

```text
替换已有文本
修改已有图表
```

尚不支持：

- 根据集合克隆表格行；
- 根据专业数量克隆整段分析区；
- 在重复块中克隆图表及嵌入工作簿；
- 条件块；
- 组件插槽；
- 报告蓝图；
- 组件依赖和版本固定；
- 一张图动态增加/删除系列。

本设计把问题分为三层：

```text
组件 Component
    ↓
重复/条件规则 Repeat / Condition
    ↓
报告蓝图 Blueprint
```

---

## 2. 核心概念

## 2.1 Component

Component 是可独立维护、版本化的小模板，例如：

```text
标准封面
学校总体概览
专业概览表
单专业详细分析
黄牌预警分析
优势特色专业分析
就业率图表卡片
雷达图卡片
改进建议
附录说明
```

继续复用现有：

```text
rp_template.template_type = 'COMPONENT'
```

每个 Component 仍通过 `rp_template_version` 保存不可变 DOCX。

## 2.2 Repeat Block

Repeat Block 是模板中的一个可复制原型。

示例：

```text
majors 数组有 12 条
→ 专业分析原型复制 12 次
→ 每次使用当前 major 数据上下文
```

类型：

```text
REPEAT_ROW      复制表格行
REPEAT_BLOCK    复制段落/表格/图表块
REPEAT_COMPONENT 在蓝图中重复整个组件
```

## 2.3 Conditional Block

根据数据决定是否保留：

```text
warningEnabled = true
→ 保留黄牌预警部分

advantageEnabled = false
→ 删除优势特色部分
```

## 2.4 Blueprint

Blueprint 是报告组件清单和执行规则，不直接保存 Word 内容。

```text
学校专业监测报告 Blueprint
├─ 标准封面组件 v2
├─ 总体概览组件 v4
├─ 专业概览表组件 v3
├─ Repeat majors
│   └─ 单专业分析组件 v7
├─ 改进建议组件 v3
└─ 附录组件 v1
```

---

## 3. 使用的方法和库

## 3.1 Open XML SDK 3.0.2

核心类型：

| 类型 | 用途 |
|---|---|
| `SdtBlock` | 标记组件插槽、重复块、条件块 |
| `SdtRow` | 标记重复表格行 |
| `SdtCell` | 必要时标记单元格组件 |
| `Tag` | 保存稳定 Block Key |
| `OpenXmlElement.CloneNode(true)` | 克隆段落、表格和内容控件 |
| `MainDocumentPart.AddPart` | 复制部件并生成新关系 |
| `ChartPart` | 克隆 Word 原生图表 |
| `EmbeddedPackagePart` | 克隆图表嵌入 Excel 工作簿 |
| `ImagePart` | 克隆图片 |
| `NumberingDefinitionsPart` | 处理编号 |
| `StyleDefinitionsPart` | 导入组件样式 |
| `HeaderPart/FooterPart` | 主模板页眉页脚策略 |
| `CustomXmlPart` | 保存组件和绑定 Manifest |

不引入 Aspose、Spire 或商业 Word SDK。

## 3.2 System.Text.Json

用于：

- Repeat 配置；
- Blueprint 节点；
- 数据作用域；
- 条件规则；
- 组件输入输出契约；
- RenderPlan；
- 组件依赖清单。

条件和转换必须使用受控 JSON DSL，不允许任意 C#、JavaScript 或 SQL。

## 3.3 MySqlConnector

用于保存：

- 组件元数据；
- Blueprint 版本；
- Blueprint 节点；
- 组件依赖；
- 生成任务固定版本；
- 运行时实例清单。

## 3.4 前端库

### Vue 3

组件编排器、组件库和节点属性面板。

### docx-preview

预览单个组件或展开后的报告预览。

### JSZip

读取 DOCX 包中的图表和媒体关系，继续复用现有前端识别流程。

### ECharts

显示图表预览。最终 DOCX 仍写入 Word 原生 ChartPart，ECharts 不参与正式文件生成。

---

## 4. 模板标记协议

## 4.1 不在 Tag 中保存复杂 JSON

不要这样：

```text
wtb:repeat:major|source=majors|key=majorId|condition=...
```

长期会遇到：

- Tag 长度和转义问题；
- Word/WPS 修改；
- 配置难以升级；
- 无法引用复杂映射。

推荐 Tag 只保存稳定键：

```text
wtb:repeat:major-analysis
wtb:condition:warning-section
wtb:slot:report-body
wtb:component-root:major-analysis
```

完整配置保存于数据库或 CustomXmlPart。

## 4.2 Repeat Block 标记

### 表格行

选中模板中的一行，使用内容控件：

```text
Tag: wtb:repeat:major-summary-row
```

配置：

```json
{
  "blockKey": "major-summary-row",
  "blockType": "REPEAT_ROW",
  "sourcePath": "majors",
  "itemAlias": "major",
  "itemKeyPath": "majorId",
  "emptyBehavior": "REMOVE_PROTOTYPE"
}
```

行内绑定：

```text
{{major.majorName}}
{{major.warningLevel}}
{{major.employmentRate}}
```

### 整块分析

```text
Tag: wtb:repeat:major-analysis
```

配置：

```json
{
  "blockKey": "major-analysis",
  "blockType": "REPEAT_BLOCK",
  "sourcePath": "majors",
  "itemAlias": "major",
  "itemKeyPath": "majorId",
  "pageBreak": "BEFORE_EACH_EXCEPT_FIRST",
  "emptyBehavior": "REMOVE_PROTOTYPE"
}
```

## 4.3 条件块

```text
Tag: wtb:condition:warning-section
```

配置：

```json
{
  "blockKey": "warning-section",
  "expression": {
    "operator": "EQ",
    "left": {
      "path": "major.sections.warningEnabled"
    },
    "right": {
      "constant": true
    }
  },
  "falseBehavior": "REMOVE"
}
```

支持的受控运算符：

```text
EQ
NE
GT
GTE
LT
LTE
IS_NULL
IS_NOT_NULL
IS_EMPTY
IS_NOT_EMPTY
AND
OR
NOT
```

第一阶段不支持任意脚本表达式。

## 4.4 组件插槽

主模板中：

```text
Tag: wtb:slot:report-body
```

Blueprint 把组件插入该槽位。

---

## 5. 数据结构示例

```json
{
  "school": {
    "schoolId": "1001",
    "schoolName": "示例大学",
    "year": 2026
  },
  "majors": [
    {
      "majorId": "080901",
      "majorName": "计算机科学与技术",
      "warningLevel": "正常",
      "employmentRate": 95.2,
      "sections": {
        "warningEnabled": false,
        "advantageEnabled": true
      },
      "charts": {
        "employment": {
          "categories": ["2022", "2023", "2024", "2025"],
          "series": [
            {
              "seriesKey": "employment-rate",
              "name": "就业率",
              "values": [92.1, 93.5, 94.0, 95.2]
            }
          ]
        },
        "radar": {
          "categories": ["师资", "课程", "就业", "科研", "实践"],
          "series": [
            {
              "seriesKey": "current-major",
              "name": "本专业",
              "values": [86, 91, 89, 82, 90]
            }
          ]
        }
      }
    }
  ]
}
```

稳定实例键：

```text
majorId = 080901
```

运行时路径：

```text
major-analysis/080901
major-analysis/080901/chart/employment
major-analysis/080901/chart/radar
```

不要使用数组下标作为实例身份，因为排序变化会导致缓存和日志失效。

---

## 6. 数据库设计

## 6.1 组件元数据

现有 `rp_template` 可继续使用。建议增加组件契约表：

```sql
CREATE TABLE rp_component_contract (
    template_version_id BIGINT UNSIGNED NOT NULL,

    component_key VARCHAR(128) NOT NULL,
    contract_version INT UNSIGNED NOT NULL DEFAULT 1,

    input_schema_json JSON NOT NULL,
    output_manifest_json JSON NULL,
    slot_schema_json JSON NULL,
    repeat_schema_json JSON NULL,
    condition_schema_json JSON NULL,

    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),

    PRIMARY KEY (template_version_id),

    CONSTRAINT fk_rp_component_contract_version
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE CASCADE
);
```

## 6.2 Blueprint

```sql
CREATE TABLE rp_report_blueprint (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    blueprint_code VARCHAR(64) NOT NULL,
    blueprint_name VARCHAR(255) NOT NULL,
    description TEXT NULL,
    blueprint_status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    current_version_no INT UNSIGNED NOT NULL DEFAULT 0,
    row_version INT UNSIGNED NOT NULL DEFAULT 0,
    created_by BIGINT UNSIGNED NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_by BIGINT UNSIGNED NULL,
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
        ON UPDATE CURRENT_TIMESTAMP(3),

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_blueprint_code (blueprint_code)
);
```

```sql
CREATE TABLE rp_report_blueprint_version (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    blueprint_id BIGINT UNSIGNED NOT NULL,
    version_no INT UNSIGNED NOT NULL,
    version_status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',

    master_template_version_id BIGINT UNSIGNED NOT NULL,
    config_json JSON NULL,
    dependency_hash CHAR(64) NULL,

    created_by BIGINT UNSIGNED NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    published_by BIGINT UNSIGNED NULL,
    published_at DATETIME(3) NULL,

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_blueprint_version (
        blueprint_id,
        version_no
    ),

    CONSTRAINT fk_rp_blueprint_version_blueprint
        FOREIGN KEY (blueprint_id)
        REFERENCES rp_report_blueprint(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_rp_blueprint_master_template
        FOREIGN KEY (master_template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE RESTRICT
);
```

```sql
CREATE TABLE rp_report_blueprint_node (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    blueprint_version_id BIGINT UNSIGNED NOT NULL,
    parent_node_id BIGINT UNSIGNED NULL,

    node_key VARCHAR(128) NOT NULL,
    node_name VARCHAR(255) NOT NULL,
    node_type VARCHAR(32) NOT NULL,

    template_version_id BIGINT UNSIGNED NULL,
    target_slot_key VARCHAR(128) NULL,

    data_scope_path VARCHAR(1024) NULL,
    item_alias VARCHAR(64) NULL,
    item_key_path VARCHAR(1024) NULL,

    condition_config_json JSON NULL,
    assembly_config_json JSON NULL,

    sort_key DECIMAL(20,10) NOT NULL DEFAULT 1000.0000000000,
    is_enabled TINYINT(1) NOT NULL DEFAULT 1,

    PRIMARY KEY (id),
    UNIQUE KEY uk_rp_blueprint_node (
        blueprint_version_id,
        node_key
    ),
    KEY idx_rp_blueprint_node_tree (
        blueprint_version_id,
        parent_node_id,
        sort_key
    ),

    CONSTRAINT fk_rp_blueprint_node_version
        FOREIGN KEY (blueprint_version_id)
        REFERENCES rp_report_blueprint_version(id)
        ON DELETE CASCADE,

    CONSTRAINT fk_rp_blueprint_node_parent
        FOREIGN KEY (parent_node_id)
        REFERENCES rp_report_blueprint_node(id)
        ON DELETE RESTRICT,

    CONSTRAINT fk_rp_blueprint_node_template
        FOREIGN KEY (template_version_id)
        REFERENCES rp_template_version(id)
        ON DELETE RESTRICT
);
```

节点类型：

```text
STATIC_COMPONENT
REPEAT_COMPONENT
CONDITIONAL_COMPONENT
GROUP
SLOT_REFERENCE
```

---

## 7. 组件 DOCX 规范

每个组件 DOCX 建议：

1. 只有一个组件根内容控件；
2. 不自己定义最终页眉页脚；
3. 不包含主文档目录；
4. 页面方向通过组件配置声明；
5. 样式名称增加组件命名空间；
6. 图片、图表必须位于根内容控件内；
7. 绑定元素使用稳定 Tag；
8. 必须有组件契约；
9. 不直接引用外部本地文件；
10. 不包含宏。

组件根：

```text
Tag: wtb:component-root:major-analysis
```

---

## 8. 核心渲染顺序

必须严格按以下顺序：

```text
1. 读取 BlueprintVersion
2. 固定所有 Component TemplateVersion
3. 构建 RenderPlan
4. 把静态组件插入主模板 Slot
5. 展开 REPEAT_COMPONENT
6. 展开组件内部 REPEAT_BLOCK / REPEAT_ROW
7. 处理 CONDITIONAL_BLOCK
8. 生成 Runtime Locator Map
9. 替换文字
10. 写入图表数据
11. 处理分页、分节、编号和样式
12. 执行完整性校验
13. 保存 Artifact
```

不能先替换文本再克隆，因为克隆后的实例会携带已替换值，难以定位和区分。

---

## 9. RenderPlan 设计

```csharp
public sealed record BlueprintRenderPlan
{
    public ulong BlueprintVersionId { get; init; }
    public ulong MasterTemplateVersionId { get; init; }

    public IReadOnlyList<ComponentRenderNode> Nodes { get; init; }
        = Array.Empty<ComponentRenderNode>();

    public IReadOnlyDictionary<ulong, ulong> FixedTemplateVersions { get; init; }
        = new Dictionary<ulong, ulong>();

    public IReadOnlyDictionary<ulong, ulong> FixedDataSnapshots { get; init; }
        = new Dictionary<ulong, ulong>();
}
```

```csharp
public sealed record ComponentRenderNode
{
    public string NodeKey { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;

    public ulong TemplateVersionId { get; init; }
    public string? TargetSlotKey { get; init; }

    public string? DataScopePath { get; init; }
    public string? ItemAlias { get; init; }
    public string? ItemKeyPath { get; init; }

    public JsonElement? Condition { get; init; }
    public JsonElement? AssemblyConfig { get; init; }
}
```

发布 Blueprint 时解析并保存依赖哈希：

```text
SHA256(
  masterTemplateVersionId
  + nodeKey/templateVersionId/config
  + componentContractVersion
)
```

---

## 10. 数据上下文解析

当前简单字段解析通常取样例首行，不足以支持循环。

新增：

```text
IDataContextResolver
JsonDataContextResolver
RenderScope
```

```csharp
public sealed record RenderScope
{
    public RenderScope? Parent { get; init; }
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
        = new Dictionary<string, object?>();

    public string InstanceKey { get; init; } = string.Empty;
}
```

根作用域：

```text
school
majors
year
```

循环作用域：

```text
major = majors[i]
$index = i
$key = major.majorId
$parent = 根作用域
```

路径解析：

```text
major.majorName
major.charts.employment
$parent.school.schoolName
```

不支持反射执行任意方法。

---

## 11. Repeat Row 实现

## 11.1 OpenXML 结构

重复行应标记为 `SdtRow`，内部包含一个 `TableRow`。

处理：

```text
找到 SdtRow
→ 读取数据集合
→ 对每个 item 克隆原型行
→ 重映射行内 ID 和关系
→ 插入到原型前
→ 删除原型内容控件
```

示意：

```csharp
SdtRow repeatRow = ResolveRepeatRow(blockKey);
TableRow prototype = repeatRow
    .Descendants<TableRow>()
    .First();

foreach (RenderScope itemScope in itemScopes)
{
    TableRow clone = (TableRow)prototype.CloneNode(true);

    RuntimeCloneResult remapped =
        await _cloneRemapper.RemapAsync(
            document,
            clone,
            itemScope.InstanceKey,
            cancellationToken);

    repeatRow.InsertBeforeSelf(remapped.Root);
}

repeatRow.Remove();
```

---

## 12. Repeat Block 实现

块级内容控件可包含：

- 段落；
- 表格；
- 图片；
- 图表；
- 分页符。

处理：

```text
找到 SdtBlock
→ 取得 SdtContentBlock 子元素
→ 每个 item 深克隆全部子元素
→ 克隆依赖部件
→ 生成实例 Locator
→ 插入到原内容控件前
→ 删除原型
```

如果设置：

```text
pageBreak = BEFORE_EACH_EXCEPT_FIRST
```

在除首项外的每个实例前插入：

```xml
<w:p>
  <w:r>
    <w:br w:type="page"/>
  </w:r>
</w:p>
```

如果需要不同页面方向，应使用分节符，不使用普通分页符。

---

## 13. 图表克隆

## 13.1 为什么必须克隆 ChartPart

如果把同一个图表 XML 节点复制 12 次，但仍引用同一个 `ChartPart`：

- 12 张图实际共享一个数据源；
- 写入第二个专业时会覆盖第一个；
- 最终所有图可能显示最后一个专业的数据。

因此每个重复实例必须拥有独立：

```text
ChartPart
EmbeddedPackagePart
RelationshipId
必要的 ImagePart
```

## 13.2 克隆流程

```text
找到 Drawing 中 ChartReference r:id
→ 根据旧 r:id 取得源 ChartPart
→ 在 MainDocumentPart 创建新的 ChartPart
→ 复制 ChartSpace XML
→ 复制 ChartPart 下的 EmbeddedPackagePart
→ 复制 ChartPart 下的图片和关系
→ 获取新关系 ID
→ 修改克隆 Drawing 的 ChartReference.Id
→ 重映射 docPr.Id
```

建议类：

```text
OpenXmlChartPartCloner
OpenXmlEmbeddedWorkbookCloner
OpenXmlDrawingIdAllocator
OpenXmlRelationshipCloner
```

示意接口：

```csharp
public interface IOpenXmlChartPartCloner
{
    Task<ChartCloneResult> CloneForElementAsync(
        MainDocumentPart mainPart,
        OpenXmlElement clonedRoot,
        string instanceKey,
        CancellationToken cancellationToken);
}
```

## 13.3 嵌入工作簿

源 `ChartPart` 可能包含：

```text
EmbeddedPackagePart
application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
```

克隆时：

1. 新建 EmbeddedPackagePart；
2. 复制原始二进制；
3. 复制 ChartPart 到工作簿的关系；
4. 后续由现有 `EmbeddedChartWorkbookWriter` 写入当前专业数据。

## 13.4 图表分类和系列

当前已有能力适合：

- 分类数量动态变化；
- 现有系列值动态变化；
- 公式范围更新。

当前不应假设支持：

- 新增系列；
- 删除系列；
- 动态系列样式。

第一阶段规则：

> 每个图表组件在 Word 中预先定义固定系列结构；专业数量通过克隆整张图表解决。

后续增加：

```text
OpenXmlChartSeriesCloner
```

处理：

- `c:ser`；
- `c:idx`；
- `c:order`；
- 系列样式；
- 公式列；
- 工作簿数据列；
- 图例。

---

## 14. Runtime Locator

原模板 Locator 不能直接用于多个重复实例，因为同一原型会被克隆多次。

运行时 Locator 采用：

```text
{componentNodeKey}/{instanceKey}/{elementKey}
```

示例：

```text
major-loop/080901/major-title
major-loop/080901/employment-chart
major-loop/080901/radar-chart
major-loop/080902/major-title
```

模型：

```csharp
public sealed record RuntimeTemplateElement
{
    public string RuntimeLocatorId { get; init; } = string.Empty;
    public string SourceElementKey { get; init; } = string.Empty;
    public string InstanceKey { get; init; } = string.Empty;
    public string DataScopePath { get; init; } = string.Empty;
    public OpenXmlRuntimeLocator Locator { get; init; } = default!;
}
```

Runtime Locator 只存在于生成任务 Manifest 或临时内存中，不写回原始模板元素表。

---

## 15. 组件插入与依赖复制

## 15.1 推荐主模板策略

主模板负责：

- 默认主题；
- Normal 样式；
- 标题样式；
- 页眉页脚；
- 页码；
- 目录；
- 全局页面设置。

组件负责正文内容。

## 15.2 插入流程

```text
打开主模板副本
→ 找到 wtb:slot:{slotKey}
→ 打开组件模板
→ 克隆组件根内容
→ 导入组件样式
→ 导入编号
→ 克隆图片和图表关系
→ 插入 Slot
→ 删除 Slot 原型
```

## 15.3 样式冲突

新增：

```text
OpenXmlStyleImportService
```

策略：

1. 样式 ID 不存在：直接导入；
2. 样式 ID 存在且 XML 指纹一致：复用；
3. 样式 ID 存在但内容不同：重命名；
4. 更新组件内容中的 `ParagraphStyleId`、`RunStyle`、`TableStyle`。

重命名示例：

```text
Heading2
→ cmp_major_analysis_Heading2
```

组件开发规范建议主动使用命名空间样式，降低冲突。

## 15.4 编号冲突

新增：

```text
OpenXmlNumberingRemapper
```

处理：

- `AbstractNumId`；
- `NumberingInstance`；
- 段落 `NumberingId`；
- 多级列表。

不能直接复制组件的 `numId`，因为主模板可能已占用。

## 15.5 其他 ID

必须重映射：

- Bookmark ID；
- Bookmark Name；
- Drawing `docPr.Id`；
- Shape ID；
- Footnote ID；
- Endnote ID；
- Comment ID；
- relationship ID；
- ChartPart URI；
- 图片关系。

第一阶段可明确限制组件不包含脚注、尾注和批注，降低实现范围。

---

## 16. 条件块处理

处理顺序在 Repeat 之后。

原因：

```text
每个专业的 warningEnabled 不同
```

必须先创建各专业实例，再在各实例作用域中判断。

流程：

```text
遍历运行时条件块
→ 用当前 RenderScope 解析表达式
→ true：移除外层 Sdt，保留内容
→ false：删除整个 Sdt
```

条件失败策略：

```text
STRICT：报错并终止生成
FALSE_ON_ERROR：记录警告并按 false
KEEP_TEMPLATE：记录警告并保留原型
```

正式发布建议使用 `STRICT`。

---

## 17. Blueprint 管理

## 17.1 创建新报告

用户操作：

```text
选择“学校专业监测报告”蓝图
→ 复制为新蓝图草稿
→ 替换封面组件
→ 修改组件顺序
→ 删除不需要的条件组件
→ 增加新组件
→ 校验
→ 发布
```

复制的是节点配置，不复制所有组件 DOCX。

## 17.2 组件升级

已有 Blueprint 固定：

```text
major-analysis component v7
```

组件发布 v8 后：

- 旧 Blueprint 继续使用 v7；
- UI 显示“可升级”；
- 用户查看兼容性；
- 主动升级节点到 v8；
- 生成新的 BlueprintVersion。

不能自动追踪“最新组件”，否则历史报告不可复现。

## 17.3 依赖检查

升级前比较：

```text
旧 input schema
新 input schema
旧 element keys
新 element keys
旧 slots
新 slots
```

结果：

```text
COMPATIBLE
COMPATIBLE_WITH_WARNINGS
BREAKING
```

---

## 18. API 设计

## 18.1 组件库

```http
GET /api/components
GET /api/components/{templateId}
GET /api/components/{templateId}/versions
POST /api/components
POST /api/components/{templateId}/versions
```

## 18.2 组件契约

```http
GET /api/component-versions/{versionId}/contract
PUT /api/component-versions/{versionId}/contract
POST /api/component-versions/{versionId}/validate
```

## 18.3 Blueprint

```http
GET /api/report-blueprints
POST /api/report-blueprints
GET /api/report-blueprints/{id}/versions
POST /api/report-blueprints/{id}/versions
GET /api/report-blueprint-versions/{id}/nodes
PUT /api/report-blueprint-versions/{id}/nodes
POST /api/report-blueprint-versions/{id}/validate
POST /api/report-blueprint-versions/{id}/publish
```

## 18.4 预览

```http
POST /api/report-blueprint-versions/{id}/preview
```

请求固定一个测试数据快照，生成 Artifact，不直接修改 Blueprint。

---

## 19. 前端页面

新增：

```text
/components
/components/:id
/report-blueprints
/report-blueprints/:id
/report-blueprint-versions/:id/designer
```

Blueprint Designer：

```text
左侧：组件库
中间：节点树/画布
右侧：节点属性
底部：数据作用域和校验
```

节点属性：

- 组件版本；
- Slot；
- 数据作用域；
- Repeat Source；
- Item Alias；
- Item Key；
- 条件；
- 分页；
- 页面方向；
- 是否启用。

第一阶段采用树形编排比自由画布更稳妥。

---

## 20. 学校专业报告示例

Blueprint：

```text
root
├─ cover
│  └─ STATIC_COMPONENT / cover-standard v2
├─ overview
│  └─ STATIC_COMPONENT / school-overview v4
├─ major-table
│  └─ STATIC_COMPONENT / major-summary-table v3
├─ major-loop
│  ├─ nodeType = REPEAT_COMPONENT
│  ├─ sourcePath = majors
│  ├─ itemAlias = major
│  ├─ itemKeyPath = majorId
│  └─ templateVersion = major-analysis v7
├─ recommendations
│  └─ STATIC_COMPONENT / recommendation v3
└─ appendix
   └─ STATIC_COMPONENT / appendix v1
```

`major-analysis v7` 内部：

```text
专业名称
就业率图
雷达图
IF warningEnabled
  黄牌预警部分
IF advantageEnabled
  优势特色部分
```

数据中 18 个专业：

```text
major-analysis 组件实例化 18 次
每次克隆 2 张图
最终自动得到 36 张独立 Word 原生图表
```

---

## 21. 后端类结构

```text
src/WordTemplateBinding.Core/
├─ Models/ComponentModels.cs
├─ Models/BlueprintModels.cs
├─ Models/RepeatBlockModels.cs
├─ Models/RenderScope.cs
├─ Interfaces/IComponentContractRepository.cs
├─ Interfaces/IReportBlueprintRepository.cs
├─ Interfaces/IDataContextResolver.cs
├─ Services/BlueprintService.cs
├─ Services/BlueprintValidator.cs
├─ Services/BlueprintRenderPlanBuilder.cs
└─ Services/ComponentCompatibilityService.cs

src/WordTemplateBinding.Infrastructure/OpenXml/
├─ Components/OpenXmlComponentImporter.cs
├─ Components/OpenXmlSlotResolver.cs
├─ Repeats/OpenXmlRepeatBlockExpander.cs
├─ Repeats/OpenXmlRepeatRowExpander.cs
├─ Conditions/OpenXmlConditionalBlockProcessor.cs
├─ Cloning/OpenXmlRelationshipCloner.cs
├─ Cloning/OpenXmlChartPartCloner.cs
├─ Cloning/OpenXmlDrawingIdAllocator.cs
├─ Styles/OpenXmlStyleImportService.cs
├─ Numbering/OpenXmlNumberingRemapper.cs
└─ Runtime/OpenXmlRuntimeLocatorBuilder.cs
```

重构渲染器：

```text
WordReportRenderer
  ↓
BlueprintWordReportRenderer
  1. component importer
  2. repeat expander
  3. condition processor
  4. runtime locator builder
  5. text renderer
  6. chart renderer
```

保留当前 `WordReportRenderer` 作为单模板兼容模式。

---

## 22. 完整性校验

生成完成后执行：

```text
OpenXmlPackageValidator
```

检查：

1. 所有 `r:id` 能解析；
2. 所有 ChartReference 指向独立 ChartPart；
3. 重复实例没有共享应独立的 ChartPart；
4. Bookmark ID 唯一；
5. Drawing ID 唯一；
6. `numId` 有定义；
7. 样式引用存在；
8. 没有残留 `wtb:repeat` 原型；
9. 没有未处理条件块；
10. 没有未解析必填绑定；
11. ZIP 可重新打开；
12. OpenXML SDK 可读取并保存。

可选使用 SDK 自带验证器：

```csharp
using DocumentFormat.OpenXml.Validation;

OpenXmlValidator validator = new();
IEnumerable<ValidationErrorInfo> errors =
    validator.Validate(document);
```

注意：验证器只能检查 OpenXML 规范问题，不能替代业务校验。

---

## 23. 异常码

| 错误码 | 含义 |
|---|---|
| `component_root_not_found` | 组件没有根内容控件 |
| `component_contract_invalid` | 输入契约无效 |
| `blueprint_slot_not_found` | 主模板找不到插槽 |
| `blueprint_dependency_missing` | 组件版本不存在 |
| `repeat_source_not_array` | Repeat 数据不是数组 |
| `repeat_item_key_missing` | 缺少稳定业务键 |
| `repeat_instance_key_duplicated` | 集合业务键重复 |
| `condition_expression_invalid` | 条件规则无效 |
| `chart_clone_failed` | 图表部件克隆失败 |
| `embedded_workbook_clone_failed` | 嵌入工作簿克隆失败 |
| `style_conflict_unresolved` | 样式冲突无法处理 |
| `numbering_remap_failed` | 编号重映射失败 |
| `runtime_locator_conflict` | 运行时定位重复 |
| `blueprint_breaking_upgrade` | 组件升级不兼容 |

---

## 24. 测试设计

## 24.1 单元测试

```text
RepeatBlockTagParserTests
JsonDataContextResolverTests
ConditionEvaluatorTests
RuntimeLocatorBuilderTests
BlueprintValidatorTests
ComponentCompatibilityTests
StyleImportServiceTests
NumberingRemapperTests
```

## 24.2 OpenXML 集成测试

程序化创建 DOCX，覆盖：

1. 重复表格行 0/1/10 条；
2. 重复段落块；
3. 重复块中含图片；
4. 重复块中含柱状图；
5. 重复块中含雷达图；
6. 每个实例 ChartPart 独立；
7. 嵌入工作簿独立；
8. 条件块每个实例结果不同；
9. 样式同名同内容；
10. 样式同名不同内容；
11. 编号冲突；
12. Bookmark ID 冲突；
13. Drawing ID 冲突；
14. 页面分页；
15. 分节方向；
16. 空数组；
17. 重复业务键；
18. 大量专业生成。

## 24.3 性能测试

至少测试：

```text
100 个专业
每个专业 3 张图
总计 300 张图
```

记录：

- 生成时间；
- 峰值内存；
- 文件大小；
- ChartPart 数量；
- EmbeddedPackagePart 数量；
- 数据库 Artifact 写入时间。

当前代码把整份 DOCX 读为 `byte[]` 并 `ToArray()`，大规模组件生成后应改为临时文件流和分片 Artifact 写入。

---

## 25. 分阶段实施

### 阶段 A：重复表格行

- SdtRow 标记；
- JSON 数据作用域；
- Repeat Row；
- 文字绑定；
- 空数组处理；
- Runtime Locator。

这是最容易验证的动态结构能力。

### 阶段 B：重复块与条件块

- SdtBlock；
- 段落/表格克隆；
- 条件 DSL；
- 分页；
- 运行时作用域。

### 阶段 C：重复块中的图表

- ChartPart 克隆；
- EmbeddedPackagePart 克隆；
- Drawing ID；
- 独立图表数据写入；
- 图表完整性测试。

### 阶段 D：组件库

- COMPONENT 契约；
- 组件根；
- Slot；
- 样式导入；
- 编号重映射；
- 组件预览。

### 阶段 E：Blueprint

- Blueprint 表；
- 节点树；
- 可视化编排；
- 版本固定；
- 兼容性分析；
- 发布和正式生成。

### 阶段 F：动态系列和高级结构

- Chart Series 克隆；
- 脚注、尾注、批注；
- 跨组件引用；
- 复杂分节；
- 自动目录更新。

---

## 26. 验收标准

1. 专业数组有多少条，就生成多少个专业分析块；
2. 每个专业的图表是独立 Word ChartPart；
3. 一个专业图表写入不会覆盖其他专业；
4. 总体概览表自动生成对应数量的专业行；
5. 条件部分按每个专业数据独立显示或隐藏；
6. 组件版本固定，旧报告可复现；
7. 新报告可以通过复制 Blueprint 和替换组件构建；
8. 组件升级不会静默影响旧 Blueprint；
9. 样式、编号、图片和图表关系没有冲突；
10. 生成后 OpenXML 校验通过；
11. 原有单模板文字和图表绑定仍保持兼容；
12. 100 个专业、300 张图的压力测试可完成且内存可控。
