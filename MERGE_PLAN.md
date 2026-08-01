# WordTemplateBinding + WordTemplateWpsTemplateDriven 融合方案

## 1. 背景与目标

### 1.1 两个项目的特点

| 项目 | 优势 | 局限 |
|------|------|------|
| **WordTemplateBinding** | 企业级架构、MySQL 持久化、Vue 3 现代前端、完整项目管理 | 使用 docx-preview 网页渲染，无法呈现真实分页 |
| **WordTemplateWpsTemplateDriven** | WPS 真实 PDF 预览、书签锚点、完整模板语法 | 无数据库、无项目概念、内存存储 |

### 1.2 融合目标

以 **WordTemplateBinding** 为基础框架，集成 **WordTemplateWpsTemplateDriven** 的以下能力：

1. **WPS 真实分页预览** - 保留 Word/WPS 的真实分页、图表和排版
2. **Word 书签锚点** - 稳定的定位机制，不依赖页码
3. **增强的模板语法** - `{{source::path|default}}` 格式支持
4. **智能文档解析** - 将普通文档批量转换为模板

---

## 2. 融合架构

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        Vue 3 前端                                │
├─────────────────────────────────────────────────────────────────┤
│  WorkspaceView.vue                                              │
│  ├── [原有] DocxViewer (docx-preview 网页渲染)                  │
│  ├── [新增] WpsPdfPreview (WPS 真实 PDF 预览)                   │
│  └── 模式切换：网页预览 ↔ WPS 真实预览                           │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Minimal API                      │
├─────────────────────────────────────────────────────────────────┤
│  已有 API：                                                      │
│  ├── /api/templates/*     (模板管理)                             │
│  ├── /api/projects/*     (项目管理)                             │
│  ├── /api/bindings/*      (绑定管理)                             │
│  └── /api/datasources/*  (数据源管理)                           │
│                                                                  │
│  新增 API：                                                      │
│  ├── GET  /api/wps/status           (WPS 状态检测)             │
│  ├── POST /api/wps/pdf-preview       (WPS PDF 转换)             │
│  └── POST /api/wps/annotated-preview (带标注的预览)              │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    .NET 后端服务                                │
├─────────────────────────────────────────────────────────────────┤
│  新增服务：                                                       │
│  ├── WpsPdfConverter.cs     (WPS COM PDF 转换)                  │
│  ├── DocxAnchorService.cs   (Word 书签锚点)                      │
│  └── TemplateMigrationService.cs (智能文档解析)                  │
│                                                                  │
│  已有服务：                                                       │
│  ├── DocxTemplateEngine.cs (模板引擎 - 增强书签支持)             │
│  └── 其他领域服务                                                │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    WPS Office (COM 自动化)                       │
│                    仅 Windows 桌面环境可用                        │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 前端模块划分

```
frontend/src/
├── components/
│   ├── DocxViewer.vue          # 已有 - 网页渲染
│   ├── WpsPdfPreview.vue       # 新增 - WPS PDF 预览
│   └── PreviewModeToggle.vue   # 新增 - 预览模式切换
├── features/
│   ├── wps-preview/            # 新增 - WPS 预览功能模块
│   │   ├── pdfRenderer.ts      # PDF.js 渲染器
│   │   ├── anchorOverlay.ts    # 书签锚点叠加层
│   │   └── fieldDragHandler.ts # 字段拖拽处理
│   └── docx/
│       └── processDocx.ts      # 已有 - 保持不变
└── views/
    └── WorkspaceView.vue        # 修改 - 集成双模式预览
```

---

## 3. 详细设计

### 3.1 后端新增服务

#### 3.1.1 WpsPdfConverter.cs

**职责：** 通过 WPS COM 将 DOCX 转换为 PDF，实现真实分页预览。

**核心功能：**
- 自动检测 WPS/KWPS/WPS 三个 ProgID
- STA 线程执行 COM 操作
- 超时控制（默认 90 秒）
- 临时文件管理

**使用示例：**
```csharp
var converter = new WpsPdfConverter(configuration);
var result = await converter.ConvertDocxToPdfAsync(docxBytes, "template.docx");
Console.WriteLine($"页数: {result.PageCount}");
```

#### 3.1.2 DocxAnchorService.cs

**职责：** 在 DOCX 中插入隐藏书签，用于稳定的定位锚点。

**锚点命名规则：**
```
bm_字段路径           → bm_school_name
bm_字段路径_序号       → bm_school_name_2 (同名第二次出现)
bm_table_表格ID       → bm_table_01
bm_chart_图表ID       → bm_chart_01
```

**与原有定位的区别：**
| 方式 | 优点 | 缺点 |
|------|------|------|
| 页码 + 坐标 | 简单 | 分页变化即失效 |
| 书签锚点 | 稳定，分页变化可自适应 | 需修改 DOCX |

### 3.2 前端新增组件

#### 3.2.1 WpsPdfPreview.vue

**功能：**
- 使用 PDF.js 渲染 WPS 生成的 PDF
- 单页模式和连续滚动模式
- 放大/缩小/适合宽度
- 在 PDF 页面上叠加可点击的锚点框

**UI 设计：**
```
┌────────────────────────────────────────────────────────────┐
│ [网页预览] [WPS 真实预览]          第 1/12 页  [−][100%][+]│
├────────────────────────────────────────────────────────────┤
│                                                            │
│   ┌──────────────────────────────────────────────────┐     │
│   │                                                  │     │
│   │              PDF 页面渲染区域                      │     │
│   │    ┌─────────────────┐                           │     │
│   │    │ 锚点框 (可点击)  │ ← 高亮显示可绑定区域       │     │
│   │    └─────────────────┘                           │     │
│   │                                                  │     │
│   └──────────────────────────────────────────────────┘     │
│                                                            │
├────────────────────────────────────────────────────────────┤
│ ◀ ▶  跳转到: [____] 页    [单页] [连续]                    │
└────────────────────────────────────────────────────────────┘
```

#### 3.2.2 锚点叠加层逻辑

1. 后端返回锚点信息（位置、尺寸、绑定目标）
2. 前端根据 PDF 缩放比例计算锚点框位置
3. 鼠标悬停显示字段信息
4. 点击触发绑定操作

### 3.3 API 设计

#### 3.3.1 WPS 状态检测

```
GET /api/wps/status

Response:
{
  "isWindows": true,
  "isAvailable": true,
  "progId": "KWPS.Application",
  "message": "已检测到 WPS 自动化组件：KWPS.Application"
}
```

#### 3.3.2 生成带标注的预览 PDF

```
POST /api/wps/annotated-preview

Request:
{
  "templateId": "guid",
  "includeBindings": true
}

Response:
{
  "pdfUrl": "/api/wps/preview/{previewId}.pdf",
  "pageCount": 12,
  "anchors": [
    {
      "anchorName": "bm_school_name",
      "pageNumber": 1,
      "bounds": { "x": 100, "y": 200, "width": 150, "height": 20 },
      "targetType": "placeholder",
      "targetId": "placeholder-001",
      "boundDataPath": "school.name"
    }
  ]
}
```

---

## 4. 实施步骤

### Phase 1: 后端基础设施

1. 添加 `WpsPdfConverter.cs` 服务
2. 添加 `DocxAnchorService.cs` 服务
3. 添加 `/api/wps/*` 端点
4. 配置 WPS 相关设置（appsettings.json）

### Phase 2: 前端基础组件

1. 创建 `WpsPdfPreview.vue` 组件
2. 创建 `PreviewModeToggle.vue` 组件
3. 添加 PDF.js 依赖

### Phase 3: 集成与绑定

1. 修改 `WorkspaceView.vue` 集成双模式
2. 实现锚点叠加层
3. 实现字段拖拽到 PDF 锚点

### Phase 4: 增强功能

1. 智能文档解析功能
2. 表格/图表锚点支持
3. 优化性能和用户体验

---

## 5. 配置说明

### 5.1 appsettings.json 新增配置

```json
{
  "Wps": {
    "ProgIds": ["KWPS.Application", "wps.Application", "WPS.Application"],
    "TimeoutSeconds": 90,
    "TempRoot": "C:\\Temp\\WpsConversion"
  }
}
```

### 5.2 环境要求

- Windows 10/11
- .NET 8 SDK
- Windows 桌面版 WPS Office
- MySQL 8.0+（已有）

---

## 6. 向后兼容

### 6.1 功能开关

添加配置项控制是否启用 WPS 预览：

```json
{
  "Features": {
    "EnableWpsPreview": true
  }
}
```

当 `EnableWpsPreview: false` 或 WPS 不可用时：
- 隐藏 WPS 预览模式切换
- 仅使用原有 docx-preview 渲染

### 6.2 书签锚点

书签锚点仅在启用 WPS 预览时添加，避免对原有流程的影响。

---

## 7. 文件清单

### 新增文件

| 文件路径 | 说明 |
|----------|------|
| `src/WordTemplateBinding.Api/Services/WpsPdfConverter.cs` | WPS PDF 转换服务 |
| `src/WordTemplateBinding.Api/Services/DocxAnchorService.cs` | Word 书签服务 |
| `src/WordTemplateBinding.Api/Services/TemplateMigrationService.cs` | 文档迁移服务 |
| `frontend/src/components/WpsPdfPreview.vue` | WPS PDF 预览组件 |
| `frontend/src/components/PreviewModeToggle.vue` | 预览模式切换组件 |
| `frontend/src/features/wps-preview/pdfRenderer.ts` | PDF 渲染器 |
| `frontend/src/features/wps-preview/anchorOverlay.ts` | 锚点叠加层 |
| `frontend/src/features/wps-preview/types.ts` | 类型定义 |

### 修改文件

| 文件路径 | 修改说明 |
|----------|----------|
| `src/WordTemplateBinding.Api/Program.cs` | 注册新服务、添加 API 端点 |
| `src/WordTemplateBinding.Api/appsettings.json` | 添加 WPS 配置 |
| `frontend/src/views/WorkspaceView.vue` | 集成双模式预览 |
| `frontend/package.json` | 添加 pdfjs-dist 依赖 |
| `frontend/src/api/client.ts` | 添加 WPS API 调用 |

---

## 8. 测试计划

### 8.1 后端测试

- [ ] WPS 状态检测（已安装/未安装）
- [ ] DOCX → PDF 转换
- [ ] 书签锚点插入
- [ ] 超时处理

### 8.2 前端测试

- [ ] PDF 渲染（单页/连续）
- [ ] 缩放控制
- [ ] 锚点框点击
- [ ] 模式切换
- [ ] 字段拖拽

### 8.3 集成测试

- [ ] 完整绑定流程（上传→解析→绑定→预览→生成）
- [ ] WPS 可用/不可用降级
