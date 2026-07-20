# WordTemplateBinding Vue 前端

此目录是 ASP.NET Core 项目的正式前端。它把原 DOCX 图表预览 demo 与现有模板扫描、数据绑定和报告生成 API 合并为同一个工作台。

## 职责边界

一份上传文件会进入两条互不混用的处理路径：

1. 浏览器使用 `docx-preview` 渲染正文、表格、图片和分页近似效果；使用 `JSZip` 读取 OOXML ChartPart，再用 ECharts 在原图表位置近似重绘。
2. ASP.NET Core 保存原始 DOCX 字节，C# 扫描器返回文本 Locator；绑定保存、重新扫描、值替换和最终 DOCX 生成全部由后端完成。

浏览器为预览而注入图表 Marker 时只修改内存中的临时 ZIP，不会把临时文档上传回服务端，也不会作为最终报告来源。

## 本地开发

先启动后端：

```powershell
dotnet run --project ..\src\WordTemplateBinding.Api --urls http://127.0.0.1:5080
```

再启动 Vite；`/api` 默认代理到上述地址：

```powershell
npm install
npm run dev
```

如后端使用其他地址，可设置 `WTB_API_ORIGIN`。

## 验证与生产构建

```powershell
npm run typecheck
npm test
npm run build
```

`npm run build` 直接输出到 `src/WordTemplateBinding.Api/wwwroot`，因此 ASP.NET Core 启动后会托管同一套 Vue 页面。生产资源也提交到仓库；当 `node_modules` 存在时，`dotnet publish` 会自动重新构建前端。CI 若单独构建前端，可传入 `-p:SkipFrontendBuild=true`。

## 绑定映射

后端响应中的 `paragraphIndex/startOffset/length` 是最终替换的唯一真相。前端在 `docx-preview` 完成渲染后，按段落文本将这些 Locator 映射到 HTML Text Node，并注入黄色/绿色绑定高亮。若浏览器排版产生无法匹配的段落，左侧模拟值导航仍可选择该 Locator，再点击右侧字段完成绑定。

## 当前图表预览范围

当前注册了柱/条形图、折线图、饼图、环形图、面积图、散点图和组合图处理器。3D 图表、雷达图、SmartArt、OLE 等未覆盖类型会在原位置显示降级提示。ECharts 结果只用于定位和近似预览，不承诺与 Microsoft Word 像素级一致。

