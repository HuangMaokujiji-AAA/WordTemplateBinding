using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 在原始 DOCX 副本中局部修改 Text 节点以生成报告。
/// </summary>
public sealed class WordReportRenderer : IWordReportRenderer
{
    private readonly IDataValueFormatter _formatter;
    private readonly TemplateProcessingOptions _options;

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
        _options = options;
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

                foreach (IGrouping<DocumentPartLocatorKey, ReplacementInstruction> partGroup
                             in replacements.GroupBy(item => new DocumentPartLocatorKey(
                                 item.MockItem.Locator.PartKind,
                                 item.MockItem.Locator.PartKey)))
                {
                    ReplacementInstruction firstPartReplacement = partGroup.First();
                    DocumentPartContext partContext = ResolveDocumentPart(
                        mainPart,
                        partGroup.Key,
                        firstPartReplacement.Binding.LocatorId);

                    foreach (IGrouping<int, ReplacementInstruction> paragraphGroup
                                 in partGroup.GroupBy(
                                     item => item.MockItem.Locator.ParagraphIndex))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int paragraphIndex = paragraphGroup.Key;
                        if (paragraphIndex < 0 || paragraphIndex >= partContext.Paragraphs.Count)
                        {
                            throw new LocatorNotFoundException(
                                paragraphGroup.First().Binding.LocatorId);
                        }

                        ParagraphTextMap map = ParagraphTextMapBuilder.Build(
                            partContext.Paragraphs[paragraphIndex]);
                        List<ReplacementInstruction> paragraphReplacements = paragraphGroup
                            .OrderBy(item => item.MockItem.Locator.StartOffset)
                            .ToList();
                        ValidateParagraphReplacements(map, paragraphReplacements);

                        // 必须从后向前替换，避免后方文本长度变化破坏同段落前方定位。
                        foreach (ReplacementInstruction replacement in paragraphReplacements
                                     .OrderByDescending(
                                         item => item.MockItem.Locator.StartOffset))
                        {
                            ReplaceMappedText(
                                map,
                                replacement.MockItem.Locator,
                                replacement.FormattedValue);
                        }
                    }

                    partContext.Save();
                }

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
                        OpenXmlChartWriter.Write(mainPart, chartItem, chartValue);
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
    /// 在写入前统一校验定位原值、上下文和范围重叠，保证修改过程具有原子性。
    /// </summary>
    /// <param name="map">目标段落文本映射。</param>
    /// <param name="replacements">该段落的替换指令。</param>
    private void ValidateParagraphReplacements(
        ParagraphTextMap map,
        IReadOnlyList<ReplacementInstruction> replacements)
    {
        int previousEnd = -1;
        foreach (ReplacementInstruction replacement in replacements)
        {
            TextLocator locator = replacement.MockItem.Locator;
            int endOffset = locator.StartOffset + locator.Length;
            if (!OpenXmlDocumentHelpers.IsSupportedTextLocator(locator) ||
                locator.StartOffset < 0 ||
                locator.Length <= 0 ||
                endOffset > map.FullText.Length)
            {
                throw new LocatorNotFoundException(replacement.Binding.LocatorId);
            }

            if (locator.StartOffset < previousEnd)
            {
                throw new ReportRenderingException("同一段落中的绑定范围发生重叠。");
            }

            string currentValue = map.FullText.Substring(locator.StartOffset, locator.Length);
            string contextHash = OpenXmlDocumentHelpers.ComputeContextHash(
                map.FullText,
                locator.StartOffset,
                locator.Length,
                _options.ContextLength);
            if (!string.Equals(currentValue, locator.OriginalValue, StringComparison.Ordinal) ||
                !string.Equals(contextHash, locator.ContextHash, StringComparison.Ordinal))
            {
                throw new LocatorNotFoundException(replacement.Binding.LocatorId);
            }

            previousEnd = endOffset;
        }
    }

    /// <summary>
    /// 根据结构化部件定位解析正文或具体页脚的段落集合和保存动作。
    /// </summary>
    private static DocumentPartContext ResolveDocumentPart(
        MainDocumentPart mainPart,
        DocumentPartLocatorKey key,
        string locatorId)
    {
        if (key.PartKind == DocumentPartKind.MainDocument &&
            string.Equals(
                key.PartKey,
                OpenXmlDocumentHelpers.MainDocumentPartKey,
                StringComparison.Ordinal))
        {
            Body body = mainPart.Document?.Body
                ?? throw new ReportRenderingException("模板缺少主文档正文。");
            return new DocumentPartContext(
                OpenXmlDocumentHelpers.GetMainDocumentParagraphs(body),
                () => mainPart.Document.Save());
        }

        if (key.PartKind == DocumentPartKind.Footer)
        {
            FooterPart? footerPart = mainPart.FooterParts.FirstOrDefault(part =>
                string.Equals(
                    part.Uri.OriginalString,
                    key.PartKey,
                    StringComparison.Ordinal));
            if (footerPart?.Footer is null)
            {
                throw new LocatorNotFoundException(locatorId);
            }

            return new DocumentPartContext(
                OpenXmlDocumentHelpers.GetTextParagraphs(footerPart.Footer),
                () => footerPart.Footer.Save());
        }

        throw new LocatorNotFoundException(locatorId);
    }

    /// <summary>
    /// 将跨一个或多个 Text 节点的目标范围局部替换为新值。
    /// </summary>
    /// <param name="map">扫描时同构的段落文本映射。</param>
    /// <param name="locator">目标定位信息。</param>
    /// <param name="replacementValue">已经格式化的新值。</param>
    private static void ReplaceMappedText(
        ParagraphTextMap map,
        TextLocator locator,
        string replacementValue)
    {
        int targetEnd = locator.StartOffset + locator.Length;
        List<TextSegment> affected = map.Segments
            .Where(segment =>
                segment.StartOffset < targetEnd &&
                segment.EndOffset > locator.StartOffset)
            .ToList();
        if (affected.Count == 0)
        {
            throw new LocatorNotFoundException(locator.OriginalValue);
        }

        for (int index = 0; index < affected.Count; index++)
        {
            TextSegment segment = affected[index];
            string originalNodeText = segment.TextNode.Text ?? string.Empty;
            int localStart = Math.Max(0, locator.StartOffset - segment.StartOffset);
            int localEnd = Math.Min(segment.Length, targetEnd - segment.StartOffset);
            string prefix = originalNodeText[..localStart];
            string suffix = originalNodeText[localEnd..];

            if (index == 0)
            {
                // 新值写入首个相关 Text 节点，使其继承首个 Run 的字体、字号和强调格式。
                segment.TextNode.Text = prefix + replacementValue +
                    (affected.Count == 1 ? suffix : string.Empty);
            }
            else if (index == affected.Count - 1)
            {
                // 最后节点仅保留目标范围之后的原始文本，不删除所属 Run 或其他 XML。
                segment.TextNode.Text = suffix;
            }
            else
            {
                segment.TextNode.Text = prefix + suffix;
            }

            OpenXmlDocumentHelpers.PreserveBoundaryWhitespace(segment.TextNode);
        }
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

    /// <summary>
    /// 表示用于批量定位替换的文档部件键。
    /// </summary>
    private sealed record DocumentPartLocatorKey(
        DocumentPartKind PartKind,
        string PartKey);

    /// <summary>
    /// 表示一个已解析的可写文本部件。
    /// </summary>
    private sealed record DocumentPartContext(
        IReadOnlyList<Paragraph> Paragraphs,
        Action Save);
}
