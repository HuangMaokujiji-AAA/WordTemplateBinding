using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml;
using WordTemplateBinding.Infrastructure.OpenXml.Repeats;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供新功能的浏览器可测试演示端点。
/// </summary>
public static class DemoEndpoints
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// 映射演示端点。
    /// </summary>
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/demo/repeat-row-test", TestRepeatRowAsync);
        endpoints.MapPost("/api/demo/repeat-block-test", TestRepeatBlockAsync);
        endpoints.MapGet("/api/demo/test-page", ServeTestPageAsync);
        return endpoints;
    }

    /// <summary>
    /// 演示重复表格行功能。
    /// 程序化创建带有 SdtRow 的 DOCX，接受 JSON 数据，展开重复行，返回生成的 DOCX。
    /// </summary>
    private static async Task<IResult> TestRepeatRowAsync(
        HttpRequest request,
        IDataContextResolver resolver,
        OpenXmlRepeatRowExpander expander,
        OpenXmlRuntimeLocatorBuilder locatorBuilder,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("DemoRepeatRow");

        // 读取请求中的 JSON 数据
        string requestBody;
        using (StreamReader reader = new(request.Body))
        {
            requestBody = await reader.ReadToEndAsync(cancellationToken);
        }

        JsonDocument? requestJson = string.IsNullOrWhiteSpace(requestBody)
            ? null
            : JsonDocument.Parse(requestBody);

        // 使用请求数据或默认演示数据
        JsonElement data = requestJson?.RootElement ?? CreateDefaultDemoData();

        // 构建根作用域
        JsonElement school = data.TryGetProperty("school", out JsonElement s) ? s.Clone() : default;
        JsonElement majors = data.TryGetProperty("majors", out JsonElement m) ? m.Clone() : default;
        int year = data.TryGetProperty("year", out JsonElement y) && y.ValueKind == JsonValueKind.Number
            ? y.GetInt32() : 2026;

        Dictionary<string, object?> variables = new(StringComparer.Ordinal)
        {
            ["school"] = school.ValueKind != JsonValueKind.Undefined ? school : null,
            ["majors"] = majors.ValueKind != JsonValueKind.Undefined ? majors : null,
            ["year"] = year,
        };
        RenderScope scope = RenderScope.CreateRoot(variables);

        // 程序化创建带有 SdtRow 模板的 DOCX
        byte[] templateBytes = CreateTemplateWithSdtRow(logger);

        using MemoryStream outputStream = new(templateBytes.Length + 65536);
        outputStream.Write(templateBytes, 0, templateBytes.Length);
        outputStream.Position = 0;

        RepeatExpansionResult? result = null;
        List<string> diagnostics = new();

        using (WordprocessingDocument document = WordprocessingDocument.Open(outputStream, true))
        {
            MainDocumentPart mainPart = document.MainDocumentPart
                ?? throw new InvalidOperationException("文档缺少主文档部件。");

            locatorBuilder.Reset();

            // 定义重复块配置
            RepeatBlockDefinition definition = new()
            {
                BlockKey = "major-summary-row",
                BlockType = RepeatBlockType.REPEAT_ROW,
                SourcePath = "majors",
                ItemAlias = "major",
                ItemKeyPath = "majorId",
                EmptyBehavior = EmptyBehavior.REMOVE_PROTOTYPE,
            };

            try
            {
                result = await expander.ExpandAsync(
                    document, mainPart, definition, scope, cancellationToken);
                diagnostics.Add($"展开成功：{result.InstanceCount} 行");
                diagnostics.Add($"运行时元素：{result.RuntimeElements.Count} 个");
                foreach (RuntimeTemplateElement element in result.RuntimeElements.Take(10))
                {
                    diagnostics.Add($"  - {element.RuntimeLocatorId}");
                }

                if (result.RuntimeElements.Count > 10)
                {
                    diagnostics.Add($"  ... 还有 {result.RuntimeElements.Count - 10} 个");
                }

                // 执行简单的文字替换（将占位符替换为实际数据）
                ReplacePlaceholders(mainPart, resolver, scope, definition, result, logger);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"错误：{ex.Message}");
                logger.LogError(ex, "Repeat row 展开失败");
            }
        }

        // 在 DOCX 末尾追加诊断信息段落
        byte[] finalBytes = AppendDiagnosticsParagraph(outputStream.ToArray(), diagnostics);

        string fileName = $"repeat_row_demo_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.docx";
        return Results.File(finalBytes, DocxContentType, fileName);
    }

    /// <summary>
    /// 演示重复块功能。
    /// </summary>
    private static async Task<IResult> TestRepeatBlockAsync(
        HttpRequest request,
        IDataContextResolver resolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        List<string> diagnostics = new()
        {
            "REPEAT_BLOCK 演示端点已就绪。",
            "此端点需要 OpenXmlRepeatBlockExpander（已注册）。",
            "发送 POST 请求并附带 JSON 数据以测试。",
        };

        // 读取请求数据
        string requestBody;
        using (StreamReader reader = new(request.Body))
        {
            requestBody = await reader.ReadToEndAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(requestBody))
        {
            diagnostics.Add($"收到数据：{requestBody[..Math.Min(requestBody.Length, 200)]}");
        }

        // 返回一个简单的 DOCX 显示诊断信息
        byte[] docxBytes = CreateDiagnosticsDocx(diagnostics);
        string fileName = $"repeat_block_demo_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.docx";
        return Results.File(docxBytes, DocxContentType, fileName);
    }

    /// <summary>
    /// 提供一个简单的 HTML 测试页面。
    /// </summary>
    private static Task<IResult> ServeTestPageAsync()
    {
        string html = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="UTF-8">
        <title>Repeat Row / Block 演示</title>
        <style>
        body { font-family: system-ui, sans-serif; max-width: 900px; margin: 0 auto; padding: 20px; background: #f5f5f5; }
        h1 { color: #2563eb; }
        .card { background: white; border-radius: 8px; padding: 20px; margin: 16px 0; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        textarea { width: 100%; height: 300px; font-family: monospace; font-size: 13px; border: 1px solid #d1d5db; border-radius: 4px; padding: 12px; }
        button { background: #2563eb; color: white; border: none; padding: 10px 24px; border-radius: 6px; font-size: 14px; cursor: pointer; margin-right: 8px; margin-bottom: 8px; }
        button:hover { background: #1d4ed8; }
        button:disabled { background: #9ca3af; cursor: not-allowed; }
        .status { padding: 8px 12px; border-radius: 4px; margin: 8px 0; }
        .status.success { background: #d1fae5; color: #065f46; }
        .status.error { background: #fee2e2; color: #991b1b; }
        .status.info { background: #dbeafe; color: #1e40af; }
        pre { background: #1f2937; color: #e5e7eb; padding: 12px; border-radius: 4px; overflow-x: auto; font-size: 12px; }
        .tabs { display: flex; border-bottom: 2px solid #e5e7eb; margin-bottom: 16px; }
        .tab { padding: 8px 16px; cursor: pointer; border: none; background: none; color: #6b7280; font-size: 14px; }
        .tab.active { color: #2563eb; border-bottom: 2px solid #2563eb; margin-bottom: -2px; font-weight: 600; }
        </style>
        </head>
        <body>
        <h1>🔁 WordTemplateBinding — Repeat Row / Block 演示</h1>

        <div class="card">
        <h3>📋 默认测试数据（可直接使用）</h3>
        <textarea id="jsonData">{
          "school": {
            "schoolId": "1001",
            "schoolName": "示例大学",
            "year": 2026
          },
          "majors": [
            {
              "majorId": "080901",
              "majorName": "计算机科学与技术",
              "warningLevel": "正常",
              "employmentRate": 95.2
            },
            {
              "majorId": "080902",
              "majorName": "软件工程",
              "warningLevel": "正常",
              "employmentRate": 93.1
            },
            {
              "majorId": "080903",
              "majorName": "数据科学与大数据技术",
              "warningLevel": "注意",
              "employmentRate": 88.5
            }
          ],
          "year": 2026
        }</textarea>
        </div>

        <div class="card">
        <h3>🚀 测试操作</h3>
        <button id="btnRepeatRow" onclick="testRepeatRow()">测试 Repeat Row（表格行重复）</button>
        <button id="btnRepeatBlock" onclick="testRepeatBlock()">测试 Repeat Block（内容块重复）</button>
        <button id="btnHealth" onclick="checkHealth()">健康检查</button>
        <div id="status"></div>
        </div>

        <div class="card" id="resultCard" style="display:none;">
        <h3>📄 结果</h3>
        <pre id="resultText"></pre>
        </div>

        <script>
        async function testRepeatRow() {
          const btn = document.getElementById('btnRepeatRow');
          const status = document.getElementById('status');
          btn.disabled = true;
          status.innerHTML = '<div class="status info">⏳ 正在生成 DOCX（Repeat Row）...</div>';
          try {
            const jsonText = document.getElementById('jsonData').value;
            const response = await fetch('/api/demo/repeat-row-test', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: jsonText,
            });
            if (!response.ok) {
              const err = await response.text();
              throw new Error(`HTTP ${response.status}: ${err}`);
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'repeat_row_demo.docx';
            a.click();
            URL.revokeObjectURL(url);
            status.innerHTML = '<div class="status success">✅ DOCX 已生成并下载！请用 Word 或 WPS 打开查看。</div>';
          } catch (err) {
            status.innerHTML = `<div class="status error">❌ 错误：${err.message}</div>`;
          } finally {
            btn.disabled = false;
          }
        }

        async function testRepeatBlock() {
          const btn = document.getElementById('btnRepeatBlock');
          const status = document.getElementById('status');
          btn.disabled = true;
          status.innerHTML = '<div class="status info">⏳ 正在生成 DOCX（Repeat Block）...</div>';
          try {
            const jsonText = document.getElementById('jsonData').value;
            const response = await fetch('/api/demo/repeat-block-test', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: jsonText,
            });
            if (!response.ok) {
              const err = await response.text();
              throw new Error(`HTTP ${response.status}: ${err}`);
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'repeat_block_demo.docx';
            a.click();
            URL.revokeObjectURL(url);
            status.innerHTML = '<div class="status success">✅ DOCX 已生成并下载！请用 Word 或 WPS 打开查看。</div>';
          } catch (err) {
            status.innerHTML = `<div class="status error">❌ 错误：${err.message}</div>`;
          } finally {
            btn.disabled = false;
          }
        }

        async function checkHealth() {
          const status = document.getElementById('status');
          status.innerHTML = '<div class="status info">⏳ 检查中...</div>';
          try {
            const resp = await fetch('/api/system/database/health');
            const json = await resp.json();
            status.innerHTML = `<div class="status success">✅ 后端运行正常：${JSON.stringify(json)}</div>`;
          } catch (err) {
            status.innerHTML = `<div class="status error">❌ 后端连接失败：${err.message}</div>`;
          }
        }
        </script>
        </body>
        </html>
        """;

        return Task.FromResult(Results.Content(html, "text/html; charset=utf-8"));
    }

    /// <summary>
    /// 创建默认演示数据。
    /// </summary>
    private static JsonElement CreateDefaultDemoData()
    {
        string json = """
        {
          "school": { "schoolId": "1001", "schoolName": "示例大学", "year": 2026 },
          "majors": [
            { "majorId": "080901", "majorName": "计算机科学与技术", "warningLevel": "正常", "employmentRate": 95.2 },
            { "majorId": "080902", "majorName": "软件工程", "warningLevel": "正常", "employmentRate": 93.1 },
            { "majorId": "080903", "majorName": "数据科学", "warningLevel": "注意", "employmentRate": 88.5 }
          ],
          "year": 2026
        }
        """;
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// 程序化创建包含 SdtRow（wtb:repeat:major-summary-row）的 DOCX 模板。
    /// </summary>
    private static byte[] CreateTemplateWithSdtRow(ILogger logger)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            Body body = new();
            mainPart.Document.Body = body;

            // 添加表格
            Table table = new();

            // 表头行
            TableRow headerRow = new();
            headerRow.Append(CreateTableCell("专业代码", true));
            headerRow.Append(CreateTableCell("专业名称", true));
            headerRow.Append(CreateTableCell("预警等级", true));
            headerRow.Append(CreateTableCell("就业率", true));
            table.Append(headerRow);

            // SdtRow 原型行（包含 wtb:repeat:major-summary-row Tag）
            SdtRow sdtRow = new();
            SdtProperties sdtProps = new();
            sdtProps.Append(new Tag { Val = "wtb:repeat:major-summary-row" });
            sdtRow.Append(sdtProps);

            SdtContentRow sdtContent = new();
            TableRow prototypeRow = new();
            prototypeRow.Append(CreateTableCell("{{major.majorId}}", false));
            prototypeRow.Append(CreateTableCell("{{major.majorName}}", false));
            prototypeRow.Append(CreateTableCell("{{major.warningLevel}}", false));
            prototypeRow.Append(CreateTableCell("{{major.employmentRate}}", false));
            sdtContent.Append(prototypeRow);
            sdtRow.Append(sdtContent);

            table.Append(sdtRow);
            body.Append(table);

            logger.LogInformation("已创建包含 SdtRow 的演示模板（4 列，Tag: wtb:repeat:major-summary-row）");
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 创建表格单元格。
    /// </summary>
    private static TableCell CreateTableCell(string text, bool isHeader)
    {
        TableCell cell = new();
        Paragraph paragraph = new();
        Run run = new();
        RunProperties runProps = new();

        if (isHeader)
        {
            runProps.Append(new Bold());
        }

        run.Append(runProps);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
        cell.Append(paragraph);

        // 设置单元格属性
        TableCellProperties cellProps = new();
        TableCellWidth width = new() { Width = "2400", Type = TableWidthUnitValues.Dxa };
        cellProps.Append(width);
        if (isHeader)
        {
            cellProps.Append(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Fill = "D9E2F3",
            });
        }

        cell.Append(cellProps);
        return cell;
    }

    /// <summary>
    /// 在生成的 DOCX 中用实际数据替换占位符。
    /// </summary>
    private static void ReplacePlaceholders(
        MainDocumentPart mainPart,
        IDataContextResolver resolver,
        RenderScope rootScope,
        RepeatBlockDefinition definition,
        RepeatExpansionResult expansionResult,
        ILogger logger)
    {
        IReadOnlyList<object?>? items = resolver.ResolveArray(rootScope, definition.SourcePath);
        if (items is null) return;

        // 遍历所有段落，替换 {{...}} 占位符
        List<Paragraph> paragraphs = mainPart.Document.Body!
            .Descendants<Paragraph>()
            .ToList();

        int itemIndex = 0;
        foreach (Paragraph paragraph in paragraphs)
        {
            string fullText = paragraph.InnerText;
            if (!fullText.Contains("{{", StringComparison.Ordinal)) continue;

            // 确定该段落属于哪个实例（通过检查其内容）
            foreach (Run run in paragraph.Elements<Run>())
            {
                Text? text = run.Elements<Text>().FirstOrDefault();
                if (text?.Text is null) continue;

                string textValue = text.Text;
                if (!textValue.Contains("{{", StringComparison.Ordinal)) continue;

                // 为每个数据项创建作用域并替换
                if (itemIndex < items.Count)
                {
                    object? item = items[itemIndex];
                    string? itemKey = resolver.ResolveItemKey(item, definition.ItemKeyPath) ?? itemIndex.ToString();
                    string instanceKey = $"{definition.BlockKey}/{itemKey}";
                    RenderScope itemScope = rootScope.CreateChild(
                        definition.ItemAlias, item, itemIndex, instanceKey);

                    // 替换占位符
                    string replaced = ReplacePlaceholdersInText(textValue, resolver, itemScope);
                    text.Text = replaced;
                }
                else
                {
                    // 超出数据范围，清空占位符
                    text.Text = "";
                }
            }

            if (paragraph.InnerText.Contains("{{", StringComparison.Ordinal))
            {
                itemIndex++;
            }
        }

        mainPart.Document.Save();
    }

    /// <summary>
    /// 在文本中替换所有 {{path}} 占位符。
    /// </summary>
    private static string ReplacePlaceholdersInText(
        string text,
        IDataContextResolver resolver,
        RenderScope scope)
    {
        int searchStart = 0;
        while (searchStart < text.Length)
        {
            int openIndex = text.IndexOf("{{", searchStart, StringComparison.Ordinal);
            if (openIndex < 0) break;

            int closeIndex = text.IndexOf("}}", openIndex + 2, StringComparison.Ordinal);
            if (closeIndex < 0) break;

            string path = text[(openIndex + 2)..closeIndex].Trim();
            object? value = resolver.ResolveValue(scope, path);
            string replacement = value switch
            {
                null => "",
                decimal d => d.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                double d => d.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };

            text = text[..openIndex] + replacement + text[(closeIndex + 2)..];
            searchStart = openIndex + replacement.Length;
        }

        return text;
    }

    /// <summary>
    /// 在 DOCX 末尾追加诊断信息段落。
    /// </summary>
    private static byte[] AppendDiagnosticsParagraph(byte[] docxBytes, List<string> diagnostics)
    {
        try
        {
            using MemoryStream stream = new(docxBytes.Length + 8192);
            stream.Write(docxBytes, 0, docxBytes.Length);
            stream.Position = 0;

            using (WordprocessingDocument document = WordprocessingDocument.Open(stream, true))
            {
                Body? body = document.MainDocumentPart?.Document.Body;
                if (body is not null)
                {
                    // 添加分隔段落
                    body.Append(new Paragraph(
                        new Run(new Break { Type = BreakValues.Page })));

                    // 添加诊断标题
                    body.Append(new Paragraph(
                        new Run(
                            new RunProperties(new Bold(), new FontSize { Val = "28" }),
                            new Text("📋 生成诊断信息"))));

                    foreach (string diag in diagnostics)
                    {
                        body.Append(new Paragraph(
                            new Run(new Text(diag) { Space = SpaceProcessingModeValues.Preserve })));
                    }

                    document.MainDocumentPart!.Document.Save();
                }
            }

            return stream.ToArray();
        }
        catch
        {
            return docxBytes;
        }
    }

    /// <summary>
    /// 创建仅包含诊断信息的简单 DOCX。
    /// </summary>
    private static byte[] CreateDiagnosticsDocx(List<string> diagnostics)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            Body body = new();
            mainPart.Document.Body = body;

            body.Append(new Paragraph(
                new Run(
                    new RunProperties(new Bold(), new FontSize { Val = "32" }),
                    new Text("WordTemplateBinding 演示结果"))));

            foreach (string diag in diagnostics)
            {
                body.Append(new Paragraph(
                    new Run(new Text(diag) { Space = SpaceProcessingModeValues.Preserve })));
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
