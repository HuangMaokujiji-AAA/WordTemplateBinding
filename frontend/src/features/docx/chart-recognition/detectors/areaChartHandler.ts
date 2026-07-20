import { OOXML_NS } from "../../ooxml/namespaces";
import type { ChartTypeHandler, ChartParseContext } from "../chartDetector";
import type { WordChartModel } from "../types";
import { parseAreaChart } from "../parsers/areaChartParser";

export const AreaChartHandler: ChartTypeHandler = {
  canHandle(chartXml: Document): boolean {
    const plotArea = chartXml.getElementsByTagNameNS(OOXML_NS.c, "plotArea")[0];
    if (!plotArea) return false;
    const children = plotArea.getElementsByTagNameNS(OOXML_NS.c, "areaChart");
    // Also reject area3DChart handled by unsupported handler
    if (children.length !== 1) return false;
    // Ensure no 3D variant
    const area3D = plotArea.getElementsByTagNameNS(OOXML_NS.c, "area3DChart");
    return area3D.length === 0;
  },

  parse(context: ChartParseContext): Promise<WordChartModel> {
    return parseAreaChart(context);
  },
};
