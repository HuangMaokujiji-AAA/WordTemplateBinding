# WordTemplateBinding

WordTemplateBinding 是一个基于 .NET 7、ASP.NET Core Minimal API 和 Open XML SDK 的 Word 模板可视化数据绑定 MVP。

第一阶段提供完整闭环：

```text
上传 DOCX → 扫描小数、整数和显式文字模拟值 → 结构化预览高亮
→ 拖拽数据字段 → 保存绑定 → 生成并下载 DOCX
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

- `Core`：领域模型、存储和处理接口、业务异常以及模板/绑定/报告业务编排。
- `Infrastructure`：OpenXML 扫描与局部替换、内存存储、演示 Schema 和数据值。
- `Api`：Minimal API、ProblemDetails、上传下载安全处理和原生 HTML/CSS/JavaScript 页面。
- `UnitTests`：程序化 DOCX 扫描、替换、格式保留、存储、Schema、绑定和值格式化测试。
- `IntegrationTests`：通过 `WebApplicationFactory` 验证 HTTP 功能闭环和生成 DOCX。

## 构建与启动

在项目根目录执行：

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project .\src\WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

应用启动后，打开终端输出的本地地址。开发环境通常为：

```text
http://localhost:5080
```

如需固定地址：

```bash
dotnet run --project src/WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

## 使用流程

1. 打开浏览器访问应用首页。
2. 点击“上传 DOCX”，选择包含模拟数据的普通 `.docx` 文件。普通小数和整数可直接书写；文字使用 `{{text:示例文字}}` 显式标记。
3. 在中间结构化预览中查看黄色高亮，例如 `88.5`、`1200` 或 `{{text:年度报告}}`。
4. 在右侧数据源中展开字段树或搜索 `AverageScore`。
5. 将 `StudentStatistics.AverageScore` 拖到高亮的 `88.5` 上。
6. 检查高亮变为绿色、右侧已绑定列表和属性信息同步更新。
7. 点击“生成报告”，下载生成的 `.docx`。
8. 使用 Microsoft Word 或 WPS Office 打开并检查替换值和原模板格式。

如果生成请求没有显式提供字段值，系统使用内存演示值；`StudentStatistics.AverageScore` 的默认演示值为 `92.3`。

## 配置

`src/WordTemplateBinding.Api/appsettings.json`：

```json
{
  "TemplateProcessing": {
    "MaxUploadSizeMb": 20,
    "MockNumberPattern": "(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\\.[0-9]+(?![A-Za-z0-9_.,])",
    "MockIntegerPattern": "(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?![A-Za-z0-9_.,])",
    "MockTextPattern": "\\{\\{text:(?<value>[^{}\\r\\n]+)\\}\\}",
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

扫描器不会对每个 `Text.Text` 单独运行正则。它按段落拼接全部 `Text` 节点，并为每个节点记录全局起始偏移和长度。这样即使 Word 将 `88.5` 或 `{{text:年度报告}}` 拆到多个 Run，仍可作为一个连续值识别。多个识别器产生重叠候选时保留外层范围，因此文字标记内部的数字不会重复识别。

### LocatorId

LocatorId 包含模板内容哈希、文档部件、段落索引、段落偏移、长度、匹配序号、原始值和上下文哈希。字段按长度前缀编码后计算 SHA-256，并输出 Base64Url。

同一文档中的多个 `88.5` 因段落或偏移不同而拥有不同 LocatorId。

### 局部替换

报告生成从原始模板字节创建新的内存副本，只修改目标 `Text.Text`：

- 同一段落的替换从后向前执行；
- 新值写入首个相关 Text 节点，继承首个 Run 的格式；
- 后续节点仅删除被覆盖字符；
- 保留目标范围前后的原始文本；
- 不删除 Run，不重建 `document.xml`；
- 首尾空格需要时设置 `xml:space="preserve"`。

### 原始模板不可变

模板模型和内存存储均对原始字节执行防御性复制。每次报告生成使用新的 `MemoryStream`，连续生成不会相互影响。

## API 文档

完整接口、请求、响应和错误状态码见 [docs/api.md](docs/api.md)。

## 当前限制

- 自动识别十进制小数和整数；普通文字只有使用 `{{text:...}}` 显式标记时才会识别。
- 只扫描主文档正文和表格单元格中的普通段落。
- 不扫描页眉、页脚、脚注、尾注和文本框。
- 浏览器预览是段落级结构化预览，不是 Word 像素级预览。
- 使用内存模板和绑定存储，服务重启后数据丢失。
- 使用演示字段树和演示数据值。
- 数组节点仅用于展示后续扩展方向，当前不可绑定。
- 不支持循环表格、条件、图片、图表、HTML 富文本、除 `{{text:...}}` 外的模板语法或 PDF 转换。
- 不包含用户登录、权限、数据库、模板版本或多人协作。

## 安全约束

- 只允许 `.docx`，不信任客户端 Content-Type。
- 使用 Open XML SDK 实际打开并校验普通 Word Document 类型。
- 拒绝空文件、损坏包、宏启用文档和超限文件。
- 服务器不使用用户文件名作为存储路径。
- 下载文件名会移除路径和非法字符。
- 前端不把模板文本写入 `innerHTML`，而是创建文本节点和元素。
- 日志不记录完整 DOCX、模板正文或数据值。
