import JSZip from "jszip";
import { validateDocxFile } from "./file-validator/validateDocxFile";
import { locateDocumentCharts } from "./ooxml/documentChartLocator";
import { injectChartMarkers } from "./ooxml/docxMarkerInjector";
import { parseXmlString } from "./ooxml/xmlUtils";
import { renderDocx } from "./rendering/renderDocx";
import {
  replaceChartMarkers,
  type ChartMarkerMeta,
} from "./rendering/replaceChartMarkers";
import { chartInstanceManager } from "./rendering/chartInstanceManager";
import { analyzeChartXml } from "./chart-analysis/parsers/chartXmlAnalyzer";
import { toWordChartModel } from "./chart-analysis/render/toWordChartModel";
import type { ParsedWordChart } from "./chart-analysis/models/types";
import type { ChartDiagnostics } from "./chart-analysis/diagnostics/diagnostics";
import { renderBarChart } from "./chart-recognition/renderers/EChartsBarRenderer";
import { renderLineChart } from "./chart-recognition/renderers/EChartsLineRenderer";
import { renderPieChart } from "./chart-recognition/renderers/EChartsPieRenderer";
import { renderDoughnutChart } from "./chart-recognition/renderers/EChartsDoughnutRenderer";
import { renderAreaChart } from "./chart-recognition/renderers/EChartsAreaRenderer";
import { renderScatterChart } from "./chart-recognition/renderers/EChartsScatterRenderer";
import { renderComboChart } from "./chart-recognition/renderers/EChartsComboRenderer";
import { renderRadarChart } from "./chart-recognition/renderers/EChartsRadarRenderer";
import { renderUnsupportedChart } from "./chart-recognition/renderers/UnsupportedChartRenderer";
import type {
  WordChartModel,
  WordBarChartModel,
  WordLineChartModel,
  WordPieChartModel,
  WordDoughnutChartModel,
  WordAreaChartModel,
  WordScatterChartModel,
  WordRadarChartModel,
  WordComboChartModel,
} from "./chart-recognition/types";

export interface DocxProcessProgress {
  stage:
    | "idle"
    | "validating"
    | "locating-charts"
    | "injecting-markers"
    | "rendering-document"
    | "parsing-charts"
    | "rendering-charts"
    | "completed"
    | "failed";
  message: string;
  progress: number;
}

export type ChartProcessStatus = "rendered" | "partially-rendered" | "unsupported" | "failed";

export interface ParsedChartProcessResult {
  slotId: string;
  sourcePath: string;
  relationshipId: string;
  documentOrder: number;

  detectedType: string;

  status: ChartProcessStatus;
  message?: string;

  /** Full chart-analysis model. Null only when analysis itself failed (status "failed"). */
  model: ParsedWordChart | null;
  diagnostics: ChartDiagnostics | null;
}

export interface DocxProcessResult {
  totalCharts: number;
  renderedCharts: number;
  partiallyRenderedCharts: number;
  unsupportedCharts: number;
  failedCharts: number;

  charts: ParsedChartProcessResult[];

  warnings: string[];
}

export interface ProcessDocxOptions {
  documentContainer: HTMLElement;
  styleContainer: HTMLElement;
  onProgress?: (progress: DocxProcessProgress) => void;
}

/**
 * Main processing pipeline for a DOCX file.
 *
 * Flow:
 *  1. Validate file
 *  2. Unzip with JSZip
 *  3. Locate all Word charts in document.xml
 *  4. Inject text markers in place of chart drawings
 *  5. Re-zip and render with docx-preview
 *  6. Find markers in rendered DOM
 *  7. Analyze each chart's XML into a ParsedWordChart (chart-analysis)
 *  8. Project each ParsedWordChart into a WordChartModel and render with
 *     ECharts (or the unsupported placeholder)
 *
 * Every chart is analyzed exactly once per upload — the ECharts option is
 * derived FROM the ParsedWordChart (chart-analysis/render/toWordChartModel),
 * never by re-reading the chart XML.
 *
 * @param file - The uploaded .docx file.
 * @param options - Containers and progress callback.
 * @returns Processing result with per-chart structured models and statistics.
 */
export async function processDocx(
  file: File,
  options: ProcessDocxOptions
): Promise<DocxProcessResult> {
  const warnings: string[] = [];
  const chartResults: ParsedChartProcessResult[] = [];

  try {
    // Stage 1: Validate
    options.onProgress?.({
      stage: "validating",
      message: "正在校验文件...",
      progress: 5,
    });

    const arrayBuffer = await file.arrayBuffer();
    const zip = await JSZip.loadAsync(arrayBuffer);

    const validationResult = await validateDocxFile(file, zip);
    if (!validationResult.valid) {
      throw new Error(validationResult.error ?? "文件校验失败");
    }

    // Stage 2: Locate charts
    options.onProgress?.({
      stage: "locating-charts",
      message: "正在定位图表位置...",
      progress: 15,
    });

    const locatedCharts = await locateDocumentCharts(zip);

    // Stage 3: Inject markers
    options.onProgress?.({
      stage: "injecting-markers",
      message: "正在注入图表标记...",
      progress: 25,
    });

    // Read the document.xml to inject markers
    const docXmlFile = zip.file("word/document.xml");
    if (!docXmlFile) {
      throw new Error("word/document.xml not found");
    }
    const docXmlString = await docXmlFile.async("text");

    injectChartMarkers(zip, locatedCharts, docXmlString);

    // Generate the modified DOCX blob
    options.onProgress?.({
      stage: "injecting-markers",
      message: "正在生成预览文档...",
      progress: 35,
    });

    const modifiedBlob = await zip.generateAsync({
      type: "blob",
      mimeType:
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      compression: "DEFLATE",
    });

    // Stage 4: Render document with docx-preview
    options.onProgress?.({
      stage: "rendering-document",
      message: "正在渲染文档...",
      progress: 45,
    });

    // Clear previous content
    options.documentContainer.innerHTML = "";
    options.styleContainer.innerHTML = "";

    await renderDocx(
      modifiedBlob,
      options.documentContainer,
      options.styleContainer
    );

    // Stage 5: Analyze charts
    options.onProgress?.({
      stage: "parsing-charts",
      message: "正在解析图表数据...",
      progress: 65,
    });

    // Build the chart model map (marker text → WordChartModel) for the
    // legacy marker-replacement/render step, alongside the full analysis
    // results keyed by slotId for the extended return value.
    const chartModelMap = new Map<string, WordChartModel>();
    const chartLocationMap = new Map(
      locatedCharts.map((chart) => [chart.marker, chart])
    );
    const chartMetaMap = new Map<string, ChartMarkerMeta>();

    // Re-read the original zip for chart XML files — the zip mutated in
    // Stage 3 has document.xml replaced with marker text, so chart XML
    // must come from an independent load of the untouched original bytes.
    const origZip = await JSZip.loadAsync(arrayBuffer);

    for (const chart of locatedCharts) {
      try {
        const chartXmlFile = origZip.file(chart.chartPath);
        if (!chartXmlFile) {
          warnings.push(`Chart XML not found: ${chart.chartPath}`);
          chartResults.push({
            slotId: chart.slotId,
            sourcePath: chart.chartPath,
            relationshipId: chart.relationshipId,
            documentOrder: chart.documentOrder,
            detectedType: "unknown",
            status: "failed",
            message: "Chart XML 文件未找到",
            model: null,
            diagnostics: null,
          });
          continue;
        }

        const chartXmlString = await chartXmlFile.async("text");
        const chartXml = parseXmlString(chartXmlString);

        const parsed = await analyzeChartXml({
          chartXml,
          chartXmlPath: chart.chartPath,
          chartId: chart.slotId,
          slotId: chart.slotId,
          relationshipId: chart.relationshipId,
          documentOrder: chart.documentOrder,
          marker: chart.marker,
          widthPx: chart.widthPx,
          heightPx: chart.heightPx,
          widthEmu: chart.widthEmu ?? null,
          heightEmu: chart.heightEmu ?? null,
          zip: origZip,
        });

        chartModelMap.set(chart.marker, toWordChartModel(parsed));
        chartMetaMap.set(chart.marker, {
          bindable: parsed.supportedForBinding,
          schemaVersion: parsed.schemaVersion,
        });

        const status: ChartProcessStatus = parsed.supportedForPreview
          ? (parsed.diagnostics.hasErrors || parsed.diagnostics.hasWarnings
              ? "partially-rendered"
              : "rendered")
          : "unsupported";

        chartResults.push({
          slotId: chart.slotId,
          sourcePath: chart.chartPath,
          relationshipId: chart.relationshipId,
          documentOrder: chart.documentOrder,
          detectedType: parsed.type,
          status,
          model: parsed,
          diagnostics: parsed.diagnostics,
        });
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        warnings.push(`Chart ${chart.chartPath} parsing failed: ${msg}`);
        chartResults.push({
          slotId: chart.slotId,
          sourcePath: chart.chartPath,
          relationshipId: chart.relationshipId,
          documentOrder: chart.documentOrder,
          detectedType: "unknown",
          status: "failed",
          message: msg,
          model: null,
          diagnostics: null,
        });
      }
    }

    // Stage 6: Replace markers and render charts
    options.onProgress?.({
      stage: "rendering-charts",
      message: "正在渲染图表...",
      progress: 75,
    });

    const replacedSlots = replaceChartMarkers(
      options.documentContainer,
      chartModelMap,
      chartLocationMap,
      chartMetaMap
    );

    // Render each chart
    for (const slot of replacedSlots) {
      try {
        const model = slot.model;
        const canvasEl = slot.canvasElement;

        // Ensure the canvas element has proper styling
        canvasEl.style.width = "100%";
        canvasEl.style.height = "100%";

        let echartsInstance: ReturnType<typeof renderBarChart> = null;

        switch (model.type) {
          case "bar":
          case "column":
            echartsInstance = renderBarChart(model as WordBarChartModel, canvasEl);
            break;
          case "line":
            echartsInstance = renderLineChart(model as WordLineChartModel, canvasEl);
            break;
          case "pie":
            echartsInstance = renderPieChart(model as WordPieChartModel, canvasEl);
            break;
          case "doughnut":
            echartsInstance = renderDoughnutChart(model as WordDoughnutChartModel, canvasEl);
            break;
          case "area":
            echartsInstance = renderAreaChart(model as WordAreaChartModel, canvasEl);
            break;
          case "scatter":
            echartsInstance = renderScatterChart(model as WordScatterChartModel, canvasEl);
            break;
          case "combo":
            echartsInstance = renderComboChart(model as WordComboChartModel, canvasEl);
            break;
          case "radar":
            echartsInstance = renderRadarChart(model as WordRadarChartModel, canvasEl);
            break;
          case "unsupported": {
            const parentEl = canvasEl.parentElement;
            if (parentEl) {
              parentEl.innerHTML = "";
              parentEl.classList.add("docx-chart-slot");
              renderUnsupportedChart(model, parentEl);
            }
            break;
          }
        }

        if (!echartsInstance && model.type !== "unsupported") {
          const resultEntry = chartResults.find(
            (r) => r.slotId === slot.slotId
          );
          if (resultEntry) {
            resultEntry.status = "failed";
            resultEntry.message = "ECharts 渲染失败";
          }
          continue;
        }

        if (echartsInstance) {
          chartInstanceManager.register(slot.slotId, echartsInstance);
          chartInstanceManager.observeResize(
            slot.slotId,
            echartsInstance,
            (canvasEl.parentElement as HTMLElement) || canvasEl
          );
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        warnings.push(`Chart ${slot.slotId} rendering failed: ${msg}`);
        const resultEntry = chartResults.find((r) => r.slotId === slot.slotId);
        if (resultEntry) {
          resultEntry.status = "failed";
          resultEntry.message = msg;
        }
      }
    }

    // Compute final stats
    const totalCharts = chartResults.length;
    const renderedCharts = chartResults.filter((r) => r.status === "rendered").length;
    const partiallyRenderedCharts = chartResults.filter((r) => r.status === "partially-rendered").length;
    const unsupportedCharts = chartResults.filter((r) => r.status === "unsupported").length;
    const failedCharts = chartResults.filter((r) => r.status === "failed").length;

    options.onProgress?.({
      stage: "completed",
      message: "渲染完成",
      progress: 100,
    });

    return {
      totalCharts,
      renderedCharts,
      partiallyRenderedCharts,
      unsupportedCharts,
      failedCharts,
      charts: chartResults,
      warnings,
    };
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    options.onProgress?.({
      stage: "failed",
      message: msg,
      progress: 0,
    });

    return {
      totalCharts: 0,
      renderedCharts: 0,
      partiallyRenderedCharts: 0,
      unsupportedCharts: 0,
      failedCharts: 0,
      charts: [],
      warnings: [...warnings, msg],
    };
  }
}
