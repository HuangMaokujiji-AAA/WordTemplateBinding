# Chart Recognition Module

独立图表识别模块，负责从 OOXML Chart XML 中识别、解析和渲染 Word 原生图表。

## 架构

```
OOXML Chart XML
    ↓ (chartDetector)
ChartDetectionResult
    ↓ (Handler.parse)
WordChartModel (统一中间模型，与 ECharts 无关)
    ↓ (wordBarChartToECharts)
EChartsOption
    ↓ (EChartsBarRenderer / UnsupportedChartRenderer)
DOM 渲染
```

## 目录结构

- `index.ts` — 公共 API，自动注册默认 Handler
- `types.ts` — WordChartModel 等类型定义
- `chartDetector.ts` — Chart 类型检测接口
- `chartRegistry.ts` — Handler 注册表（Registry 模式）
- `detectors/` — 图表类型探测器
  - `barChartDetector.ts` — 柱状图/条形图检测
  - `barChartHandler.ts` — 柱状图 Handler
  - `unsupportedChartDetector.ts` — 不支持图表 Handler（回退）
- `parsers/` — OOXML 解析器
  - `barChartParser.ts` — 柱状图主解析器
  - `chartSeriesParser.ts` — 系列数据解析
  - `chartCategoryParser.ts` — 分类轴解析
  - `multiLevelCategoryParser.ts` — 多级分类轴解析
  - `chartAxisParser.ts` — 坐标轴解析
  - `chartLegendParser.ts` — 图例解析
  - `chartStyleParser.ts` — 样式解析
  - `chartDataLabelParser.ts` — 数据标签解析
  - `embeddedWorkbookFallback.ts` — 嵌入 Excel 回退
- `mappers/` — 模型映射器
  - `wordBarChartToECharts.ts` — WordChartModel → EChartsOption
- `renderers/` — 渲染器
  - `EChartsBarRenderer.ts` — ECharts 柱状图渲染
  - `UnsupportedChartRenderer.ts` — 不支持图表占位渲染
- `utils/` — 工具函数
  - `emuUtils.ts` — EMU 单位转换
  - `numberUtils.ts` — 浮点数清理
  - `colorUtils.ts` — 颜色解析
  - `chartPathUtils.ts` — 图表路径解析

## 扩展指南

添加新图表类型（如折线图）的步骤：

1. 创建 `detectors/lineChartDetector.ts`
2. 创建 `detectors/lineChartHandler.ts`（实现 ChartTypeHandler 接口）
3. 创建 `parsers/lineChartParser.ts`
4. 创建 `mappers/wordLineChartToECharts.ts`
5. 创建 `renderers/EChartsLineRenderer.ts`
6. 在 `index.ts` 中注册新 Handler

不修改主流程代码。
