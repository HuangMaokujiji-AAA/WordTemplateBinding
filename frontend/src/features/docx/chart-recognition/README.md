# Chart Recognition Module

图表识别模块，负责将统一的图表结构化分析结果（`chart-analysis`）映射到既有的
ECharts 渲染管线，并保留按图表类型分发的 Handler 结构。

深度 OOXML 解析（分类、系列、公式、坐标轴、图例、样式、嵌入工作簿等）已经迁移
到独立模块 `../chart-analysis/`，详见该模块的设计文档
[`docs/chart-analysis-design.md`](../../../../../docs/chart-analysis-design.md)。
本模块不再自行遍历 Chart XML。

## 架构

```
OOXML Chart XML
    ↓ (chart-analysis/parsers/chartXmlAnalyzer.ts，只解析一次)
ParsedWordChart（结构化模型：分类/系列/坐标轴/公式/数据表/绑定槽位/诊断）
    ↓ (chart-analysis/render/toWordChartModel.ts)
WordChartModel（本模块的渲染专用中间模型，与 ECharts 无关）
    ↓ (mappers/wordXChartToECharts.ts)
EChartsOption
    ↓ (renderers/EChartsXRenderer.ts / UnsupportedChartRenderer.ts)
DOM 渲染
```

`ChartTypeHandler.parse()`（每种图表类型一个 Handler）不再各自解析 XML，而是统一
调用 `detectors/analyzerBridge.ts#parseChartViaAnalyzer`，内部完成
"analyzeChartXml → toWordChartModel" 两步，再返回给 `processDocx.ts`。

## 目录结构

- `index.ts` — 公共 API，自动注册默认 Handler
- `types.ts` — `WordChartModel` 等渲染层类型定义（ECharts 输入模型）
- `chartDetector.ts` — `ChartTypeHandler`/`ChartParseContext` 接口
- `chartRegistry.ts` — Handler 注册表（Registry 模式）
- `detectors/` — 图表类型探测与分发
  - `analyzerBridge.ts` — 桥接层：调用 `chart-analysis` 并投影为 `WordChartModel`
  - `barChartDetector.ts` — 柱状图/条形图类型判定（结构探测，不解析数据）
  - `barChartHandler.ts` / `lineChartHandler.ts` / `pieChartHandler.ts` /
    `doughnutChartHandler.ts` / `areaChartHandler.ts` / `scatterChartHandler.ts` /
    `comboChartHandler.ts` — 各类型 Handler，`canHandle` 判定类型，`parse` 委托给
    `analyzerBridge`
  - `unsupportedChartDetector.ts` — 兜底 Handler；仍会调用
    `analyzerBridge`，因此雷达图等不支持预览的图表也能获得完整结构化数据，只是
    `toWordChartModel` 会将其投影为占位符
- `mappers/` — `WordChartModel` → `EChartsOption`（纯函数，不读 XML）
- `renderers/` — ECharts 挂载和不支持占位符渲染
- `utils/` — `emuUtils.ts`（EMU 转换）、`numberUtils.ts`（数值清理）、
  `colorUtils.ts`（渲染层调色板兜底，真正的主题色解析在 `chart-analysis` 中）

## 扩展指南

添加新的可预览图表类型（如雷达图）的步骤：

1. 在 `../chart-analysis/parsers/chartTypeElements.ts` 中把该类型的
   `previewable` 改为 `true`，并在 `../chart-analysis/render/toWordChartModel.ts`
   中补充对应的投影函数。
2. 创建 `mappers/wordRadarChartToECharts.ts`。
3. 创建 `renderers/EChartsRadarRenderer.ts`。
4. 在 `../chart-analysis/models/types.ts` 的 `WordChartType` 与本模块
   `types.ts` 中补充/复用类型定义（如需要专属字段）。
5. 在 `processDocx.ts` 的渲染分发 `switch` 中新增分支。

不需要新增 Handler 或再次解析 XML——结构化解析已经在 `chart-analysis` 中统一完成。
