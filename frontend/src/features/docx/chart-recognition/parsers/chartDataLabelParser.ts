import { OOXML_NS } from "../../ooxml/namespaces";

/**
 * Parse chart-level data labels from the chart XML.
 *
 * Chart-level labels apply to all series unless overridden at the series level.
 * Found at: <c:chart><c:plotArea><c:barChart><c:dLbls>
 */
export function extractChartDataLabels(
  barChartEl: Element
): { showValueLabel: boolean; dataLabelPosition?: string } | undefined {
  const dLbls = barChartEl.getElementsByTagNameNS(OOXML_NS.c, "dLbls")[0];
  if (!dLbls) return undefined;

  const showVal = dLbls.getElementsByTagNameNS(OOXML_NS.c, "showVal")[0];
  const showValAttr = showVal?.getAttribute("val");

  const dLblPos = dLbls.getElementsByTagNameNS(OOXML_NS.c, "dLblPos")[0];
  const dLblPosVal = dLblPos?.getAttribute("val");

  return {
    showValueLabel: showValAttr === "1",
    dataLabelPosition: dLblPosVal ?? undefined,
  };
}
