using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Infrastructure.OpenXml.Charts;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 在原始 DOCX 副本中局部修改 Text 节点以生成报告。
/// </summary>
public sealed class WordReportRenderer : IWordReportRenderer
{
    private readonly IDataValueFormatter _formatter;
    private readonly OpenXmlTextReplacementService _textReplacementService;

    /// <summary>
    /// 初始化 Word 报告渲染器。
    /// </summary>
    /// <param name="formatter">数据值格式化器。</param>
    /// <param name="options">模板处理配置。</param>
    public WordReportRenderer(
        IDataValueFormatter formatter,
        TemplateProcessingOptions options)
    {
        _formatter = formatter;
        _textReplacementService = new OpenXmlTextReplacementService(options);
    }

    /// <inheritdoc />
    public async Task<RenderedReport> RenderAsync(
        TemplateDocument template,
        IReadOnlyCollection<TemplateBinding> bindings,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            byte[] originalBytes = template.GetOriginalBytesCopy();
            using MemoryStream stream = new(originalBytes.Length + 4096);
            await stream.WriteAsync(originalBytes, cancellationToken);
            stream.Position = 0;

            using (WordprocessingDocument document = WordprocessingDocument.Open(stream, true))
            {
                MainDocumentPart mainPart = document.MainDocumentPart
                    ?? throw new ReportRenderingException("模板缺少主文档部件。");
                Dictionary<string, MockDataItem> mockItems = template.ScanResult.MockItems
                    .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);
                Dictionary<string, ChartTemplateItem> chartItems = template.ScanResult.Charts
                    .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);
                List<ReplacementInstruction> replacements = BuildReplacementInstructions(
                    bindings.Where(binding => binding.TargetKind == BindingTargetKind.Text).ToList(),
                    values,
                    mockItems);
                _textReplacementService.ReplaceAll(
                    mainPart,
                    replacements.Select(item => new OpenXmlTextReplacement(
                            item.Binding.LocatorId,
                            item.MockItem.Locator,
                            item.FormattedValue))
                        .ToList()
                        .AsReadOnly(),
                    cancellationToken);

                foreach (TemplateBinding binding in bindings.Where(
                             binding => binding.TargetKind == BindingTargetKind.Chart))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!chartItems.TryGetValue(binding.LocatorId, out ChartTemplateItem? chartItem))
                    {
                        throw new LocatorNotFoundException(binding.LocatorId);
                    }

                    if (!values.TryGetValue(binding.DataPath, out object? chartValue))
                    {
                        throw new MissingDataValueException(binding.DataPath);
                    }

                    try
                    {
                        if (binding.ChartMapping is not null && chartItem.DataDefinition is not null)
                        {
                            // New pipeline: resolve via mapping, then write to workbook + chart XML
                            NormalizedChartData normData = ChartDataBindingResolver.Resolve(
                                chartValue, binding.ChartMapping, chartItem.DataDefinition);

                            // Write to embedded workbook first
                            try
                            {
                                EmbeddedChartWorkbookWriter.Write(
                                    mainPart.ChartParts.First(p =>
                                        string.Equals(p.Uri.OriginalString, chartItem.Locator.PartKey, StringComparison.Ordinal)),
                                    normData,
                                    chartItem.DataDefinition);
                            }
                            catch (InvalidOperationException)
                            {
                                // No embedded workbook; that's OK, just write to cache
                            }

                            // Write to Chart XML caches + update formulas
                            OpenXmlChartWriter.Write(mainPart, chartItem, normData, chartItem.DataDefinition);
                        }
                        else
                        {
                            // Legacy: use ChartDataSetParser for backward compatibility
                            OpenXmlChartWriter.Write(mainPart, chartItem, chartValue);
                        }
                    }
                    catch (Exception exception) when (
                        exception is FormatException or InvalidCastException or OverflowException)
                    {
                        throw new DataValueConversionException(binding.DataPath, exception);
                    }
                }
            }

            return new RenderedReport(stream.ToArray(), BuildDownloadFileName(template.OriginalFileName));
        }
        catch (WordTemplateBindingException)
        {
            throw;
        }
        catch (OpenXmlPackageException exception)
        {
            throw new ReportRenderingException("生成 DOCX 时 OpenXML 包处理失败。", exception);
        }
        catch (FileFormatException exception)
        {
            throw new ReportRenderingException("生成 DOCX 时发现模板格式无效。", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new ReportRenderingException("生成 DOCX 时压缩包数据无效。", exception);
        }
        catch (IOException exception)
        {
            throw new ReportRenderingException("生成 DOCX 时发生流读写错误。", exception);
        }
    }

    /// <summary>
    /// 将绑定和原始模拟数据转换为经过格式化的替换指令。
    /// </summary>
    /// <param name="bindings">模板绑定关系。</param>
    /// <param name="values">合并后的数据值。</param>
    /// <param name="mockItems">按定位标识索引的模拟数据。</param>
    /// <returns>返回替换指令列表。</returns>
    private List<ReplacementInstruction> BuildReplacementInstructions(
        IReadOnlyCollection<TemplateBinding> bindings,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, MockDataItem> mockItems)
    {
        List<ReplacementInstruction> replacements = new(bindings.Count);
        foreach (TemplateBinding binding in bindings)
        {
            if (!mockItems.TryGetValue(binding.LocatorId, out MockDataItem? mockItem))
            {
                throw new LocatorNotFoundException(binding.LocatorId);
            }

            if (!values.TryGetValue(binding.DataPath, out object? value))
            {
                throw new MissingDataValueException(binding.DataPath);
            }

            try
            {
                replacements.Add(new ReplacementInstruction(
                    binding,
                    mockItem,
                    _formatter.Format(value, binding.DataType, CultureInfo.InvariantCulture)));
            }
            catch (Exception exception) when (
                exception is FormatException or InvalidCastException or OverflowException)
            {
                throw new DataValueConversionException(binding.DataPath, exception);
            }
        }

        return replacements;
    }

    /// <summary>
    /// 根据模板文件名生成不含路径和非法字符的下载文件名。
    /// </summary>
    /// <param name="originalFileName">模板原始文件名。</param>
    /// <returns>返回安全的生成报告文件名。</returns>
    private static string BuildDownloadFileName(string originalFileName)
    {
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(originalFileName));
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        invalidCharacters.UnionWith("<>:\"/\\|?*");
        string sanitized = new(stem
            .Where(character => !invalidCharacters.Contains(character) && !char.IsControl(character))
            .ToArray());
        sanitized = sanitized.Trim();
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..100];
        }

        return $"{(string.IsNullOrWhiteSpace(sanitized) ? "report" : sanitized)}_generated.docx";
    }

    /// <summary>
    /// 表示一次经过校验和格式化的文本替换。
    /// </summary>
    /// <param name="Binding">绑定关系。</param>
    /// <param name="MockItem">目标模拟数据。</param>
    /// <param name="FormattedValue">格式化后的替换文本。</param>
    private sealed record ReplacementInstruction(
        TemplateBinding Binding,
        MockDataItem MockItem,
        string FormattedValue);

}
