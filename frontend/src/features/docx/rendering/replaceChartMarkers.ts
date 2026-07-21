import type { WordChartModel } from "../chart-recognition/types";
import type { LocatedChart } from "../ooxml/documentChartLocator";
import { groupChartWithCaption } from "./groupChartWithCaption";

/**
 * Walk the rendered DOM to find chart markers and replace them with
 * chart containers.
 *
 * Uses TreeWalker (NodeFilter.SHOW_TEXT) to find text nodes containing
 * markers like [[DOCX_CHART_SLOT:chart-1-rId7]].
 *
 * Each marker is replaced with:
 *  <span class="docx-chart-slot" data-chart-slot-id="...">
 *    <span class="docx-chart-slot__canvas"></span>
 *  </span>
 *
 * The canvas span is where ECharts (or the unsupported placeholder)
 * will be rendered.
 *
 * @param container - The root DOM element of the rendered document.
 * @param chartModels - Map of marker text → WordChartModel.
 * @returns Array of { slotId, canvasElement, model } for chart rendering.
 */
export interface ReplacedChartSlot {
  slotId: string;
  canvasElement: HTMLElement;
  model: WordChartModel;
}

export function replaceChartMarkers(
  container: HTMLElement,
  chartModels: Map<string, WordChartModel>,
  chartLocations: Map<string, LocatedChart> = new Map()
): ReplacedChartSlot[] {
  const replaced: ReplacedChartSlot[] = [];
  const processedMarkers = new Set<string>();
  const chartElements: Array<{
    element: HTMLElement;
    location?: LocatedChart;
  }> = [];

  const walker = document.createTreeWalker(
    container,
    NodeFilter.SHOW_TEXT,
    {
      acceptNode(_node: Text): number {
        // Accept if the node might contain a marker
        if (_node.textContent?.includes("[[DOCX_CHART_SLOT:")) {
          return NodeFilter.FILTER_ACCEPT;
        }
        return NodeFilter.FILTER_SKIP;
      },
    }
  );

  const markerRegex = /\[\[DOCX_CHART_SLOT:([^\]]+)\]\]/g;
  const nodesToProcess: Text[] = [];

  // Collect all matching text nodes first
  let node: Text | null;
  while ((node = walker.nextNode() as Text | null)) {
    nodesToProcess.push(node);
  }

  for (const textNode of nodesToProcess) {
    const text = textNode.textContent ?? "";
    markerRegex.lastIndex = 0;

    let match: RegExpExecArray | null;
    const fragments: Array<Node> = [];
    let lastIndex = 0;

    while ((match = markerRegex.exec(text)) !== null) {
      const slotId = match[1];
      const fullMatch = match[0];

      // Text before the marker
      if (match.index > lastIndex) {
        fragments.push(
          document.createTextNode(text.substring(lastIndex, match.index))
        );
      }

      // Create the chart slot element
      const model = chartModels.get(fullMatch);
      if (model && !processedMarkers.has(fullMatch)) {
        processedMarkers.add(fullMatch);

        const slotSpan = document.createElement("span");
        slotSpan.className = "docx-chart-slot";
        slotSpan.setAttribute("data-chart-slot-id", slotId);
        slotSpan.setAttribute("role", "img");
        slotSpan.setAttribute("aria-label", "Word 图表");
        const location = chartLocations.get(fullMatch);
        if (location) {
          slotSpan.dataset.chartPartKey = normalizePartKey(location.chartPath);
        }

        // Apply dimensions
        if (model.widthPx) {
          slotSpan.style.width = `${model.widthPx}px`;
          slotSpan.style.maxWidth = "100%";
        }
        if (model.widthPx && model.heightPx) {
          slotSpan.style.aspectRatio = `${model.widthPx} / ${model.heightPx}`;
        }

        const canvasSpan = document.createElement("span");
        canvasSpan.className = "docx-chart-slot__canvas";
        slotSpan.appendChild(canvasSpan);

        fragments.push(slotSpan);
        chartElements.push({
          element: slotSpan,
          location,
        });

        // Don't use div inside p — use span as the slot container
        replaced.push({
          slotId,
          canvasElement: canvasSpan,
          model,
        });
      } else if (!model) {
        // Marker not found in model map — leave as text
        console.warn(`Chart model not found for marker: ${fullMatch}`);
        fragments.push(document.createTextNode(fullMatch));
      } else {
        // Already processed
        fragments.push(document.createTextNode(fullMatch));
      }

      lastIndex = match.index + fullMatch.length;
    }

    // Text after the last marker
    if (lastIndex < text.length) {
      fragments.push(document.createTextNode(text.substring(lastIndex)));
    }

    // Replace the text node with fragments
    if (fragments.length > 0) {
      const parent = textNode.parentNode;
      if (parent) {
        for (const frag of fragments) {
          parent.insertBefore(frag, textNode);
        }
        parent.removeChild(textNode);
      }
    }
  }

  for (const chart of chartElements) {
    groupChartWithCaption(container, chart.element, chart.location?.caption);
  }

  return replaced;
}

function normalizePartKey(path: string): string {
  const normalized = path.replace(/\\/g, "/");
  return normalized.startsWith("/") ? normalized : `/${normalized}`;
}
