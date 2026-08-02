import type { DataFieldNode, TableItem } from "../../api/types";
import { FIELD_MIME_TYPE } from "./renderedDocumentBindings";

export interface RenderedTableBindingCallbacks {
  onSelect: (table: TableItem) => void;
  onBind: (locatorId: string, field: DataFieldNode) => void;
  onError: (message: string) => void;
}

export interface RenderedTableBindingResult {
  renderedCount: number;
  unresolvedLocatorIds: string[];
}

export function decorateRenderedTables(
  container: HTMLElement,
  tables: TableItem[],
  callbacks: RenderedTableBindingCallbacks
): RenderedTableBindingResult {
  const renderedTables = [
    ...container.querySelectorAll<HTMLElement>(".docx article table"),
  ].filter((table) => !table.parentElement?.closest("table"));
  const ordered = [...tables].sort(
    (left, right) => left.segmentLocalOrder - right.segmentLocalOrder
  );
  const resolved = new Set<string>();
  const usedTargets = new Set<HTMLElement>();

  ordered.forEach((table) => {
    const expectedSignature = normalizeHeaderSignature(table.locator.headerSignature);
    const target = renderedTables.find((candidate) =>
      !usedTargets.has(candidate) &&
      renderedHeaderSignature(candidate) === expectedSignature
    ) || renderedTables.find((candidate) => !usedTargets.has(candidate));
    if (!target) return;
    usedTargets.add(target);
    resolved.add(table.locatorId);
    target.classList.add("template-table-target");
    target.classList.toggle("is-disabled", !table.isBindable);
    target.dataset.tableLocatorId = table.locatorId;
    target.tabIndex = 0;
    target.setAttribute("role", "button");
    target.setAttribute("aria-label", `${table.title}，点击选择表格绑定`);
    target.addEventListener("click", (event) => {
      const clicked = event.target as HTMLElement;
      if (clicked.closest(".template-binding-target, .template-chart-target")) return;
      callbacks.onSelect(table);
    });
    target.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        callbacks.onSelect(table);
      }
    });
    target.addEventListener("dragover", (event) => {
      if (!event.dataTransfer?.types.includes(FIELD_MIME_TYPE)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = table.isBound ? "link" : "copy";
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
      } else if (field.type !== "Array") {
        callbacks.onError("表格只能绑定集合字段。");
      } else if (!table.isBindable) {
        callbacks.onError(table.parseMessage || "该表格当前不可绑定。");
      } else {
        callbacks.onBind(table.locatorId, field);
      }
    });
  });

  refreshTableBindingTargetStates(container, tables);
  return {
    renderedCount: resolved.size,
    unresolvedLocatorIds: ordered
      .filter((table) => !resolved.has(table.locatorId))
      .map((table) => table.locatorId),
  };
}

export function refreshTableBindingTargetStates(
  container: HTMLElement,
  tables: TableItem[]
): void {
  const byId = new Map(tables.map((table) => [table.locatorId, table]));
  for (const target of container.querySelectorAll<HTMLElement>(
    ".template-table-target[data-table-locator-id]"
  )) {
    const table = byId.get(target.dataset.tableLocatorId || "");
    target.classList.toggle("is-bound", Boolean(table?.isBound));
    target.title = table?.isBound
      ? `表格已绑定：${table.boundDataPath}；拖入其他集合字段可改绑`
      : table?.isBindable
        ? `表格：${table.title}（可拖入集合字段）`
        : `表格：${table?.title || "未命名"}（不可绑定）`;
  }
}

export function focusTableTarget(container: HTMLElement, locatorId: string): boolean {
  const target = [...container.querySelectorAll<HTMLElement>(
    ".template-table-target[data-table-locator-id]"
  )].find((element) => element.dataset.tableLocatorId === locatorId);
  if (!target) return false;
  target.scrollIntoView({ behavior: "smooth", block: "center" });
  target.focus({ preventScroll: true });
  target.classList.add("is-focused");
  window.setTimeout(() => target.classList.remove("is-focused"), 1200);
  return true;
}

function renderedHeaderSignature(table: HTMLElement): string {
  const firstRow = table.querySelector("tr");
  if (!firstRow) return "";
  return [...firstRow.children]
    .filter((cell) => cell.tagName === "TD" || cell.tagName === "TH")
    .map((cell) => normalizeHeader(String(cell.textContent || "")))
    .join("|");
}

function normalizeHeaderSignature(value: string): string {
  return value.split("|").map(normalizeHeader).join("|");
}

function normalizeHeader(value: string): string {
  return value.replace(/\s+/g, "").replace(/[（）()]/g, "");
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
