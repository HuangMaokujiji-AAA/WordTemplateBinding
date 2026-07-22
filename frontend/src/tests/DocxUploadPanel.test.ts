import { createApp, type App } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import DocxUploadPanel from "../components/DocxUploadPanel.vue";

let app: App<Element> | null = null;

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.replaceChildren();
});

describe("DocxUploadPanel output actions", () => {
  it("disables both output actions when no binding exists", () => {
    const container = mountPanel(0);

    expect(findButton(container, "导出复用模板").disabled).toBe(true);
    expect(findButton(container, "生成报告").disabled).toBe(true);
  });

  it("keeps export and report as independent enabled actions", async () => {
    const onExportReusable = vi.fn();
    const onGenerate = vi.fn();
    const container = mountPanel(2, { onExportReusable, onGenerate });
    const exportButton = findButton(container, "导出复用模板");
    const reportButton = findButton(container, "生成报告");

    expect(exportButton.disabled).toBe(false);
    expect(reportButton.disabled).toBe(false);
    exportButton.click();
    reportButton.click();
    await Promise.resolve();

    expect(onExportReusable).toHaveBeenCalledOnce();
    expect(onGenerate).toHaveBeenCalledOnce();
  });
});

function mountPanel(
  bindingCount: number,
  listeners: {
    onExportReusable?: () => void;
    onGenerate?: () => void;
  } = {}
): HTMLElement {
  const container = document.createElement("div");
  document.body.append(container);
  app = createApp(DocxUploadPanel, {
    fileName: "template.docx",
    fileSize: "1 KB",
    statusMessage: "就绪",
    loading: false,
    hasTemplate: true,
    bindingCount,
    ...listeners,
  });
  app.mount(container);
  return container;
}

function findButton(container: HTMLElement, label: string): HTMLButtonElement {
  const button = Array.from(container.querySelectorAll("button")).find(
    (candidate) => candidate.textContent?.trim() === label
  );
  if (!(button instanceof HTMLButtonElement)) {
    throw new Error(`找不到按钮：${label}`);
  }
  return button;
}
