using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 验证模板上传、绑定、报告生成和错误响应的完整 HTTP 闭环。
/// </summary>
public sealed class ApiWorkflowTests
    : IClassFixture<IntegrationWebApplicationFactory>
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly HttpClient _client;

    /// <summary>
    /// 初始化 API 集成测试。
    /// </summary>
    /// <param name="factory">隔离外部配置的 ASP.NET Core 测试应用工厂。</param>
    public ApiWorkflowTests(IntegrationWebApplicationFactory factory)
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
    /// 验证数据库凭据仍为空时返回明确且不含敏感信息的诊断结果。
    /// </summary>
    [Fact]
    public async Task DatabaseHealth_WithoutCredentials_ReturnsNotConfigured()
    {
        HttpResponseMessage response =
            await _client.GetAsync("/api/system/database/health");
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("not_configured", root.GetProperty("status").GetString());
        Assert.Equal("report_platform", root.GetProperty("database").GetString());
        Assert.Equal(
            new[]
            {
                "Database:Host",
                "Database:Username",
                "Database:Password",
            },
            root.GetProperty("missingSettings")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.DoesNotContain(
            "Password=",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
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
            "标题{{text:年度报告}}，人数1200人，成绩88.5分");
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
            "{{text:年度报告}}",
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
    /// 验证文本绑定可导出、重新上传自动恢复并直接生成最终报告。
    /// </summary>
    [Fact]
    public async Task ReusableTemplate_TextRoundTrip_RestoresAndGeneratesReport()
    {
        using JsonDocument firstUpload = await UploadTemplateAsync(
            TestDocumentFactory.Create("平均成绩为 88.5 分。"),
            "成绩报告.docx");
        Guid firstTemplateId = firstUpload.RootElement.GetProperty("templateId").GetGuid();
        string firstLocatorId = firstUpload.RootElement
            .GetProperty("mockItems")[0]
            .GetProperty("locatorId")
            .GetString()!;
        HttpResponseMessage bindingResponse = await _client.PostAsJsonAsync(
            "/api/bindings",
            new
            {
                templateId = firstTemplateId,
                locatorId = firstLocatorId,
                dataPath = "StudentStatistics.AverageScore",
            });
        bindingResponse.EnsureSuccessStatusCode();

        HttpResponseMessage exportResponse = await _client.PostAsync(
            $"/api/templates/{firstTemplateId}/export-reusable",
            null);
        byte[] reusableBytes = await exportResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal(DocxContentType, exportResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "成绩报告-template.docx",
            exportResponse.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Contains(
            "{{StudentStatistics.AverageScore}}",
            TestDocumentFactory.ReadBodyText(reusableBytes),
            StringComparison.Ordinal);

        using JsonDocument secondUpload = await UploadTemplateAsync(
            reusableBytes,
            "成绩报告-template.docx");
        JsonElement secondRoot = secondUpload.RootElement;
        Guid secondTemplateId = secondRoot.GetProperty("templateId").GetGuid();
        JsonElement restoredItem = secondRoot.GetProperty("mockItems")[0];
        JsonElement importSummary = secondRoot.GetProperty("importSummary");

        Assert.NotEqual(firstLocatorId, restoredItem.GetProperty("locatorId").GetString());
        Assert.True(restoredItem.GetProperty("isBound").GetBoolean());
        Assert.Equal(
            "StudentStatistics.AverageScore",
            restoredItem.GetProperty("boundDataPath").GetString());
        Assert.Equal(1, importSummary.GetProperty("textBindingsRestored").GetInt32());

        HttpResponseMessage reportResponse = await _client.PostAsJsonAsync(
            "/api/reports/generate",
            new
            {
                templateId = secondTemplateId,
                values = new Dictionary<string, decimal>
                {
                    ["StudentStatistics.AverageScore"] = 92.3m,
                },
            });
        byte[] reportBytes = await reportResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Contains("92.3", TestDocumentFactory.ReadBodyText(reportBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("{{", TestDocumentFactory.ReadBodyText(reportBytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证图表绑定通过 Manifest 往返，图表本体导出时不被修改。
    /// </summary>
    [Fact]
    public async Task ReusableTemplate_ChartRoundTrip_RestoresAndGeneratesReport()
    {
        byte[] source = TestDocumentFactory.CreateChartDocument();
        string sourceChartXml = TestDocumentFactory.ReadFirstChartXml(source);
        using JsonDocument firstUpload = await UploadTemplateAsync(source, "图表报告.docx");
        Guid firstTemplateId = firstUpload.RootElement.GetProperty("templateId").GetGuid();
        string firstChartLocator = firstUpload.RootElement
            .GetProperty("charts")[0]
            .GetProperty("locatorId")
            .GetString()!;
        HttpResponseMessage bindingResponse = await _client.PostAsJsonAsync(
            "/api/bindings",
            new
            {
                templateId = firstTemplateId,
                locatorId = firstChartLocator,
                dataPath = "ChartData.ScienceScores",
            });
        bindingResponse.EnsureSuccessStatusCode();

        HttpResponseMessage exportResponse = await _client.PostAsync(
            $"/api/templates/{firstTemplateId}/export-reusable",
            null);
        byte[] reusableBytes = await exportResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal(sourceChartXml, TestDocumentFactory.ReadFirstChartXml(reusableBytes));
        Assert.Contains(
            "ChartData.ScienceScores",
            TestDocumentFactory.ReadBindingManifest(reusableBytes),
            StringComparison.Ordinal);

        using JsonDocument secondUpload = await UploadTemplateAsync(
            reusableBytes,
            "图表报告-template.docx");
        Guid secondTemplateId = secondUpload.RootElement.GetProperty("templateId").GetGuid();
        JsonElement restoredChart = secondUpload.RootElement.GetProperty("charts")[0];

        Assert.True(restoredChart.GetProperty("isBound").GetBoolean());
        Assert.Equal(
            "ChartData.ScienceScores",
            restoredChart.GetProperty("boundDataPath").GetString());
        Assert.Equal(
            1,
            secondUpload.RootElement.GetProperty("importSummary")
                .GetProperty("chartBindingsRestored")
                .GetInt32());

        HttpResponseMessage reportResponse = await _client.PostAsJsonAsync(
            "/api/reports/generate",
            new { templateId = secondTemplateId });
        byte[] reportBytes = await reportResponse.Content.ReadAsByteArrayAsync();
        IReadOnlyList<IReadOnlyList<decimal>> values =
            TestDocumentFactory.ReadChartValues(reportBytes);

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(new[] { 552m, 518m }, values[0]);
        Assert.Equal(new[] { 506m, 493m }, values[1]);
    }

    /// <summary>
    /// 验证没有绑定时复用模板导出返回统一 ProblemDetails。
    /// </summary>
    [Fact]
    public async Task ExportReusable_WithoutBindings_ReturnsConflictProblemDetails()
    {
        using JsonDocument upload = await UploadTemplateAsync("平均成绩 88.5");
        Guid templateId = upload.RootElement.GetProperty("templateId").GetGuid();

        HttpResponseMessage response = await _client.PostAsync(
            $"/api/templates/{templateId}/export-reusable",
            null);
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "empty_reusable_template_bindings",
            problem.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// 上传一个测试模板并返回 JSON 文档。
    /// </summary>
    /// <param name="text">测试模板正文文本。</param>
    /// <returns>返回上传接口 JSON 响应。</returns>
    private async Task<JsonDocument> UploadTemplateAsync(string text)
    {
        return await UploadTemplateAsync(TestDocumentFactory.Create(text), "template.docx");
    }

    /// <summary>
    /// 上传指定 DOCX 字节并返回 JSON 文档。
    /// </summary>
    private async Task<JsonDocument> UploadTemplateAsync(byte[] bytes, string fileName)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(DocxContentType);
        form.Add(file, "file", fileName);

        HttpResponseMessage response = await _client.PostAsync("/api/templates/upload", form);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
