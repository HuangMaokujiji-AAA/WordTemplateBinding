import { fileURLToPath } from "node:url";
import { expect, test, type Page, type Route } from "@playwright/test";

const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
const SAMPLE_DOCX_PATH = fileURLToPath(
  new URL("../public/samples/第一部分 科学监测结果.docx", import.meta.url),
);

const templateRecord = {
  id: "10",
  templateCode: "TPL_BASELINE",
  templateName: "基线监测报告",
  templateType: "SECTION",
  categoryCode: null,
  templateStatus: "ACTIVE",
  description: "阶段 0 浏览器冒烟测试模板",
  currentVersionNo: 6,
  createdAt: "2026-07-28T00:00:00Z",
  updatedAt: "2026-07-28T00:00:00Z",
};

const projectRecord = {
  projectId: "1",
  projectCode: "P_BASELINE",
  projectName: "基线项目",
  description: null,
  projectStatus: "CONFIGURING",
  createdAt: "2026-07-28T00:00:00Z",
  updatedAt: "2026-07-28T00:00:00Z",
  rowVersion: 0,
};

const chapterRecord = {
  id: "2",
  projectId: "1",
  parentId: null,
  chapterCode: "CH01",
  title: "监测报告",
  levelNo: 1,
  sortKey: 1,
  workflowStatus: "EDITING",
  isEnabled: true,
  createdAt: "2026-07-28T00:00:00Z",
  updatedAt: "2026-07-28T00:00:00Z",
  rowVersion: 0,
};

const dataSourceRecord = {
  id: "3",
  projectId: "1",
  connectionId: "1",
  sourceCode: "DS_BASELINE",
  sourceName: "基线数据源",
  sourceType: "JSON",
  sourceStatus: "ACTIVE",
  schemaName: "",
  objectType: "JSON",
  objectName: "baseline",
};

const segmentRecord = {
  id: "4",
  templateVersionId: "6",
  parentSegmentId: null,
  segmentKey: "__full__",
  segmentName: "完整模板",
  segmentType: "FULL_DOCUMENT",
  anchorType: "VIRTUAL_FULL",
  documentOrderStart: 0,
  documentOrderEnd: 1,
  segmentStatus: "READY",
  previewStatus: "READY",
  previewErrorMessage: null,
  sortNo: 0,
  elementCount: 1,
  bindingProgress: {
    total: 1,
    bound: 1,
    requiredMissing: 0,
  },
  rowVersion: 0,
};

const bindingSetRecord = {
  id: "5",
  chapterId: "2",
  versionNo: 1,
  templateVersionId: "6",
  bindingStatus: "DRAFT",
  validationStatus: "VALID",
  validationResult: null,
};

const bindingItemRecord = {
  id: "7",
  bindingSetId: "5",
  templateElementId: "999",
  targetProperty: "$",
  sourceKind: "DATA_SOURCE",
  dataSourceId: "3",
  sourcePath: "school.name",
  transformConfig: null,
  formatConfig: null,
  fallbackValue: null,
  isRequired: true,
};

const versionView = {
  template: templateRecord,
  version: {
    id: "6",
    templateId: "10",
    versionNo: 6,
    fileObjectId: "8",
    versionStatus: "READY",
    elementCount: 0,
    createdAt: "2026-07-28T00:00:00Z",
  },
  file: {
    id: "8",
    originalName: "基线监测报告.docx",
    mimeType: DOCX_CONTENT_TYPE,
    fileSize: 1024,
    sha256: "baseline-content-hash",
    objectStatus: "READY",
  },
  elements: [],
  parseResult: {
    scanResult: {
      contentHash: "baseline-content-hash",
      mockItems: [],
      charts: [],
      preview: { paragraphs: [] },
      warnings: [],
    },
    importSummary: {
      textBindingsRestored: 0,
      chartBindingsRestored: 0,
      unresolvedPlaceholders: [],
      warnings: [],
    },
    warnings: [],
  },
};

function fulfillJson(route: Route, body: unknown, status = 200) {
  return route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function installCurrentSystemApi(page: Page) {
  await page.route(/^https?:\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === "/api/templates" && method === "GET") {
      return fulfillJson(route, {
        items: [templateRecord],
        total: 1,
        page: 1,
        pageSize: Number(url.searchParams.get("pageSize") || 20),
      });
    }
    if (path === "/api/templates/10" && method === "GET") {
      return fulfillJson(route, templateRecord);
    }
    if (path === "/api/templates/10/versions" && method === "GET") {
      return fulfillJson(route, [versionView.version]);
    }
    if (path === "/api/template-versions/6" && method === "GET") {
      return fulfillJson(route, versionView);
    }
    if (path === "/api/template-studio/10" && method === "GET") {
      return fulfillJson(route, {
        versionView,
        segments: [segmentRecord],
        outline: {
          templateVersionId: "6",
          contentHash: "baseline-content-hash",
          blocks: [
            {
              blockId: "body/0",
              blockType: "PARAGRAPH",
              displayText: "基线报告标题",
              segmentKey: null,
              canSelect: true,
              depth: 0,
              children: [],
            },
          ],
        },
        summary: {
          segmentCount: 1,
          elementCount: 0,
          validElementCount: 0,
          warningElementCount: 0,
          unsupportedElementCount: 0,
          chartCount: 0,
          boundElementCount: 0,
          requiredMissingCount: 0,
        },
      });
    }
    if (path === "/api/projects" && method === "GET") {
      return fulfillJson(route, {
        items: [projectRecord],
        total: 1,
        page: 1,
        pageSize: Number(url.searchParams.get("pageSize") || 20),
      });
    }
    if (path === "/api/projects/1/chapters" && method === "GET") {
      return fulfillJson(route, [chapterRecord]);
    }
    if (path === "/api/data-sources" && method === "GET") {
      return fulfillJson(route, [dataSourceRecord]);
    }
    if (path === "/api/data-sources/3/fields" && method === "GET") {
      return fulfillJson(route, [
        {
          id: "11",
          snapshotId: "12",
          fieldPath: "school.name",
          fieldName: "name",
          comment: "学校名称",
          dataType: "String",
          isArray: false,
          isNullable: false,
          isBindable: true,
          sampleValue: "示例学校",
          displayOrder: 1,
        },
      ]);
    }
    if (path === "/api/templates/10/current" && method === "GET") {
      return fulfillJson(route, versionView);
    }
    if (path === "/api/binding-sets" && method === "POST") {
      return fulfillJson(route, bindingSetRecord);
    }
    if (path === "/api/template-versions/6/segments" && method === "GET") {
      return fulfillJson(route, { items: [segmentRecord] });
    }
    if (path === "/api/template-segments/4/elements" && method === "GET") {
      return fulfillJson(route, []);
    }
    if (path === "/api/template-segments/4/preview" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: DOCX_CONTENT_TYPE,
        path: SAMPLE_DOCX_PATH,
      });
    }
    if (path === "/api/binding-sets/5/items" && method === "GET") {
      return fulfillJson(route, [bindingItemRecord]);
    }
    if (path === "/api/template-versions/6/segment-outline" && method === "GET") {
      return fulfillJson(route, {
        templateVersionId: "6",
        contentHash: "baseline-content-hash",
        blocks: [
          {
            blockId: "body/0",
            blockType: "PARAGRAPH",
            displayText: "基线报告标题",
            segmentKey: null,
            canSelect: true,
            depth: 0,
            children: [],
          },
        ],
      });
    }
    if (path === "/api/binding-sets/5/validate" && method === "POST") {
      return fulfillJson(route, {
        status: "VALID",
        summary: { errors: 0, warnings: 0 },
        items: [],
      });
    }
    if (path === "/api/binding-sets/5/reports" && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: DOCX_CONTENT_TYPE,
        headers: {
          "Content-Disposition":
            "attachment; filename*=UTF-8''baseline-report.docx",
        },
        path: SAMPLE_DOCX_PATH,
      });
    }

    return fulfillJson(route, {
      title: "E2E mock route not configured",
      detail: `${method} ${path}`,
    }, 501);
  });
}

function contextSelect(page: Page, label: string) {
  return page
    .locator(".workspace-context label")
    .filter({ hasText: label })
    .locator("select");
}

async function openLoadedWorkspace(page: Page) {
  await page.goto("/#/workspace");
  await expect(page.getByText("Word 模板可视化数据绑定")).toBeVisible();
  await expect(contextSelect(page, "项目")).toHaveValue("1");
  await expect(contextSelect(page, "章节")).toHaveValue("2");
  await expect(contextSelect(page, "数据源")).toHaveValue("3");
  await contextSelect(page, "模板").selectOption("10");
  await expect(page.getByRole("treeitem", { name: /完整模板/ })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByText(/已加载片段：完整模板/).first()).toBeVisible();
}

test.beforeEach(async ({ page }) => {
  await installCurrentSystemApi(page);
});

test("首页通过两大中心引导核心任务，并保留旧模板入口", async ({ page }) => {
  await page.goto("/#/");
  await expect(page.getByRole("heading", { name: "今天要完成哪项任务？" })).toBeVisible();
  await expect(page.getByRole("link", { name: "模板制作中心", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "报告生成中心", exact: true })).toBeVisible();

  await page.getByRole("link", { name: "进入模板制作中心" }).click();
  await expect(page).toHaveURL(/#\/template-center\/templates/);
  await expect(page.getByRole("heading", { name: "模板管理" })).toBeVisible();

  await page.goto("/#/templates");
  await expect(page.getByRole("heading", { name: "模板管理" })).toBeVisible();
});

test("制作模板当前旅程：从模板库确认结构、数据源并进入绑定", async ({ page }) => {
  await page.goto("/#/templates");
  await expect(page.getByRole("heading", { name: "模板管理" })).toBeVisible();
  const templateRow = page.getByRole("row", { name: /TPL_BASELINE/ });
  await expect(templateRow).toContainText("基线监测报告");

  await page.getByRole("button", { name: "上传模板" }).click();
  await expect(page.getByRole("heading", { name: "上传模板" })).toBeVisible();
  await page.locator(".dialog-close").click();

  await templateRow.getByRole("button", { name: "进入绑定" }).click();
  await expect(page).toHaveURL(/#\/template-center\/studio/);
  await expect(
    page.getByRole("heading", { name: "确认报告结构" }).first(),
  ).toBeVisible();
  await expect(page.getByText("完整模板").first()).toBeVisible();

  await page.getByRole("button", { name: /5 数据/ }).click();
  await expect(
    page.getByRole("heading", { name: "连接数据源" }).first(),
  ).toBeVisible();
  await expect(page.locator(".studio-form-grid select").nth(0)).toHaveValue("1");
  await expect(page.locator(".studio-form-grid select").nth(1)).toHaveValue("2");
  await expect(page.locator(".studio-form-grid select").nth(2)).toHaveValue("3");
  await page.getByRole("button", { name: "数据上下文已确认，开始绑定" }).click();

  await expect(
    page.getByRole("heading", { name: "拖拽绑定字段" }).first(),
  ).toBeVisible();
  await expect(contextSelect(page, "项目")).toHaveValue("1");
  await expect(contextSelect(page, "章节")).toHaveValue("2");
  await expect(contextSelect(page, "数据源")).toHaveValue("3");
  await expect(contextSelect(page, "模板")).toHaveValue("10");
  await expect(page.getByRole("treeitem", { name: /完整模板/ })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.getByText(/已加载片段：完整模板/).first()).toBeVisible();
});

test("模板详情可将版本摘要展开为完整版本视图", async ({ page }) => {
  await page.goto("/#/templates");
  const templateRow = page.getByRole("row", { name: /TPL_BASELINE/ });
  await templateRow.getByRole("button", { name: "查看" }).click();

  await expect(page).toHaveURL(/#\/template-center\/templates\/10/);
  await expect(page.getByRole("heading", { name: "模板详情" })).toBeVisible();
  await expect(page.getByRole("row", { name: /基线监测报告\.docx/ })).toContainText(
    "v6",
  );
});

test("生成报告当前旅程：校验绑定并下载单份报告", async ({ page }) => {
  await openLoadedWorkspace(page);

  const validateButton = page.getByRole("button", { name: "校验绑定" });
  await expect(validateButton).toBeEnabled();
  await validateButton.click();
  await expect(page.getByText(/校验状态：VALID/).first()).toBeVisible();

  const generateButton = page.getByRole("button", { name: "生成报告" });
  await expect(generateButton).toBeEnabled();
  const downloadPromise = page.waitForEvent("download");
  await generateButton.click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe("baseline-report.docx");
  await expect(page.getByText(/报告已生成/).first()).toBeVisible();
});
