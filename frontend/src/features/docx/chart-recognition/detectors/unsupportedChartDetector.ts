import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";

/**
 * Detect what type of chart this is (for unsupported chart messages).
 */
function detectChartType(xmlDoc: Document): string {
  const plotArea = xmlDoc.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
  if (!plotArea) return "unknown";

  const types: string[] = [];
  for (const child of Array.from(plotArea.children)) {
    if (child.namespaceURI === OOXML_NS.c) {
      types.push(child.localName);
    }
  }

  if (types.length === 1) return types[0];
  if (types.length > 1) return "combo";
  return "unknown";
}

/**
 * Handler for unsupported charts.
 *
 * This handler is registered last and catches any chart that
 * isn't handled by a more specific handler.
 */
export const UnsupportedChartHandler: ChartTypeHandler = {
  canHandle(_chartXml: Document): boolean {
    // Always returns true as the fallback — it must be registered last
    return true;
  },

  async parse(context: ChartParseContext): Promise<WordChartModel> {
    const detectedType = detectChartType(context.chartXml);

    return {
      id: context.chartId,
      relationshipId: context.relationshipId,
      sourcePath: context.chartXmlPath,
      type: "unsupported",
      categories: [],
      series: [],
      widthPx: context.widthPx,
      heightPx: context.heightPx,
      unsupportedReason: `当前图表类型暂不支持网页渲染\n图表类型：${detectedType}`,
    };
  },
};
