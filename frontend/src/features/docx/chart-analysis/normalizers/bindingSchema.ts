import type {
  ChartBindingMode,
  ChartBindingSchema,
  ChartBindingSlot,
  ChartCategoryDefinition,
  ChartIdentity,
  ParsedChartSeries,
  WordChartType,
} from "../models/types";

const XY_TYPES: ReadonlySet<WordChartType> = new Set(["scatter", "bubble"]);

/**
 * Pure function: derives the (currently mostly non-bindable, structural)
 * binding schema from the parsed chart. Only "whole-dataset" is wired to
 * the existing backend upsertBinding flow today — every other slot is
 * generated with bindable:false so the UI can show what fine-grained
 * binding WILL look like without letting users drag onto it yet.
 *
 * Slot ids are derived deterministically from identity.chartId + role
 * (+ seriesKey when applicable) so re-parsing the same DOCX produces the
 * same ids.
 */
export function buildBindingSchema(
  identity: ChartIdentity,
  chartType: WordChartType,
  categories: ChartCategoryDefinition[],
  series: ParsedChartSeries[]
): ChartBindingSchema {
  const isXy = XY_TYPES.has(chartType);
  const slots: ChartBindingSlot[] = [];

  slots.push({
    id: `${identity.chartId}::dataset`,
    role: "dataset",
    label: "整张数据集",
    description: "将图表整体绑定到一个集合字段，由后端按分类和系列写入图表缓存。",
    seriesKey: null,
    expectedDataType: "Array<Object>",
    required: true,
    bindable: true,
    currentSourceFormula: null,
    currentPointCount: series[0]?.values.pointCount ?? null,
  });

  const supportedModes: ChartBindingMode[] = ["whole-dataset"];

  if (isXy) {
    supportedModes.push("xy-series");
    for (const s of series) {
      slots.push(nameSlot(identity, s));
      slots.push({
        id: `${identity.chartId}::${s.key}::x-values`,
        role: "x-values",
        label: `${s.name} · X 值`,
        description: "散点/气泡图的 X 坐标序列。",
        seriesKey: s.key,
        expectedDataType: "Array<Number>",
        required: false,
        bindable: false,
        currentSourceFormula: s.xValues?.formula ?? null,
        currentPointCount: s.xValues?.pointCount ?? null,
      });
      slots.push({
        id: `${identity.chartId}::${s.key}::y-values`,
        role: "y-values",
        label: `${s.name} · Y 值`,
        description: "散点/气泡图的 Y 坐标序列。",
        seriesKey: s.key,
        expectedDataType: "Array<Number>",
        required: false,
        bindable: false,
        currentSourceFormula: s.yValues?.formula ?? null,
        currentPointCount: s.yValues?.pointCount ?? null,
      });
      if (s.bubbleSizes) {
        slots.push({
          id: `${identity.chartId}::${s.key}::bubble-sizes`,
          role: "bubble-sizes",
          label: `${s.name} · 气泡大小`,
          description: "气泡图的大小序列。",
          seriesKey: s.key,
          expectedDataType: "Array<Number>",
          required: false,
          bindable: false,
          currentSourceFormula: s.bubbleSizes.formula,
          currentPointCount: s.bubbleSizes.pointCount,
        });
      }
    }
  } else {
    supportedModes.push("category-and-series", "series-values");

    if (categories.length > 0) {
      slots.push({
        id: `${identity.chartId}::categories`,
        role: "categories",
        label: "分类",
        description: "图表分类轴的标签序列。",
        seriesKey: null,
        expectedDataType: "Array<String>",
        required: false,
        bindable: false,
        currentSourceFormula: categories.find((c) => c.sourceFormula)?.sourceFormula ?? null,
        currentPointCount: categories.length,
      });
    }

    for (const s of series) {
      slots.push(nameSlot(identity, s));
      slots.push({
        id: `${identity.chartId}::${s.key}::values`,
        role: "series-values",
        label: `${s.name} · 数值`,
        description: "该系列的数值序列。",
        seriesKey: s.key,
        expectedDataType: "Array<Number>",
        required: false,
        bindable: false,
        currentSourceFormula: s.values.formula,
        currentPointCount: s.values.pointCount,
      });
    }
  }

  return {
    schemaVersion: "1.0",
    supportedModes,
    defaultMode: "whole-dataset",
    slots,
  };
}

function nameSlot(identity: ChartIdentity, s: ParsedChartSeries): ChartBindingSlot {
  return {
    id: `${identity.chartId}::${s.key}::name`,
    role: "series-name",
    label: `${s.name} · 系列名称`,
    description: "该系列的名称来源。",
    seriesKey: s.key,
    expectedDataType: "String",
    required: false,
    bindable: false,
    currentSourceFormula: s.nameSource.formula,
    currentPointCount: s.nameSource.pointCount,
  };
}
