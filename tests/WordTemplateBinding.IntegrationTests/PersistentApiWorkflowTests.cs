using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 验证正式数值 ID API 的模板、章节和绑定集闭环。
/// </summary>
public sealed class PersistentApiWorkflowTests
    : IClassFixture<IntegrationWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// 初始化持久化 API 测试客户端。
    /// </summary>
    public PersistentApiWorkflowTests(IntegrationWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// 验证所有数据库 ID 均以字符串返回，模板文件可从文件服务流式下载。
    /// </summary>
    [Fact]
    public async Task TemplateVersionApi_ReturnsStringIdsAndDownloadableFile()
    {
        byte[] bytes = TestDocumentFactory.Create("标题：{{text:ReportTitle}}");
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(file, "file", "persistent.docx");
        form.Add(new StringContent($"HTTP_{Guid.NewGuid():N}"), "templateCode");
        form.Add(new StringContent("持久化模板"), "templateName");

        HttpResponseMessage response = await _client.PostAsync("/api/templates", form);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string templateId = body.RootElement
            .GetProperty("template")
            .GetProperty("id")
            .GetString()!;
        string versionId = body.RootElement
            .GetProperty("version")
            .GetProperty("id")
            .GetString()!;
        string elementId = body.RootElement
            .GetProperty("elements")[0]
            .GetProperty("id")
            .GetString()!;
        Assert.True(ulong.TryParse(templateId, out _));
        Assert.True(ulong.TryParse(versionId, out _));
        Assert.True(ulong.TryParse(elementId, out _));

        JsonElement segmentList = await _client
            .GetFromJsonAsync<JsonElement>(
                $"/api/template-versions/{versionId}/segments");
        JsonElement segment = segmentList.GetProperty("items")[0];
        Assert.Equal("full-document", segment.GetProperty("segmentKey").GetString());
        string segmentId = segment.GetProperty("id").GetString()!;
        JsonElement segmentElements = await _client
            .GetFromJsonAsync<JsonElement>(
                $"/api/template-segments/{segmentId}/elements");
        Assert.Equal(elementId, segmentElements[0].GetProperty("id").GetString());
        HttpResponseMessage segmentPreview = await _client.GetAsync(
            $"/api/template-segments/{segmentId}/preview");
        Assert.Equal(HttpStatusCode.OK, segmentPreview.StatusCode);
        Assert.NotEmpty(await segmentPreview.Content.ReadAsByteArrayAsync());

        HttpResponseMessage download = await _client.GetAsync(
            $"/api/template-versions/{versionId}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        JsonElement studio = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-studio/{templateId}?versionId={versionId}");
        Assert.Equal(
            versionId,
            studio.GetProperty("versionView")
                .GetProperty("version")
                .GetProperty("id")
                .GetString());
        Assert.Equal(
            1,
            studio.GetProperty("summary")
                .GetProperty("segmentCount")
                .GetInt32());
    }

    /// <summary>
    /// 验证项目、章节和固定模板版本的草稿绑定集可通过正式 API 创建。
    /// </summary>
    [Fact]
    public async Task ProjectChapterBindingSetApi_CreatesDraftAgainstFixedVersion()
    {
        string suffix = Guid.NewGuid().ToString("N");
        JsonElement project = await PostJsonAsync(
            "/api/projects",
            new
            {
                projectCode = $"P_{suffix}",
                projectName = "接口项目",
            });
        string projectId = project.GetProperty("projectId").GetString()!;
        JsonElement chapter = await PostJsonAsync(
            $"/api/projects/{projectId}/chapters",
            new
            {
                chapterCode = "C_001",
                title = "第一章",
                sortKey = 1,
            });

        byte[] bytes = TestDocumentFactory.Create("无绑定元素正文");
        using MultipartFormDataContent form = new();
        form.Add(new ByteArrayContent(bytes), "file", "plain.docx");
        form.Add(new StringContent($"T_{suffix}"), "templateCode");
        form.Add(new StringContent("章节模板"), "templateName");
        HttpResponseMessage upload = await _client.PostAsync("/api/templates", form);
        upload.EnsureSuccessStatusCode();
        using JsonDocument template = JsonDocument.Parse(
            await upload.Content.ReadAsStringAsync());
        string versionId = template.RootElement
            .GetProperty("version")
            .GetProperty("id")
            .GetString()!;

        JsonElement bindingSet = await PostJsonAsync(
            "/api/binding-sets",
            new
            {
                chapterId = chapter.GetProperty("id").GetString(),
                templateVersionId = versionId,
            });
        Assert.Equal("DRAFT", bindingSet.GetProperty("bindingStatus").GetString());
        Assert.Equal(
            versionId,
            bindingSet.GetProperty("templateVersionId").GetString());
    }

    /// <summary>
    /// 验证网页边界编辑创建新版本，删除边界只解包且不改变正文。
    /// </summary>
    [Fact]
    public async Task SegmentBoundaryApi_CreatesImmutableVersionsAndPreservesContent()
    {
        byte[] bytes = TestDocumentFactory.CreateParagraphs("第一段", "第二段", "第三段");
        using MultipartFormDataContent form = new();
        form.Add(new ByteArrayContent(bytes), "file", "boundary.docx");
        form.Add(new StringContent($"BOUNDARY_{Guid.NewGuid():N}"), "templateCode");
        form.Add(new StringContent("边界模板"), "templateName");
        HttpResponseMessage upload = await _client.PostAsync("/api/templates", form);
        upload.EnsureSuccessStatusCode();
        using JsonDocument uploaded = JsonDocument.Parse(
            await upload.Content.ReadAsStringAsync());
        string originalVersionId = uploaded.RootElement
            .GetProperty("version").GetProperty("id").GetString()!;

        JsonElement outline = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{originalVersionId}/segment-outline");
        JsonElement blocks = outline.GetProperty("blocks");
        HttpResponseMessage insert = await _client.PostAsJsonAsync(
            $"/api/template-versions/{originalVersionId}/segment-boundaries",
            new
            {
                segmentKey = "first-two",
                segmentName = "前两段",
                startBlockId = blocks[0].GetProperty("blockId").GetString(),
                endBlockId = blocks[1].GetProperty("blockId").GetString(),
                expectedContentHash = outline.GetProperty("contentHash").GetString(),
            });
        Assert.Equal(HttpStatusCode.Created, insert.StatusCode);
        using JsonDocument inserted = JsonDocument.Parse(
            await insert.Content.ReadAsStringAsync());
        string insertedVersionId = inserted.RootElement
            .GetProperty("version").GetProperty("id").GetString()!;
        Assert.NotEqual(originalVersionId, insertedVersionId);

        JsonElement insertedSegments = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{insertedVersionId}/segments");
        Assert.Equal(
            "first-two",
            insertedSegments.GetProperty("items")[0]
                .GetProperty("segmentKey").GetString());

        JsonElement insertedOutline = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{insertedVersionId}/segment-outline");
        string insertedHash = insertedOutline.GetProperty("contentHash").GetString()!;
        HttpResponseMessage remove = await _client.DeleteAsync(
            $"/api/template-versions/{insertedVersionId}/segment-boundaries/first-two" +
            $"?expectedContentHash={insertedHash}");
        Assert.Equal(HttpStatusCode.Created, remove.StatusCode);
        using JsonDocument removed = JsonDocument.Parse(
            await remove.Content.ReadAsStringAsync());
        string removedVersionId = removed.RootElement
            .GetProperty("version").GetProperty("id").GetString()!;

        HttpResponseMessage download = await _client.GetAsync(
            $"/api/template-versions/{removedVersionId}/file");
        Assert.Equal(
            "第一段第二段第三段",
            TestDocumentFactory.ReadBodyText(
                await download.Content.ReadAsByteArrayAsync()));
        JsonElement originalSegments = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{originalVersionId}/segments");
        Assert.Equal(
            "full-document",
            originalSegments.GetProperty("items")[0]
                .GetProperty("segmentKey").GetString());
    }

    /// <summary>
    /// 验证多个不重叠边界只写入一个新模板版本。
    /// </summary>
    [Fact]
    public async Task SegmentBoundaryBatchApi_CreatesOneVersionForAllRanges()
    {
        byte[] bytes = TestDocumentFactory.CreateParagraphs(
            "第一段",
            "第二段",
            "第三段",
            "第四段");
        using MultipartFormDataContent form = new();
        form.Add(new ByteArrayContent(bytes), "file", "boundary-batch.docx");
        form.Add(
            new StringContent($"BOUNDARY_BATCH_{Guid.NewGuid():N}"),
            "templateCode");
        form.Add(new StringContent("批量边界模板"), "templateName");
        HttpResponseMessage upload = await _client.PostAsync(
            "/api/templates",
            form);
        upload.EnsureSuccessStatusCode();
        using JsonDocument uploaded = JsonDocument.Parse(
            await upload.Content.ReadAsStringAsync());
        string originalVersionId = uploaded.RootElement
            .GetProperty("version")
            .GetProperty("id")
            .GetString()!;
        JsonElement outline = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{originalVersionId}/segment-outline");
        JsonElement blocks = outline.GetProperty("blocks");

        HttpResponseMessage save = await _client.PostAsJsonAsync(
            $"/api/template-versions/{originalVersionId}/segment-boundaries/batch",
            new
            {
                expectedContentHash = outline.GetProperty("contentHash").GetString(),
                boundaries = new[]
                {
                    new
                    {
                        segmentKey = "first",
                        segmentName = "第一部分",
                        startBlockId = blocks[0].GetProperty("blockId").GetString(),
                        endBlockId = blocks[1].GetProperty("blockId").GetString(),
                    },
                    new
                    {
                        segmentKey = "second",
                        segmentName = "第二部分",
                        startBlockId = blocks[2].GetProperty("blockId").GetString(),
                        endBlockId = blocks[3].GetProperty("blockId").GetString(),
                    },
                },
            });

        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        using JsonDocument saved = JsonDocument.Parse(
            await save.Content.ReadAsStringAsync());
        string savedVersionId = saved.RootElement
            .GetProperty("version")
            .GetProperty("id")
            .GetString()!;
        Assert.NotEqual(originalVersionId, savedVersionId);
        JsonElement segments = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{savedVersionId}/segments");
        Assert.Equal(
            new[] { "first", "second" },
            segments.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("segmentKey").GetString()));
    }

    /// <summary>
    /// READY 解析版本不能在发布表落地前被误认为正式发布模板。
    /// </summary>
    [Fact]
    public async Task TemplateReleaseApi_DoesNotExposeReadyVersionsAsPublished()
    {
        JsonElement releases = await _client.GetFromJsonAsync<JsonElement>(
            "/api/template-releases");

        Assert.False(
            releases.GetProperty("publishingAvailable").GetBoolean());
        Assert.Empty(releases.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// 验证主文档图表按 relationshipId 归属片段，而不是误用 ChartPart 路径。
    /// </summary>
    [Fact]
    public async Task TemplateSegmentApi_IncludesMainDocumentCharts()
    {
        byte[] bytes = TestDocumentFactory.CreateChartDocument();
        using MultipartFormDataContent form = new();
        form.Add(new ByteArrayContent(bytes), "file", "segment-chart.docx");
        form.Add(new StringContent($"SEG_CHART_{Guid.NewGuid():N}"), "templateCode");
        form.Add(new StringContent("片段图表模板"), "templateName");

        HttpResponseMessage upload = await _client.PostAsync("/api/templates", form);
        upload.EnsureSuccessStatusCode();
        using JsonDocument uploaded = JsonDocument.Parse(
            await upload.Content.ReadAsStringAsync());
        string versionId = uploaded.RootElement
            .GetProperty("version").GetProperty("id").GetString()!;
        Assert.Equal(
            1,
            uploaded.RootElement.GetProperty("parseResult")
                .GetProperty("scanResult").GetProperty("charts").GetArrayLength());

        JsonElement segmentList = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-versions/{versionId}/segments");
        JsonElement segment = segmentList.GetProperty("items")[0];
        Assert.Equal(1, segment.GetProperty("elementCount").GetInt32());
        string segmentId = segment.GetProperty("id").GetString()!;

        JsonElement elements = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/template-segments/{segmentId}/elements");
        Assert.Equal("CHART", elements[0].GetProperty("elementType").GetString());
    }

    private async Task<JsonElement> PostJsonAsync(string path, object request)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
