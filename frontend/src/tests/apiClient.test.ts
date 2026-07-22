import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  downloadReport,
  downloadReusableTemplate,
} from "../api/client";

const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

describe("DOCX download API client", () => {
  const createObjectURL = vi.fn(() => "blob:test-download");
  const revokeObjectURL = vi.fn();
  const anchorClick = vi
    .spyOn(HTMLAnchorElement.prototype, "click")
    .mockImplementation(() => undefined);

  beforeEach(() => {
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
    anchorClick.mockClear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("URL", {
      ...URL,
      createObjectURL,
      revokeObjectURL,
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts to export-reusable, parses UTF-8 filename and revokes Blob URL", async () => {
    const response = new Response(new Blob(["docx"]), {
      status: 200,
      headers: {
        "Content-Type": DOCX_CONTENT_TYPE,
        "Content-Disposition":
          "attachment; filename=report-template.docx; filename*=UTF-8''%E6%88%90%E7%BB%A9-template.docx",
      },
    });
    vi.mocked(fetch).mockResolvedValue(response);

    const fileName = await downloadReusableTemplate("template/id");

    expect(fileName).toBe("成绩-template.docx");
    expect(fetch).toHaveBeenCalledWith(
      "/api/templates/template%2Fid/export-reusable",
      { method: "POST" }
    );
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(anchorClick).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:test-download");
  });

  it("keeps report generation on its original endpoint", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(new Blob(["docx"]), {
        status: 200,
        headers: { "Content-Type": DOCX_CONTENT_TYPE },
      })
    );

    const fileName = await downloadReport("report-id");

    expect(fileName).toBe("report_generated.docx");
    expect(fetch).toHaveBeenCalledWith(
      "/api/reports/generate",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ templateId: "report-id" }),
      })
    );
  });

  it("surfaces ProblemDetails detail and does not create a download", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({ detail: "当前模板尚未建立任何数据绑定，无法导出复用模板。" }),
        {
          status: 409,
          headers: { "Content-Type": "application/problem+json" },
        }
      )
    );

    await expect(downloadReusableTemplate("empty-id")).rejects.toThrow(
      "当前模板尚未建立任何数据绑定，无法导出复用模板。"
    );
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it("rejects a successful response whose content type is not DOCX", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response("not a docx", {
        status: 200,
        headers: { "Content-Type": "text/plain" },
      })
    );

    await expect(downloadReusableTemplate("bad-type")).rejects.toThrow(
      "服务器没有返回有效的 DOCX 文件。"
    );
  });
});
