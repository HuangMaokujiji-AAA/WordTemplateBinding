import type { ChartItem, DataFieldNode } from "../../api/types";
import { FIELD_MIME_TYPE } from "./renderedDocumentBindings";

export interface RenderedChartBindingCallbacks {
  onSelect: (chart: ChartItem) => void;
  onBind: (locatorId: string, field: DataFieldNode) => void;
  onError: (message: string) => void;
}

export interface RenderedChartBindingResult {
  renderedCount: number;
  unresolvedLocatorIds: string[];
}

/** Connects backend ChartPart targets to the ECharts containers in the preview. */
export function decorateRenderedCharts(
  container: HTMLElement,
  charts: ChartItem[],
  callbacks: RenderedChartBindingCallbacks
): RenderedChartBindingResult {
  const resolved = new Set<string>();

  for (const chart of charts) {
    const target = findChartTarget(container, chart.locator.partKey);
    if (!target) continue;

    resolved.add(chart.locatorId);
    target.classList.add("template-chart-target");
    target.classList.toggle("is-disabled", !chart.isBindable);
    target.dataset.chartLocatorId = chart.locatorId;
    target.tabIndex = 0;
    target.setAttribute("role", "button");
    target.setAttribute("aria-label", `${chart.title}，点击选择图表绑定`);
    target.addEventListener("click", () => callbacks.onSelect(chart), true);
    target.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        callbacks.onSelect(chart);
      }
    });
    target.addEventListener("dragover", (event) => {
      if (!event.dataTransfer?.types.includes(FIELD_MIME_TYPE)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "copy";
      target.classList.add("is-drag-over");
    });
    target.addEventListener("dragleave", () => {
      target.classList.remove("is-drag-over");
    });
    target.addEventListener("drop", (event) => {
      event.preventDefault();
      target.classList.remove("is-drag-over");
      const field = readField(event.dataTransfer);
      if (!field) {
        callbacks.onError("拖拽的数据字段无效。");
        return;
      }
      if (field.type !== "Array") {
        callbacks.onError("图表只能绑定集合字段。");
        return;
      }
      if (!chart.isBindable) {
        callbacks.onError("该图表没有可写的数据系列缓存。");
        return;
      }
      callbacks.onBind(chart.locatorId, field);
    });
  }

  refreshChartBindingTargetStates(container, charts);
  return {
    renderedCount: resolved.size,
    unresolvedLocatorIds: charts
      .filter((chart) => !resolved.has(chart.locatorId))
      .map((chart) => chart.locatorId),
  };
}

export function refreshChartBindingTargetStates(
  container: HTMLElement,
  charts: ChartItem[]
): void {
  const chartsById = new Map(charts.map((chart) => [chart.locatorId, chart]));
  for (const target of container.querySelectorAll<HTMLElement>(
    ".template-chart-target[data-chart-locator-id]"
  )) {
    const chart = chartsById.get(target.dataset.chartLocatorId ?? "");
    target.classList.toggle("is-bound", Boolean(chart?.isBound));
    target.title = chart?.isBound
      ? `图表已绑定：${chart.boundDataPath}`
      : chart?.isBindable
        ? `图表：${chart.title}（可拖入集合字段）`
        : `图表：${chart?.title ?? "未命名"}（没有可写数据缓存）`;
  }
}

export function focusChartTarget(container: HTMLElement, locatorId: string): boolean {
  const target = Array.from(
    container.querySelectorAll<HTMLElement>(
      ".template-chart-target[data-chart-locator-id]"
    )
  ).find((element) => element.dataset.chartLocatorId === locatorId);
  if (!target) return false;

  target.scrollIntoView({ behavior: "smooth", block: "center" });
  target.focus({ preventScroll: true });
  target.classList.add("is-focused");
  window.setTimeout(() => target.classList.remove("is-focused"), 1200);
  return true;
}

function findChartTarget(container: HTMLElement, partKey: string): HTMLElement | null {
  const normalizedPartKey = normalizePartKey(partKey);
  return Array.from(
    container.querySelectorAll<HTMLElement>(".docx-chart-slot[data-chart-part-key]")
  ).find((element) => normalizePartKey(element.dataset.chartPartKey ?? "") === normalizedPartKey)
    ?? null;
}

function normalizePartKey(path: string): string {
  const normalized = path.replace(/\\/g, "/");
  return normalized.startsWith("/") ? normalized : `/${normalized}`;
}

function readField(dataTransfer: DataTransfer | null): DataFieldNode | null {
  const raw = dataTransfer?.getData(FIELD_MIME_TYPE);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as DataFieldNode;
  } catch {
    return null;
  }
}
