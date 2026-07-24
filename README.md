# WordTemplateBinding

WordTemplateBinding 是一个基于 .NET 7、ASP.NET Core Minimal API 和 Open XML SDK 的 Word 模板可视化数据绑定 MVP。

当前版本提供两条互不混淆的输出链路，以及可复用模板的完整闭环：

```text
上传 DOCX → 扫描显式占位符、黄色高亮和数字模拟值 → 结构化预览高亮
→ 拖拽数据字段 → 保存绑定
├─ A. 读取真实/演示数据 → 生成最终报告 DOCX
└─ B. 写入 {{完整数据路径}} 和图表 Manifest → 导出可复用模板 DOCX
       → 以后重新上传 → 后端自动恢复绑定 → 继续编辑或直接生成报告
```

## 环境要求

- .NET SDK 7
- 支持 ES Modules、Fetch API 和 HTML Drag and Drop API 的现代浏览器
- Microsoft Word 或 WPS Office，用于人工检查最终 DOCX

应用运行不需要 Node.js、Python、LibreOffice、数据库或外部存储。

## 项目结构

```text
WordTemplateBinding.sln
├── src/
│   ├── WordTemplateBinding.Core
│   │   ├── Enums、Models、Options、Exceptions
│   │   ├── Interfaces
│   │   └── Services
│   ├── WordTemplateBinding.Infrastructure
│   │   ├── OpenXml
│   │   ├── Stores
│   │   ├── DataSchema
│   │   └── DependencyInjection
│   └── WordTemplateBinding.Api
│       ├── Contracts、Endpoints、Middleware
│       └── wwwroot
├── tests/
│   ├── WordTemplateBinding.UnitTests
│   └── WordTemplateBinding.IntegrationTests
└── docs/
    ├── phase1-design.md
    └── api.md
```

### 项目职责

- `Core`：领域模型、存储和处理接口、业务异常以及模板/绑定/报告/复用模板业务编排。
- `Infrastructure`：OpenXML 扫描、共享文本范围替换、图表 Manifest、内存存储、演示 Schema 和数据值。
- `Api`：Minimal API、ProblemDetails、上传下载安全处理和原生 HTML/CSS/JavaScript 页面。
- `UnitTests`：程序化 DOCX 扫描、替换、格式保留、存储、Schema、绑定和值格式化测试。
- `IntegrationTests`：通过 `WebApplicationFactory` 验证 HTTP 功能闭环和生成 DOCX。

## 构建与启动

### 直接运行完整项目（推荐）

仓库已经包含构建好的 Vue 生产资源，ASP.NET Core 会直接从 `src/WordTemplateBinding.Api/wwwroot` 托管前端页面。因此，仅运行项目时不需要启动独立的 Node.js/Vite 服务。

在 PowerShell 中执行：

```powershell
cd D:\Code\WordTemplateBinding

dotnet restore
dotnet build
dotnet test
dotnet run --project .\src\WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

应用启动后访问：

```text
http://127.0.0.1:5080
```

### 前后端分开开发（Vue 热更新）

需要修改 Vue/ECharts 前端时，分别启动后端 API 和 Vite 开发服务器。

终端 1：启动 ASP.NET Core 后端：

```powershell
cd D:\Code\WordTemplateBinding
dotnet run --project .\src\WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

终端 2：安装前端依赖并启动 Vite：

```powershell
cd D:\Code\WordTemplateBinding\frontend
npm ci
npm run dev
```

然后访问 Vite 输出的地址，默认通常为：

```text
http://localhost:5173
```

Vite 会把 `/api` 请求代理到 `http://127.0.0.1:5080`。如果后端地址不同，可通过环境变量 `WTB_API_ORIGIN` 指定代理目标。

### 构建前端生产资源

修改前端后执行：

```powershell
cd D:\Code\WordTemplateBinding\frontend
npm run typecheck
npm test
npm run build
```

`npm run build` 会直接更新 `src/WordTemplateBinding.Api/wwwroot`。构建完成后返回项目根目录启动后端即可：

```powershell
cd D:\Code\WordTemplateBinding
dotnet run --project .\src\WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

## 使用流程

1. 打开浏览器访问应用首页。
2. 点击“上传 DOCX”，选择包含模拟数据的普通 `.docx` 文件。可在 Word 中用黄色文本高亮人工标记任意绑定范围；普通小数和整数仍会自动识别，文字也可继续使用 `{{示例文字}}` 或 `{{text:示例文字}}` 显式标记。
3. 在中间结构化预览中查看可绑定范围，例如黄色标记的文字、`88.5`、`1200`、`{{年度报告}}` 或 `{{text:年度报告}}`。
4. 在右侧数据源中展开字段树或搜索 `AverageScore`。
5. 将 `StudentStatistics.AverageScore` 拖到高亮的 `88.5` 上。
6. 检查高亮变为绿色、右侧已绑定列表和属性信息同步更新。
7. 选择一种输出：
   - 点击“生成报告”，下载已经写入真实/演示值的最终 `.docx`；
   - 点击“导出复用模板”，下载 `{原文件名}-template.docx`，文本绑定会写成 `{{StudentStatistics.AverageScore}}`，图表绑定会保存在 DOCX 内嵌 Manifest 中。
8. 可将复用模板转移或保存，以后重新上传；后端会按当前 Schema 自动恢复有效绑定，前端直接显示绿色状态。
9. 对自动恢复结果继续取消、修改或增加绑定，也可直接点击“生成报告”。
10. 使用 Microsoft Word 或 WPS Office 打开并检查替换值、原模板格式和可编辑图表。

如果生成请求没有显式提供字段值，系统使用内存演示值；`StudentStatistics.AverageScore` 的默认演示值为 `92.3`。

## 配置

`src/WordTemplateBinding.Api/appsettings.json`：

```json
{
  "TemplateProcessing": {
    "MaxUploadSizeMb": 20,
    "MockNumberPattern": "(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\\.[0-9]+(?![A-Za-z0-9_.,])",
    "MockIntegerPattern": "(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?![A-Za-z0-9_.,])",
    "MockTextPattern": "\\{\\{(?:text:(?<value>[^{}\\r\\n]+)|(?<value>[^{}:\\r\\n]+))\\}\\}",
    "ContextLength": 20,
    "RegexTimeoutMilliseconds": 250
  }
}
```

- `MaxUploadSizeMb`：单个模板最大上传大小。
- `MockNumberPattern`：小数识别正则，允许数值紧贴中文。
- `MockIntegerPattern`：整数识别正则，不会截取小数的一部分。
- `MockTextPattern`：显式文字标记识别正则，必须包含名为 `value` 的捕获组。
- `ContextLength`：计算定位上下文哈希时匹配值两侧的字符数。
- `RegexTimeoutMilliseconds`：单次正则匹配超时。

## 核心实现说明

### 跨 Run 扫描

扫描器不会对每个 `Text.Text` 单独运行正则。它按段落拼接全部 `Text` 节点，并为每个节点记录全局起始偏移和长度。这样即使 Word 将 `88.5`、连续黄色高亮、`{{年度报告}}` 或 `{{text:年度报告}}` 拆到多个 Run，仍可作为一个连续值识别。

候选冲突按明确优先级处理：双花括号显式标记 > Word 黄色文本高亮 > 小数/整数正则。高优先级范围确定后，任何与其相交的低优先级候选都会丢弃，最终输出再按段落偏移排序。因此黄色高亮覆盖数字的一部分或全部时，不会同时出现重叠的数字候选。黄色内容完整匹配数值正则时保留 `Decimal`/`Integer` 类型，其余内容按 `String` 处理。

### LocatorId

LocatorId 包含模板内容哈希、文档部件、段落索引、段落偏移、长度、匹配序号、原始值和上下文哈希。字段按长度前缀编码后计算 SHA-256，并输出 Base64Url。

同一文档中的多个 `88.5` 因段落或偏移不同而拥有不同 LocatorId。

Word 原生图表使用模板哈希、`/word/charts/chartN.xml` 部件 URI、关系 ID 和文档顺序生成独立 LocatorId。图表绑定到 `Array` 集合字段：每行第一列作为分类，其余数值列优先按系列名称匹配，未匹配时按列顺序写入系列。

### 局部替换

最终报告和复用模板导出都从原始模板字节创建新的内存副本，并共用同一套 `OpenXmlTextReplacementService` 局部替换策略：

- 同一段落的替换从后向前执行；
- 新值写入首个相关 Text 节点，继承首个 Run 的格式；
- 导出复用模板时，绑定范围原有的黄色文本高亮会被移除，其他 Run 格式继续保留；
- 后续节点仅删除被覆盖字符；
- 保留目标范围前后的原始文本；
- 不删除 Run，不重建 `document.xml`；
- 首尾空格需要时设置 `xml:space="preserve"`。

### 原始模板不可变

模板模型和内存存储均对原始字节执行防御性复制。每次报告生成或复用模板导出都使用新的 `MemoryStream`，连续操作不会相互影响。

### 可复用模板与自动恢复

文本绑定的标准占位符协议是 `{{完整数据路径}}`，路径必须与 `DataFieldNode.path` 使用 `StringComparison.Ordinal` 完全一致。例如 `{{StudentStatistics.AverageScore}}`。旧的 `{{text:完整数据路径}}` 在导入时兼容，但新导出统一使用无前缀形式。

上传或重新扫描完成后，后端而非 Vue 前端负责恢复绑定：

- 显式占位符精确查询当前 Schema；可绑定的非集合字段恢复为文本绑定；
- 普通 `{{年度报告}}` 若不是字段路径，仍作为未绑定模拟文字；
- 未知路径、大小写不一致和类型变化不会拒绝模板，而是进入 `importSummary`；
- 同一路径出现多次时，每个新 Locator 都建立独立绑定；
- 图表本体保持不变，绑定写入命名空间为 `urn:word-template-binding:bindings:v1`、版本为 `1` 的 `CustomXmlPart`；重新上传时按 ChartPart、关系 ID 和文档顺序分级匹配。

Manifest 不保存真实数据、模板正文、数据库连接或认证信息，也不会覆盖其他软件的 CustomXmlPart。损坏或无法匹配的 Manifest 只产生警告，文本占位符恢复仍会继续。

复用模板命名为 `{stem}-template.docx`；如果 stem 已以 `-template` 结尾（忽略大小写），不会再次追加。最终报告继续使用原有 `{stem}_generated.docx` 规则。

## API 文档

完整接口、请求、响应和错误状态码见 [docs/api.md](docs/api.md)。

## 前端图表深度解析

前端在浏览器内对每个 Word 原生图表做完整的 OOXML 结构解析（分类、系列、坐标轴、
公式来源、嵌入 Excel、组合图与次坐标轴、绑定槽位、诊断信息），产出可序列化的
`ParsedWordChart` 模型并在"图表结构"工作区页签中展示，同时驱动 ECharts 预览。
设计细节见 [docs/chart-analysis-design.md](docs/chart-analysis-design.md)。
当前正式绑定流程仍然只支持整张图表绑定一个 `Array` 集合字段，细粒度绑定槽位
已生成但尚未开放。

## 当前限制

- 自动识别十进制小数和整数；普通文字可使用 Word 黄色文本高亮、`{{...}}` 或 `{{text:...}}` 标记。
- 黄色识别仅针对标准 Word 文本高亮 `w:highlight w:val="yellow"`；其他高亮颜色、底纹以及显式 `w:highlight="none"` 不作为人工绑定标记。
- 扫描主文档正文、表格单元格和 FooterPart 页脚中的普通段落；页脚 Locator 会记录具体 `/word/footerN.xml` 部件。
- 暂不扫描页眉、脚注、尾注和文本框。
- 浏览器预览是段落级结构化预览，不是 Word 像素级预览。
- 使用内存模板和绑定存储，服务重启后数据丢失。
- 使用演示字段树和演示数据值。
- 集合字段当前仅用于整张图表的数据绑定，不用于循环表格。
- 支持更新 Word 原生图表的分类、系列名称和数值缓存；当前不增加或删除系列，也不回写图表内嵌 Excel 工作簿。
- 不支持循环表格、条件、图片替换、HTML 富文本、除 `{{...}}`/`{{text:...}}` 外的模板语法或 PDF 转换。
- 自动恢复只接受当前内存 Schema 中完全一致且类型兼容的路径；未知字段和 Schema 类型变化需要用户手动修复。
- 图表 Manifest 当前只覆盖主文档中已扫描的 Word 原生图表；不能唯一匹配时保持未绑定。
- 不包含用户登录、权限、数据库、模板版本或多人协作。

## 安全约束

- 只允许 `.docx`，不信任客户端 Content-Type。
- 使用 Open XML SDK 实际打开并校验普通 Word Document 类型。
- 拒绝空文件、损坏包、宏启用文档和超限文件。
- 服务器不使用用户文件名作为存储路径。
- 下载文件名会移除路径和非法字符。
- 前端不把模板文本写入 `innerHTML`，而是创建文本节点和元素。
- 日志不记录完整 DOCX、模板正文或数据值。
