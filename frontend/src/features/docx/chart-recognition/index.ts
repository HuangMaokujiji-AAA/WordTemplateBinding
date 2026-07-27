// Chart recognition module — public API

// Types
export type {
  WordChartType,
  WordChartSeries,
  WordChartCategory,
  WordChartModel,
  WordBarChartModel,
  WordLineChartModel,
  WordPieChartModel,
  WordDoughnutChartModel,
  WordAreaChartModel,
  WordScatterChartModel,
  WordRadarChartModel,
  RadarStyle,
  WordComboChartModel,
} from "./types";

// Detector
export type {
  ChartDetectionResult,
  ChartTypeHandler,
  ChartParseContext,
} from "./chartDetector";

// Registry
export {
  registerChartHandler,
  getRegisteredHandlers,
  findHandler,
  clearHandlers,
} from "./chartRegistry";

// Handlers
export { BarChartHandler } from "./detectors/barChartHandler";
export { LineChartHandler } from "./detectors/lineChartHandler";
export { PieChartHandler } from "./detectors/pieChartHandler";
export { DoughnutChartHandler } from "./detectors/doughnutChartHandler";
export { AreaChartHandler } from "./detectors/areaChartHandler";
export { ScatterChartHandler } from "./detectors/scatterChartHandler";
export { RadarChartHandler } from "./detectors/radarChartHandler";
export { ComboChartHandler } from "./detectors/comboChartHandler";
export { UnsupportedChartHandler } from "./detectors/unsupportedChartDetector";

// Mappers
export { wordBarChartToECharts } from "./mappers/wordBarChartToECharts";
export { wordLineChartToECharts } from "./mappers/wordLineChartToECharts";
export { wordPieChartToECharts } from "./mappers/wordPieChartToECharts";
export { wordDoughnutChartToECharts } from "./mappers/wordDoughnutChartToECharts";
export { wordAreaChartToECharts } from "./mappers/wordAreaChartToECharts";
export { wordScatterChartToECharts } from "./mappers/wordScatterChartToECharts";
export { wordComboChartToECharts } from "./mappers/wordComboChartToECharts";
export { wordRadarChartToECharts } from "./mappers/wordRadarChartToECharts";

// Renderers
export { renderBarChart } from "./renderers/EChartsBarRenderer";
export { renderLineChart } from "./renderers/EChartsLineRenderer";
export { renderPieChart } from "./renderers/EChartsPieRenderer";
export { renderDoughnutChart } from "./renderers/EChartsDoughnutRenderer";
export { renderAreaChart } from "./renderers/EChartsAreaRenderer";
export { renderScatterChart } from "./renderers/EChartsScatterRenderer";
export { renderComboChart } from "./renderers/EChartsComboRenderer";
export { renderRadarChart } from "./renderers/EChartsRadarRenderer";
export { renderUnsupportedChart } from "./renderers/UnsupportedChartRenderer";

// Utils
export { normalizeChartNumber, formatChartValue } from "./utils/numberUtils";
export { emuToPixels, EMU_PER_PIXEL_AT_96_DPI } from "./utils/emuUtils";
export { resolveRadarScale } from "./utils/radarScale";

// Initialize default handlers
import { clearHandlers, registerChartHandler } from "./chartRegistry";
import { BarChartHandler } from "./detectors/barChartHandler";
import { LineChartHandler } from "./detectors/lineChartHandler";
import { PieChartHandler } from "./detectors/pieChartHandler";
import { DoughnutChartHandler } from "./detectors/doughnutChartHandler";
import { AreaChartHandler } from "./detectors/areaChartHandler";
import { ScatterChartHandler } from "./detectors/scatterChartHandler";
import { RadarChartHandler } from "./detectors/radarChartHandler";
import { ComboChartHandler } from "./detectors/comboChartHandler";
import { UnsupportedChartHandler } from "./detectors/unsupportedChartDetector";

/**
 * Initialize the chart recognition system with all known handlers.
 * Handlers are checked in registration order — specific handlers
 * (bar, line, pie, etc.) come first, UnsupportedChartHandler last
 * as the catch-all fallback.
 */
export function initChartRecognition(): void {
  // processDocx can run repeatedly in the same SPA session. Rebuild the
  // registry so handlers do not accumulate after every uploaded template.
  clearHandlers();
  registerChartHandler(BarChartHandler);
  registerChartHandler(LineChartHandler);
  registerChartHandler(PieChartHandler);
  registerChartHandler(DoughnutChartHandler);
  registerChartHandler(AreaChartHandler);
  registerChartHandler(ScatterChartHandler);
  registerChartHandler(RadarChartHandler);
  registerChartHandler(ComboChartHandler);
  // Must be registered LAST — it handles anything the above don't
  registerChartHandler(UnsupportedChartHandler);
}
