using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 验证模板上传、绑定、报告生成和错误响应的完整 HTTP 闭环。
/// </summary>
public sealed class ApiWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly HttpClient _client;

    /// <summary>
    /// 初始化 API 集成测试。
    /// </summary>
    /// <param name="factory">ASP.NET Core 测试应用工厂。</param>
    public ApiWorkflowTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// 验证首页静态资源可由 Minimal API 应用提供。
    /// </summary>
    [Fact]
    public async Task GetRoot_ReturnsFrontendPage()
    {
        HttpResponseMessage response = await _client.GetAsync("/");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Word 模板可视化数据绑定", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证上传、Schema、绑定、生成和删除绑定的完整流程。
    /// </summary>
    [Fact]
    public async Task FullWorkflow_UploadBindGenerateAndDelete_Succeeds()
    {
        JsonDocument upload = await UploadTemplateAsync("本年度学生平均成绩为 88.5 分。");
        Guid templateId = upload.RootElement.GetProperty("templateId").GetGuid();
        string locatorId = upload.RootElement
            .GetProperty("mockItems")[0]
            .GetProperty("locatorId")
            .GetString()!;

        HttpResponseMessage schemaResponse = await _client.GetAsync(
            "/api/data-schema?query=AverageScore");
        Assert.Equal(HttpStatusCode.OK, schemaResponse.StatusCode);

        HttpResponseMessage bindingResponse = await _client.PostAsJsonAsync(
            "/api/bindings",
            new
            {
                templateId,
                locatorId,
                dataPath = "StudentStatistics.AverageScore",
            });
        Assert.Equal(HttpStatusCode.OK, bindingResponse.StatusCode);

        HttpResponseMessage bindingsResponse = await _client.GetAsync(
            $"/api/templates/{templateId}/bindings");
        using JsonDocument bindings = JsonDocument.Parse(
            await bindingsResponse.Content.ReadAsStringAsync());
        Assert.Single(bindings.RootElement.EnumerateArray());

        HttpResponseMessage reportResponse = await _client.PostAsJsonAsync(
            "/api/reports/generate",
            new
            {
                templateId,
                values = new Dictionary<string, decimal>
                {
                    ["StudentStatistics.AverageScore"] = 92.3m,
                },
            });
        byte[] reportBytes = await reportResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(DocxContentType, reportResponse.Content.Headers.ContentType?.MediaType);
        string reportText = TestDocumentFactory.ReadBodyText(reportBytes);
        Assert.Contains("92.3", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain("88.5", reportText, StringComparison.Ordinal);

        HttpResponseMessage deleteResponse = await _client.DeleteAsync(
            $"/api/templates/{templateId}/bindings/{Uri.EscapeDataString(locatorId)}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    /// <summary>
    /// 验证 API 默认配置和依赖注入会识别无空格小数、整数及显式文字标记。
    /// </summary>
    [Fact]
    public async Task Upload_MixedMockData_ReturnsTypedItems()
    {
        using JsonDocument upload = await UploadTemplateAsync(
            "标题[[text:年度报告]]，人数1200人，成绩88.5分");
        JsonElement[] items = upload.RootElement
            .GetProperty("mockItems")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(3, items.Length);
        Assert.Equal(
            new[] { "年度报告", "1200", "88.5" },
            items.Select(item => item.GetProperty("mockValue").GetString()));
        Assert.Equal(
            new[] { "String", "Integer", "Decimal" },
            items.Select(item => item.GetProperty("dataType").GetString()));
        Assert.Equal(
            "[[text:年度报告]]",
            items[0].GetProperty("locator").GetProperty("originalValue").GetString());
    }

    /// <summary>
    /// 验证非法文件上传返回 ProblemDetails 风格的 400 响应。
    /// </summary>
    [Fact]
    public async Task Upload_InvalidDocx_ReturnsProblemDetails()
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(new byte[] { 1, 2, 3, 4 });
        file.Headers.ContentType = new MediaTypeHeaderValue(DocxContentType);
        form.Add(file, "file", "broken.docx");

        HttpResponseMessage response = await _client.PostAsync("/api/templates/upload", form);
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "invalid_template_file",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    /// <summary>
    /// 验证不存在的模板返回 404 ProblemDetails。
    /// </summary>
    [Fact]
    public async Task GetTemplate_MissingTemplate_ReturnsNotFound()
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// 验证没有绑定时生成报告返回 409。
    /// </summary>
    [Fact]
    public async Task GenerateReport_WithoutBindings_ReturnsConflict()
    {
        JsonDocument upload = await UploadTemplateAsync("平均成绩 88.5");
        Guid templateId = upload.RootElement.GetProperty("templateId").GetGuid();

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/reports/generate",
            new { templateId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// 上传一个测试模板并返回 JSON 文档。
    /// </summary>
    /// <param name="text">测试模板正文文本。</param>
    /// <returns>返回上传接口 JSON 响应。</returns>
    private async Task<JsonDocument> UploadTemplateAsync(string text)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(TestDocumentFactory.Create(text));
        file.Headers.ContentType = new MediaTypeHeaderValue(DocxContentType);
        form.Add(file, "file", "template.docx");

        HttpResponseMessage response = await _client.PostAsync("/api/templates/upload", form);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
