import type {
  WordAreaChartModel,
  WordBarChartModel,
  WordChartCategory,
  WordChartModel,
  WordChartSeries,
  WordComboChartModel,
  WordDoughnutChartModel,
  WordLineChartModel,
  WordPieChartModel,
  WordScatterChartModel,
} from "../../chart-recognition/types";
import type {
  ChartAxisDefinition,
  ChartCategoryDefinition,
  ChartPlotGroup,
  ParsedChartSeries,
  ParsedWordChart,
  WordChartType,
} from "../models/types";

/**
 * Projects a ParsedWordChart into the legacy WordChartModel shapes so the
 * existing 7 ECharts mappers/renderers (wordBarChartToECharts, etc.) keep
 * working unmodified. This is the ONLY place that reads ParsedWordChart to
 * produce ECharts input — the mappers/renderers never touch chart XML and
 * never see ParsedWordChart directly.
 */
export function toWordChartModel(parsed: ParsedWordChart): WordChartModel {
  if (!parsed.supportedForPreview) {
    return toUnsupportedModel(parsed);
  }

  switch (parsed.type) {
    case "bar":
    case "column":
      return toBarChartModel(parsed);
    case "line":
      return toLineChartModel(parsed);
    case "pie":
      return toPieChartModel(parsed);
    case "doughnut":
      return toDoughnutChartModel(parsed);
    case "area":
      return toAreaChartModel(parsed);
    case "scatter":
      return toScatterChartModel(parsed);
    case "combo":
      return toComboChartModel(parsed);
    default:
      return toUnsupportedModel(parsed);
  }
}

function baseFields(parsed: ParsedWordChart) {
  return {
    id: parsed.identity.chartId,
    relationshipId: parsed.identity.relationshipId,
    sourcePath: parsed.source.chartPartPath,
    widthPx: parsed.dimensions.widthPx,
    heightPx: parsed.dimensions.heightPx,
    title: parsed.title?.plainText,
  };
}

function toUnsupportedModel(parsed: ParsedWordChart): WordChartModel {
  return {
    ...baseFields(parsed),
    type: "unsupported",
    categories: [],
    series: [],
    unsupportedReason: `当前图表类型暂不支持网页渲染\n图表类型：${parsed.typeLabel}`,
  };
}

function mapCategories(categories: ChartCategoryDefinition[]): WordChartCategory[] {
  let previousInnerLevel: string | undefined;
  return categories.map((c) => {
    const displayValue = c.displayValue || (c.value == null ? "" : String(c.value));
    const innerLevel = c.levels[0];
    const isGroupStart = previousInnerLevel === undefined || innerLevel !== previousInnerLevel;
    previousInnerLevel = innerLevel;
    return {
      value: c.value == null ? "" : String(c.value),
      levels: c.levels.length > 0 ? c.levels : undefined,
      displayValue,
      isGroupStart,
    };
  });
}

function mapSeriesChartType(t: WordChartType): string {
  if (t === "bar" || t === "column") return "bar";
  if (t === "line" || t === "area") return "line";
  return t;
}

function mapSeries(series: ParsedChartSeries[]): WordChartSeries[] {
  return series.map((s) => ({
    name: s.name,
    values: s.values.points.map((p) => p.value),
    xValues: s.xValues ? s.xValues.points.map((p) => p.value) : undefined,
    chartType: mapSeriesChartType(s.chartType),
    axis: s.axisRole === "none" ? undefined : s.axisRole,
    color: s.style.fill?.color?.resolvedHex ?? undefined,
    showValueLabel: s.dataLabels?.showValue,
    dataLabelPosition: s.dataLabels?.position ?? undefined,
    sourceFormula: s.values.formula ?? undefined,
    numberFormat: s.values.formatCode ?? undefined,
  }));
}

function toLegacyAxisInfo(axis: ChartAxisDefinition | undefined) {
  if (!axis) return undefined;
  return {
    title: axis.title?.plainText,
    min: axis.min ?? undefined,
    max: axis.max ?? undefined,
    majorUnit: axis.majorUnit ?? undefined,
    numberFormat: axis.numberFormat ?? undefined,
    reversed: axis.reversed,
  };
}

function getAxisByRole(
  axes: ChartAxisDefinition[],
  role: ChartAxisDefinition["role"]
): ChartAxisDefinition | undefined {
  return axes.find((a) => a.role === role);
}

function mapLegend(parsed: ParsedWordChart): WordChartModel["legend"] {
  if (!parsed.legend) return undefined;
  const position = parsed.legend.position === "topRight" ? "right" : parsed.legend.position;
  return {
    visible: parsed.legend.visible,
    position: position ?? undefined,
  };
}

function toBarChartModel(parsed: ParsedWordChart): WordBarChartModel {
  const group = parsed.plotGroups[0];
  const catAxis = getAxisByRole(parsed.axes, "x");
  const valAxis = getAxisByRole(parsed.axes, "y");
  const barDirection = group?.barDirection ?? "col";

  const model: WordBarChartModel = {
    ...baseFields(parsed),
    type: parsed.type === "bar" ? "bar" : "column",
    grouping: (group?.grouping ?? "clustered") as WordBarChartModel["grouping"],
    barDirection,
    gapWidth: group?.gapWidth ?? undefined,
    overlap: group?.overlap ?? undefined,
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
  };

  if (barDirection === "col") {
    model.xAxis = toLegacyAxisInfo(catAxis);
    model.yAxis = toLegacyAxisInfo(valAxis);
  } else {
    model.xAxis = toLegacyAxisInfo(valAxis);
    model.yAxis = toLegacyAxisInfo(catAxis);
  }

  return model;
}

function toLineChartModel(parsed: ParsedWordChart): WordLineChartModel {
  const group = parsed.plotGroups[0];
  const catAxis = getAxisByRole(parsed.axes, "x");
  const valAxis = getAxisByRole(parsed.axes, "y");

  const showMarker = parsed.series.every((s) => !s.marker) || parsed.series.some(
    (s) => s.marker?.symbol && s.marker.symbol !== "none"
  );
  const smooth = parsed.series.some((s) => s.line?.smooth);

  return {
    ...baseFields(parsed),
    type: "line",
    grouping: (group?.grouping ?? "standard") as WordLineChartModel["grouping"],
    showMarker,
    smooth,
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
    xAxis: toLegacyAxisInfo(catAxis),
    yAxis: toLegacyAxisInfo(valAxis),
  };
}

function toPieChartModel(parsed: ParsedWordChart): WordPieChartModel {
  const explosion = parsed.series[0]?.style.explosion ?? undefined;

  return {
    ...baseFields(parsed),
    type: "pie",
    explosion: explosion ?? undefined,
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
  };
}

function toDoughnutChartModel(parsed: ParsedWordChart): WordDoughnutChartModel {
  const group = parsed.plotGroups[0];
  const explosion = parsed.series[0]?.style.explosion ?? undefined;

  return {
    ...baseFields(parsed),
    type: "doughnut",
    holeSize: group?.holeSizePercent ?? 50,
    explosion: explosion ?? undefined,
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
  };
}

function toAreaChartModel(parsed: ParsedWordChart): WordAreaChartModel {
  const group = parsed.plotGroups[0];
  const catAxis = getAxisByRole(parsed.axes, "x");
  const valAxis = getAxisByRole(parsed.axes, "y");

  return {
    ...baseFields(parsed),
    type: "area",
    grouping: (group?.grouping ?? "standard") as WordAreaChartModel["grouping"],
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
    xAxis: toLegacyAxisInfo(catAxis),
    yAxis: toLegacyAxisInfo(valAxis),
  };
}

function toScatterChartModel(parsed: ParsedWordChart): WordScatterChartModel {
  const group = parsed.plotGroups[0];
  const xAxis = getAxisByRole(parsed.axes, "x");
  const yAxis = getAxisByRole(parsed.axes, "y");
  const smooth = parsed.series.some((s) => s.line?.smooth);

  return {
    ...baseFields(parsed),
    type: "scatter",
    scatterStyle: smooth ? "smooth" : group?.scatterStyle ?? "lineMarker",
    categories: [],
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
    xAxis: toLegacyAxisInfo(xAxis),
    yAxis: toLegacyAxisInfo(yAxis),
  };
}

function toComboChartModel(parsed: ParsedWordChart): WordComboChartModel {
  const catAxis = getAxisByRole(parsed.axes, "x");
  const valAxis = getAxisByRole(parsed.axes, "y");
  const secondaryValAxis = getAxisByRole(parsed.axes, "secondary-y");
  const useSecondaryAxis = secondaryValAxis != null;

  return {
    ...baseFields(parsed),
    type: "combo",
    useSecondaryAxis,
    secondaryYAxis: useSecondaryAxis ? toLegacyAxisInfo(secondaryValAxis) : undefined,
    categories: mapCategories(parsed.categories),
    series: mapSeries(parsed.series),
    legend: mapLegend(parsed),
    xAxis: toLegacyAxisInfo(catAxis),
    yAxis: toLegacyAxisInfo(valAxis),
  };
}

export type { ChartPlotGroup };
