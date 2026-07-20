import type { WordChartModel } from "../types";

/**
 * Render a placeholder for unsupported chart types.
 *
 * Displays a centered message with:
 *  - "当前图表类型暂不支持网页渲染"
 *  - Chart type information
 *
 * The placeholder preserves the original chart's dimensions.
 */
export function renderUnsupportedChart(
  model: WordChartModel,
  container: HTMLElement
): void {
  container.classList.add("docx-chart-unsupported");

  const width = model.widthPx ?? 560;
  const height = model.heightPx ?? 320;

  container.style.display = "flex";
  container.style.flexDirection = "column";
  container.style.alignItems = "center";
  container.style.justifyContent = "center";
  container.style.width = `${width}px`;
  container.style.maxWidth = "100%";
  container.style.height = `${height}px`;
  container.style.border = "2px dashed #ccc";
  container.style.borderRadius = "6px";
  container.style.backgroundColor = "#fafafa";
  container.style.color = "#666";
  container.style.fontSize = "14px";
  container.style.fontFamily =
    '"Microsoft YaHei", "PingFang SC", sans-serif';

  const message = document.createElement("div");
  message.textContent = "当前图表类型暂不支持网页渲染";
  message.style.fontWeight = "bold";
  message.style.marginBottom = "8px";

  // Extract chart type from unsupportedReason
  const typeMatch = model.unsupportedReason?.match(/图表类型：(.+)/);
  if (typeMatch) {
    const typeInfo = document.createElement("div");
    typeInfo.textContent = `图表类型：${typeMatch[1]}`;
    typeInfo.style.fontSize = "12px";
    typeInfo.style.color = "#999";
    container.appendChild(message);
    container.appendChild(typeInfo);
  } else {
    container.appendChild(message);
  }
}
