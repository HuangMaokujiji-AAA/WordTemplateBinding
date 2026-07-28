import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  downloadReport,
  downloadReusableTemplate,
  getTemplateSegmentPreview,
  insertTemplateSegmentBoundary,
  listTemplateVersions,
  saveTemplateSegmentBoundaries,
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

  it("loads the selected segment preview instead of the full template file", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(new Blob(["segment-docx"]), {
        status: 200,
        headers: { "Content-Type": DOCX_CONTENT_TYPE },
      })
    );

    const file = await getTemplateSegmentPreview(
      "segment/id",
      "major-monitoring-preview.docx"
    );

    expect(fetch).toHaveBeenCalledWith(
      "/api/template-segments/segment%2Fid/preview"
    );
    expect(file.name).toBe("major-monitoring-preview.docx");
  });

  it("posts a block range and content hash when inserting a segment boundary", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ version: { id: "new-version" } }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      })
    );

    await insertTemplateSegmentBoundary("version/id", {
      segmentKey: "major-monitoring",
      segmentName: "专业监测结果",
      startBlockId: "body/2",
      endBlockId: "body/5",
      expectedContentHash: "abc123",
    });

    expect(fetch).toHaveBeenCalledWith(
      "/api/template-versions/version%2Fid/segment-boundaries",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          segmentKey: "major-monitoring",
          segmentName: "专业监测结果",
          startBlockId: "body/2",
          endBlockId: "body/5",
          expectedContentHash: "abc123",
        }),
      })
    );
  });

  it("saves multiple segment boundaries in one immutable version request", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ version: { id: "new-version" } }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      })
    );

    const boundaries = [
      {
        segmentKey: "overview",
        segmentName: "概况",
        startBlockId: "body/0",
        endBlockId: "body/2",
      },
      {
        segmentKey: "results",
        segmentName: "检测结果",
        startBlockId: "body/3",
        endBlockId: "body/6",
      },
    ];
    await saveTemplateSegmentBoundaries("version/id", {
      expectedContentHash: "abc123",
      boundaries,
    });

    expect(fetch).toHaveBeenCalledWith(
      "/api/template-versions/version%2Fid/segment-boundaries/batch",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          expectedContentHash: "abc123",
          boundaries,
        }),
      })
    );
  });

  it("expands version summaries before returning template detail rows", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify([
            {
              id: "12",
              templateId: "3",
              versionNo: 2,
              fileObjectId: "20",
              versionStatus: "READY",
              elementCount: 4,
              createdAt: "2026-07-28T00:00:00Z",
            },
          ]),
          {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            template: { id: "3" },
            version: { id: "12", versionNo: 2 },
            file: { id: "20", originalName: "report.docx", fileSize: 128 },
            elements: [],
            parseResult: {},
          }),
          {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }
        )
      );

    const versions = await listTemplateVersions("3");

    expect(fetch).toHaveBeenNthCalledWith(1, "/api/templates/3/versions", undefined);
    expect(fetch).toHaveBeenNthCalledWith(2, "/api/template-versions/12", undefined);
    expect(versions[0]?.file.originalName).toBe("report.docx");
    expect(versions[0]?.version.id).toBe("12");
  });
});
