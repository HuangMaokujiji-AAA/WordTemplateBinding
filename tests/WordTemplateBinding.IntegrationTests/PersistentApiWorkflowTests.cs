using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 验证正式数值 ID API 的模板、章节和绑定集闭环。
/// </summary>
public sealed class PersistentApiWorkflowTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    /// <summary>
    /// 初始化持久化 API 测试客户端。
    /// </summary>
    public PersistentApiWorkflowTests(WebApplicationFactory<Program> factory)
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

        HttpResponseMessage download = await _client.GetAsync(
            $"/api/template-versions/{versionId}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
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
        string projectId = project.GetProperty("id").GetString()!;
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

    private async Task<JsonElement> PostJsonAsync(string path, object request)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
