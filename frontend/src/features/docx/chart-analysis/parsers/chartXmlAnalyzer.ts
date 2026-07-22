import type JSZip from "jszip";
import { OOXML_NS } from "../../ooxml/namespaces";
import type {
  ChartCacheSource,
  ChartCacheSourceKind,
  ChartFormulaReference,
  ChartFormulaRole,
  ChartIdentity,
  ChartPlotGroup,
  ChartSourceMetadata,
  ParsedChartSeries,
  ParsedWordChart,
  WordChartType,
} from "../models/types";
import { ChartDiagnosticsCollector } from "../diagnostics/diagnostics";
import { assignAxisRoles, parseAllAxes } from "./axisAnalyzer";
import { parseCategoryContainer } from "./categoryAnalyzer";
import { CHART_TYPE_ELEMENTS, getDirectChartTypeChildren } from "./chartTypeElements";
import { readChartRelationships, type ChartRelationshipInfo } from "./chartRelationshipReader";
import { parseDataLabels } from "./dataLabelAnalyzer";
import { parseLegend } from "./legendAnalyzer";
import { parseSeries } from "./seriesAnalyzer";
import { parseChartStyle } from "./styleAnalyzer";
import { parseChartTitle as parseChartTitleText } from "./textAnalyzer";
import { findDocumentThemePath, readThemeColors } from "./themeColorReader";
import { reconcileWithWorkbook } from "./workbookReconciliation";
import { buildDataTable } from "../normalizers/dataTable";
import { buildBindingSchema } from "../normalizers/bindingSchema";

export interface ChartAnalysisInput {
  chartXml: Document;
  chartXmlPath: string;
  chartId: string;
  slotId: string;
  relationshipId: string;
  documentOrder: number;
  marker: string;
  widthPx: number;
  heightPx: number;
  widthEmu: number | null;
  heightEmu: number | null;
  zip: JSZip;
}

/**
 * Single entry point for turning one chart's XML into a ParsedWordChart.
 * This replaces the per-type XML traversal that used to live separately
 * inside each of the 7 chart-recognition parsers — every WordXChartModel
 * used for ECharts rendering is now derived FROM this model
 * (render/toWordChartModel.ts) instead of re-reading the XML.
 */
export async function analyzeChartXml(input: ChartAnalysisInput): Promise<ParsedWordChart> {
  const diagnostics = new ChartDiagnosticsCollector();
  const { chartXml, zip, chartXmlPath } = input;

  const partKey = normalizePartKey(chartXmlPath);

  const identity: ChartIdentity = {
    chartId: input.chartId,
    slotId: input.slotId,
    partKey,
    relationshipId: input.relationshipId,
    documentOrder: input.documentOrder,
    marker: input.marker,
  };

  const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  if (!plotArea) {
    diagnostics.error("plot-area-missing", "chart XML 中未找到 <c:plotArea>", { recoverable: false });
    return buildFallbackChart(identity, input, diagnostics, "unsupported");
  }

  const themePath = await findDocumentThemePathSafely(zip, diagnostics);
  const themeColors = await readThemeColors(zip, themePath);

  const chartTypeEls = getDirectChartTypeChildren(plotArea);
  if (chartTypeEls.length === 0) {
    diagnostics.warn("no-chart-type-element", "plotArea 中没有可识别的图表类型元素");
  }

  const { plotGroups, series } = buildPlotGroupsAndSeries(chartTypeEls, themeColors, diagnostics);

  const axes = parseAllAxes(plotArea);
  assignAxisRoles(axes, plotGroups.map((g) => g.axisIds));
  applySeriesAxisRoles(series, plotGroups, axes);

  const categories = resolveCategories(chartTypeEls, diagnostics);

  const { title, autoTitleDeleted } = parseChartTitleText(chartXml);
  const legend = parseLegend(chartXml);
  const chartLevelDataLabels = resolveChartLevelDataLabels(chartTypeEls);
  const style = parseChartStyle(chartXml, themeColors);

  const { chartType, typeLabel, supportedForPreview } = classifyChartType(chartTypeEls, plotGroups);

  const relInfo = await resolveExternalData(chartXml, zip, chartXmlPath, diagnostics);

  const source: ChartSourceMetadata = {
    chartPartPath: partKey,
    chartRelationshipPath: relInfo.relsPath,
    externalDataRelationshipId: relInfo.externalDataRelationshipId,
    embeddedWorkbookPath: relInfo.embeddedWorkbookPath,
    embeddedWorkbookDetected: relInfo.embeddedWorkbookPath != null,
    formulas: collectFormulaReferences(series, categories),
    cacheSources: collectCacheSources(series, categories),
    themePath,
  };

  if (relInfo.embeddedWorkbookPath) {
    await reconcileWithWorkbook(zip, relInfo.embeddedWorkbookPath, series, categories, diagnostics);
  }

  const dataTable = buildDataTable(chartType, categories, series);
  const bindingSchema = buildBindingSchema(identity, chartType, categories, series);

  const dimensions = {
    widthPx: input.widthPx,
    heightPx: input.heightPx,
    widthEmu: input.widthEmu,
    heightEmu: input.heightEmu,
  };

  const modules = {
    identity: "complete" as const,
    data: series.length > 0 ? ("complete" as const) : ("partial" as const),
    axes: axes.length > 0 ? ("complete" as const) : ("partial" as const),
    style: "partial" as const,
    workbook: relInfo.embeddedWorkbookPath ? ("complete" as const) : ("missing" as const),
  };

  return {
    schemaVersion: "1.0",
    identity,
    source,
    type: chartType,
    typeLabel,
    supportedForParsing: true,
    supportedForPreview,
    supportedForBinding: series.length > 0 && series.some((s) => s.values.points.length > 0),
    title,
    autoTitleDeleted,
    dimensions,
    layout: { manualLayout: false },
    plotGroups,
    axes,
    legend,
    dataLabels: chartLevelDataLabels,
    categories,
    series,
    dataTable,
    bindingSchema,
    style,
    diagnostics: diagnostics.build(modules),
  };
}

function normalizePartKey(path: string): string {
  const normalized = path.replace(/\\/g, "/");
  return normalized.startsWith("/") ? normalized : `/${normalized}`;
}

async function findDocumentThemePathSafely(
  zip: JSZip,
  diagnostics: ChartDiagnosticsCollector
): Promise<string | null> {
  try {
    return await findDocumentThemePath(zip);
  } catch {
    diagnostics.info("theme-lookup-failed", "未能定位文档主题（theme1.xml），配色将使用默认调色板");
    return null;
  }
}

function buildPlotGroupsAndSeries(
  chartTypeEls: Element[],
  themeColors: Record<string, string>,
  diagnostics: ChartDiagnosticsCollector
): { plotGroups: ChartPlotGroup[]; series: ParsedChartSeries[] } {
  const plotGroups: ChartPlotGroup[] = [];
  const allSeries: ParsedChartSeries[] = [];

  chartTypeEls.forEach((el, groupOrder) => {
    const info = CHART_TYPE_ELEMENTS[el.localName];
    if (!info) return;

    const plotGroupId = `pg${groupOrder}`;
    const axisIds = Array.from(el.getElementsByTagNameNS(OOXML_NS.c, "axId"))
      .map((axIdEl) => axIdEl.getAttribute("val"))
      .filter((v): v is string => v != null);

    let resolvedType = info.type;
    let barDirection: "bar" | "col" | null = null;
    if (el.localName === "barChart") {
      const barDirEl = el.getElementsByTagNameNS(OOXML_NS.c, "barDir")[0];
      barDirection = barDirEl?.getAttribute("val") === "bar" ? "bar" : "col";
      resolvedType = barDirection === "bar" ? "bar" : "column";
    }

    const groupingEl = el.getElementsByTagNameNS(OOXML_NS.c, "grouping")[0];
    const groupingVal = groupingEl?.getAttribute("val") ?? null;
    const grouping =
      groupingVal === "stacked" || groupingVal === "percentStacked" || groupingVal === "clustered" || groupingVal === "standard"
        ? groupingVal
        : null;

    const scatterStyleEl = el.getElementsByTagNameNS(OOXML_NS.c, "scatterStyle")[0];
    const gapWidthEl = el.getElementsByTagNameNS(OOXML_NS.c, "gapWidth")[0];
    const overlapEl = el.getElementsByTagNameNS(OOXML_NS.c, "overlap")[0];
    const varyColorsEl = el.getElementsByTagNameNS(OOXML_NS.c, "varyColors")[0];
    const holeSizeEl = el.getElementsByTagNameNS(OOXML_NS.c, "holeSize")[0];

    const serEls = Array.from(el.children).filter(
      (c) => c.namespaceURI === OOXML_NS.c && c.localName === "ser"
    );

    if (serEls.length === 0) {
      diagnostics.warn("plot-group-no-series", `图表组 ${el.localName} 未包含任何 <c:ser>`, {
        path: el.localName,
      });
    }

    const groupSeries = serEls.map((serEl, i) =>
      parseSeries(serEl, i, plotGroupId, resolvedType, axisIds, themeColors, plotGroupId)
    );
    allSeries.push(...groupSeries);

    plotGroups.push({
      id: plotGroupId,
      order: groupOrder,
      type: resolvedType,
      grouping,
      barDirection,
      scatterStyle: scatterStyleEl?.getAttribute("val") ?? null,
      seriesKeys: groupSeries.map((s) => s.key),
      axisIds,
      gapWidth: gapWidthEl ? parseInt(gapWidthEl.getAttribute("val") ?? "", 10) : null,
      overlap: overlapEl ? parseInt(overlapEl.getAttribute("val") ?? "", 10) : null,
      varyColors: varyColorsEl ? varyColorsEl.getAttribute("val") !== "0" : null,
      holeSizePercent: holeSizeEl ? parseInt(holeSizeEl.getAttribute("val") ?? "50", 10) : null,
    });
  });

  return { plotGroups, series: allSeries };
}

/**
 * Assigns each series' axisRole (primary/secondary) based on which of its
 * plot group's axIds resolve to a "y"/"value" axis vs "secondary-y". This
 * replaces the legacy heuristic (count total distinct axId, assume all
 * line series are secondary and all bar series are primary) with a real
 * per-group axId → axis-role lookup.
 */
function applySeriesAxisRoles(
  series: ParsedChartSeries[],
  plotGroups: ChartPlotGroup[],
  axes: ReturnType<typeof parseAllAxes>
): void {
  const axisById = new Map(axes.map((a) => [a.id, a]));
  const groupById = new Map(plotGroups.map((g) => [g.id, g]));

  for (const s of series) {
    const group = groupById.get(s.plotGroupId);
    if (!group) continue;
    const hasSecondaryValueAxis = group.axisIds.some((id) => axisById.get(id)?.role === "secondary-y");
    s.axisRole = hasSecondaryValueAxis ? "secondary" : "primary";
  }
}

function resolveCategories(
  chartTypeEls: Element[],
  diagnostics: ChartDiagnosticsCollector
) {
  for (const el of chartTypeEls) {
    const firstSer = el.getElementsByTagNameNS(OOXML_NS.c, "ser")[0];
    if (!firstSer) continue;
    const cat = firstSer.getElementsByTagNameNS(OOXML_NS.c, "cat")[0];
    if (!cat) continue;
    const categories = parseCategoryContainer(cat);
    if (categories.length > 0) return categories;
  }
  if (chartTypeEls.length > 0) {
    diagnostics.info("no-categories", "未找到分类轴数据（散点图/气泡图属于正常情况）");
  }
  return [];
}

function resolveChartLevelDataLabels(chartTypeEls: Element[]) {
  for (const el of chartTypeEls) {
    const labels = parseDataLabels(el);
    if (labels) return labels;
  }
  return null;
}

function classifyChartType(
  chartTypeEls: Element[],
  plotGroups: ChartPlotGroup[]
): { chartType: WordChartType; typeLabel: string; supportedForPreview: boolean } {
  if (chartTypeEls.length === 0) {
    return { chartType: "unsupported", typeLabel: "未知图表", supportedForPreview: false };
  }

  if (chartTypeEls.length === 1) {
    const info = CHART_TYPE_ELEMENTS[chartTypeEls[0].localName];
    const resolvedType = plotGroups[0]?.type ?? info.type;
    return {
      chartType: resolvedType,
      typeLabel: TYPE_LABELS[resolvedType] ?? resolvedType,
      supportedForPreview: info.previewable,
    };
  }

  // Combo: preview is currently only wired for bar+line(+area-as-line) mixes.
  const previewableCombo = chartTypeEls.every(
    (el) => el.localName === "barChart" || el.localName === "lineChart" || el.localName === "areaChart"
  );
  return { chartType: "combo", typeLabel: "组合图", supportedForPreview: previewableCombo };
}

const TYPE_LABELS: Record<WordChartType, string> = {
  bar: "条形图",
  column: "柱形图",
  line: "折线图",
  pie: "饼图",
  doughnut: "环形图",
  area: "面积图",
  scatter: "散点图",
  bubble: "气泡图",
  radar: "雷达图",
  stock: "股票图",
  surface: "曲面图",
  combo: "组合图",
  unsupported: "不支持的图表",
};

async function resolveExternalData(
  chartXml: Document,
  zip: JSZip,
  chartXmlPath: string,
  diagnostics: ChartDiagnosticsCollector
): Promise<ChartRelationshipInfo> {
  const chartSpace = chartXml.documentElement;
  const externalDataEl = chartSpace
    ? Array.from(chartSpace.children).find(
        (c) => c.namespaceURI === OOXML_NS.c && c.localName === "externalData"
      )
    : undefined;
  const externalDataRelationshipId =
    externalDataEl?.getAttributeNS(OOXML_NS.r, "id") ?? externalDataEl?.getAttribute("r:id") ?? null;

  try {
    return await readChartRelationships(zip, chartXmlPath, externalDataRelationshipId);
  } catch {
    diagnostics.warn("chart-relationships-unreadable", "未能读取图表关系文件（chartN.xml.rels）");
    return { relsPath: null, externalDataRelationshipId, embeddedWorkbookPath: null };
  }
}

function collectFormulaReferences(
  series: ParsedChartSeries[],
  categories: ReturnType<typeof parseCategoryContainer>
): ChartFormulaReference[] {
  const refs: ChartFormulaReference[] = [];

  const categoryFormula = categories.find((c) => c.sourceFormula)?.sourceFormula ?? null;
  if (categoryFormula) {
    refs.push(...toFormulaRef("category", null, categoryFormula));
  }

  for (const s of series) {
    if (s.nameSource.formula) refs.push(...toFormulaRef("series-name", s.index, s.nameSource.formula));
    if (s.values.formula) refs.push(...toFormulaRef("value", s.index, s.values.formula));
    if (s.xValues?.formula) refs.push(...toFormulaRef("x-value", s.index, s.xValues.formula));
    if (s.yValues?.formula) refs.push(...toFormulaRef("y-value", s.index, s.yValues.formula));
    if (s.bubbleSizes?.formula) refs.push(...toFormulaRef("bubble-size", s.index, s.bubbleSizes.formula));
  }

  return refs;
}

function toFormulaRef(
  role: ChartFormulaRole,
  seriesIndex: number | null,
  formula: string
): ChartFormulaReference[] {
  const parsed = splitSheetAndRange(formula);
  return [{ role, seriesIndex, formula, sheetName: parsed.sheetName, rangeAddress: parsed.rangeAddress }];
}

function splitSheetAndRange(formula: string): { sheetName: string | null; rangeAddress: string | null } {
  const match = formula.trim().match(/^(?:'([^']+)'|([^!]+))!(.+)$/);
  if (!match) return { sheetName: null, rangeAddress: null };
  return { sheetName: match[1] ?? match[2], rangeAddress: match[3] };
}

function collectCacheSources(
  series: ParsedChartSeries[],
  categories: ReturnType<typeof parseCategoryContainer>
): ChartCacheSource[] {
  const sources: ChartCacheSource[] = [];

  if (categories.length > 0) {
    sources.push({
      kind: "category",
      seriesIndex: null,
      pointCount: categories.length,
      hasCache: categories.some((c) => !c.isMissing),
    });
  }

  const pushSource = (kind: ChartCacheSourceKind, seriesIndex: number, src: { sourceKind: string; pointCount: number | null; points: Array<{ isMissing: boolean }> } | undefined) => {
    if (!src) return;
    sources.push({
      kind,
      seriesIndex,
      pointCount: src.pointCount,
      hasCache: src.sourceKind === "reference" || src.sourceKind === "literal",
    });
  };

  for (const s of series) {
    pushSource("series-name", s.index, s.nameSource);
    pushSource("value", s.index, s.values);
    pushSource("x-value", s.index, s.xValues);
    pushSource("y-value", s.index, s.yValues);
    pushSource("bubble-size", s.index, s.bubbleSizes);
  }

  return sources;
}

function buildFallbackChart(
  identity: ChartIdentity,
  input: ChartAnalysisInput,
  diagnostics: ChartDiagnosticsCollector,
  type: WordChartType
): ParsedWordChart {
  return {
    schemaVersion: "1.0",
    identity,
    source: {
      chartPartPath: identity.partKey,
      chartRelationshipPath: null,
      externalDataRelationshipId: null,
      embeddedWorkbookPath: null,
      embeddedWorkbookDetected: false,
      formulas: [],
      cacheSources: [],
      themePath: null,
    },
    type,
    typeLabel: TYPE_LABELS[type],
    supportedForParsing: false,
    supportedForPreview: false,
    supportedForBinding: false,
    title: null,
    autoTitleDeleted: false,
    dimensions: {
      widthPx: input.widthPx,
      heightPx: input.heightPx,
      widthEmu: input.widthEmu,
      heightEmu: input.heightEmu,
    },
    layout: { manualLayout: false },
    plotGroups: [],
    axes: [],
    legend: null,
    dataLabels: null,
    categories: [],
    series: [],
    dataTable: { orientation: "categories-as-rows", columns: [], rows: [], categoryColumnKey: null, seriesColumnKeys: [], rowCount: 0, columnCount: 0 },
    bindingSchema: { schemaVersion: "1.0", supportedModes: [], defaultMode: "whole-dataset", slots: [] },
    style: { chartStyleId: null, colorStyleId: null, chartAreaFill: null, plotAreaFill: null, roundedCorners: null },
    diagnostics: diagnostics.build({ identity: "complete", data: "missing", axes: "missing", style: "missing", workbook: "missing" }),
  };
}
