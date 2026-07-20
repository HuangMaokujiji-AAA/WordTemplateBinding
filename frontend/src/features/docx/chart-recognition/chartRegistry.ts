import type { ChartTypeHandler } from "./chartDetector";

const handlers: ChartTypeHandler[] = [];

/**
 * Register a chart type handler.
 */
export function registerChartHandler(handler: ChartTypeHandler): void {
  handlers.push(handler);
}

/**
 * Get all registered chart type handlers.
 */
export function getRegisteredHandlers(): readonly ChartTypeHandler[] {
  return handlers;
}

/**
 * Find the first handler that can handle the given chart XML.
 */
export function findHandler(
  chartXml: Document
): ChartTypeHandler | undefined {
  return handlers.find((h) => h.canHandle(chartXml));
}

/**
 * Clear all registered handlers (useful for testing).
 */
export function clearHandlers(): void {
  handlers.length = 0;
}
