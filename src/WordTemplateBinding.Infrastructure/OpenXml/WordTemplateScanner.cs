using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 扫描 DOCX 的正文、页眉页脚、脚注尾注和文本框，并生成结构化定位。
/// </summary>
public sealed class WordTemplateScanner : IWordTemplateScanner
{
    private readonly IReadOnlyList<IMockDataRecognizer> _recognizers;
    private readonly ILocatorIdGenerator _locatorIdGenerator;
    private readonly IDocumentPreviewBuilder _previewBuilder;
    private readonly TemplateProcessingOptions _options;

    /// <summary>
    /// 初始化 Word 模板扫描器。
    /// </summary>
    public WordTemplateScanner(
        IEnumerable<IMockDataRecognizer> recognizers,
        ILocatorIdGenerator locatorIdGenerator,
        IDocumentPreviewBuilder previewBuilder,
        TemplateProcessingOptions options)
    {
        _recognizers = recognizers.ToList().AsReadOnly();
        _locatorIdGenerator = locatorIdGenerator;
        _previewBuilder = previewBuilder;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<TemplateScanResult> ScanAsync(
        ReadOnlyMemory<byte> templateBytes,
        CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new(templateBytes.ToArray(), writable: false);
        return await ScanAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TemplateScanResult> ScanAsync(
        Stream seekableDocxStream,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!seekableDocxStream.CanRead || !seekableDocxStream.CanSeek)
        {
            throw new InvalidTemplateFileException("OpenXML 扫描需要可读、可定位的 DOCX 流。");
        }

        try
        {
            seekableDocxStream.Position = 0;
            string contentHash = await ComputeHashAsync(
                seekableDocxStream,
                cancellationToken);
            seekableDocxStream.Position = 0;

            using WordprocessingDocument document =
                WordprocessingDocument.Open(seekableDocxStream, false);
            ValidateDocument(document);

            MainDocumentPart mainPart = document.MainDocumentPart
                ?? throw new InvalidTemplateFileException("DOCX 缺少主文档部件。");
            Body body = mainPart.Document?.Body
                ?? throw new InvalidTemplateFileException("DOCX 缺少主文档正文。");
            List<string> paragraphTexts = new();
            List<MockDataItem> mockItems = new();
            List<TemplateParseWarning> warnings = new();
            IReadOnlyList<TableTemplateItem> tables = OpenXmlTableReader.Read(
                body,
                contentHash,
                _locatorIdGenerator);
            IReadOnlyList<Table> mainDocumentTables = body.Descendants<Table>()
                .Where(table => !table.Ancestors<Table>().Any())
                .ToList()
                .AsReadOnly();
            IReadOnlySet<Table> recognizedTables = tables
                .Where(item => item.Locator.TableIndex >= 0 &&
                               item.Locator.TableIndex < mainDocumentTables.Count)
                .Select(item => mainDocumentTables[item.Locator.TableIndex])
                .ToHashSet();

            ScanPartAndTextBoxes(
                body,
                DocumentPartKind.MainDocument,
                OpenXmlDocumentHelpers.MainDocumentPartKey,
                contentHash,
                paragraphTexts,
                mockItems,
                cancellationToken,
                recognizedTables);

            foreach (HeaderPart headerPart in mainPart.HeaderParts
                         .OrderBy(part => part.Uri.OriginalString, StringComparer.Ordinal))
            {
                if (headerPart.Header is not null)
                {
                    ScanPartAndTextBoxes(
                        headerPart.Header,
                        DocumentPartKind.Header,
                        headerPart.Uri.OriginalString,
                        contentHash,
                        paragraphTexts,
                        mockItems,
                        cancellationToken,
                        recognizedTables: null);
                }
            }

            foreach (FooterPart footerPart in mainPart.FooterParts
                         .OrderBy(part => part.Uri.OriginalString, StringComparer.Ordinal))
            {
                if (footerPart.Footer is not null)
                {
                    ScanPartAndTextBoxes(
                        footerPart.Footer,
                        DocumentPartKind.Footer,
                        footerPart.Uri.OriginalString,
                        contentHash,
                        paragraphTexts,
                        mockItems,
                        cancellationToken,
                        recognizedTables: null);
                }
            }

            if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
            {
                ScanPartAndTextBoxes(
                    footnotes,
                    DocumentPartKind.Footnote,
                    mainPart.FootnotesPart.Uri.OriginalString,
                    contentHash,
                    paragraphTexts,
                    mockItems,
                    cancellationToken,
                    recognizedTables: null);
            }

            if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
            {
                ScanPartAndTextBoxes(
                    endnotes,
                    DocumentPartKind.Endnote,
                    mainPart.EndnotesPart.Uri.OriginalString,
                    contentHash,
                    paragraphTexts,
                    mockItems,
                    cancellationToken,
                    recognizedTables: null);
            }

            if (FindThemeFill(document))
            {
                warnings.Add(new TemplateParseWarning(
                    "YELLOW_THEME_COLOR_UNRESOLVED",
                    "发现主题色底纹；主题色无法安全判定为黄色，相关范围未自动标记。"));
            }

            IReadOnlyList<ChartTemplateItem> charts = OpenXmlChartReader.Read(
                mainPart,
                contentHash,
                _locatorIdGenerator);
            ReusableTemplateManifest manifest = ReusableTemplateManifestSerializer.Read(mainPart);
            if (mockItems.Count == 0 && charts.Count == 0 && tables.Count == 0)
            {
                warnings.Add(new TemplateParseWarning(
                    "NO_BINDABLE_ELEMENTS",
                    "模板中暂未识别到可绑定标记。"));
            }

            IReadOnlyList<MockDataItem> readOnlyItems = mockItems.AsReadOnly();
            return new TemplateScanResult
            {
                ContentHash = contentHash,
                MockItems = readOnlyItems,
                Charts = charts,
                Tables = tables,
                Preview = _previewBuilder.Build(paragraphTexts.AsReadOnly(), readOnlyItems),
                BindingManifest = manifest,
                Warnings = warnings.AsReadOnly(),
            };
        }
        catch (InvalidTemplateFileException)
        {
            throw;
        }
        catch (OpenXmlPackageException exception)
        {
            throw new InvalidTemplateFileException("DOCX 包结构无效或已经损坏。", exception);
        }
        catch (FileFormatException exception)
        {
            throw new InvalidTemplateFileException("上传文件不是有效的 Word Open XML 文档。", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidTemplateFileException("DOCX 压缩包内容无效。", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidTemplateFileException("读取 DOCX 文件失败。", exception);
        }
    }

    private void ScanPartAndTextBoxes(
        OpenXmlElement root,
        DocumentPartKind partKind,
        string partKey,
        string contentHash,
        ICollection<string> paragraphTexts,
        ICollection<MockDataItem> mockItems,
        CancellationToken cancellationToken,
        IReadOnlySet<Table>? recognizedTables)
    {
        ScanPart(
            OpenXmlDocumentHelpers.GetTextParagraphs(root),
            partKind,
            partKey,
            contentHash,
            paragraphTexts,
            mockItems,
            cancellationToken,
            recognizedTables);

        IReadOnlyList<TextBoxContent> textBoxes = root
            .Descendants<TextBoxContent>()
            .ToList()
            .AsReadOnly();
        for (int textBoxIndex = 0; textBoxIndex < textBoxes.Count; textBoxIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Paragraph> textBoxParagraphs = textBoxes[textBoxIndex]
                .Descendants<Paragraph>()
                .Where(paragraph =>
                    ReferenceEquals(
                        paragraph.Ancestors<TextBoxContent>().FirstOrDefault(),
                        textBoxes[textBoxIndex]))
                .ToList()
                .AsReadOnly();
            ScanPart(
                textBoxParagraphs,
                DocumentPartKind.TextBox,
                $"{partKey}#textbox:{textBoxIndex}",
                contentHash,
                paragraphTexts,
                mockItems,
                cancellationToken,
                recognizedTables: null);
        }
    }

    private void ScanPart(
        IReadOnlyList<Paragraph> paragraphs,
        DocumentPartKind partKind,
        string partKey,
        string contentHash,
        ICollection<string> paragraphTexts,
        ICollection<MockDataItem> mockItems,
        CancellationToken cancellationToken,
        IReadOnlySet<Table>? recognizedTables)
    {
        for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParagraphTextMap map = ParagraphTextMapBuilder.Build(paragraphs[paragraphIndex]);
            int previewParagraphIndex = paragraphTexts.Count;
            paragraphTexts.Add(map.FullText);

            if (recognizedTables is not null &&
                paragraphs[paragraphIndex].Ancestors<Table>()
                    .Any(recognizedTables.Contains))
            {
                continue;
            }

            List<RecognizedMockData> recognized = ResolveOverlaps(
                _recognizers.SelectMany(recognizer => recognizer
                    .Recognize(map)
                    .Select(item => new PrioritizedRecognition(item, recognizer.Priority))));

            for (int occurrenceIndex = 0; occurrenceIndex < recognized.Count; occurrenceIndex++)
            {
                RecognizedMockData item = recognized[occurrenceIndex];
                TextLocator locator = new()
                {
                    PartKind = partKind,
                    PartKey = partKey,
                    ParagraphIndex = paragraphIndex,
                    StartOffset = item.StartOffset,
                    Length = item.Length,
                    OccurrenceIndex = occurrenceIndex,
                    OriginalValue = item.OriginalText,
                    ContextHash = OpenXmlDocumentHelpers.ComputeContextHash(
                        map.FullText,
                        item.StartOffset,
                        item.Length,
                        _options.ContextLength),
                };

                mockItems.Add(new MockDataItem
                {
                    LocatorId = _locatorIdGenerator.Generate(contentHash, locator),
                    MockValue = item.Value,
                    DataType = item.DataType,
                    Locator = locator,
                    ParagraphText = map.FullText,
                    PreviewParagraphIndex = previewParagraphIndex,
                    IsBound = false,
                    BoundDataPath = null,
                    PlaceholderCandidatePath = item.PlaceholderCandidatePath,
                    RecognitionKind = item.RecognitionKind,
                    ContentControlTag = ResolveContentControlTag(
                        map,
                        item.StartOffset,
                        item.Length),
                });
            }
        }
    }

    private static string? ResolveContentControlTag(
        ParagraphTextMap map,
        int startOffset,
        int length)
    {
        int endOffset = startOffset + length;
        return map.Segments
            .Where(segment =>
                segment.StartOffset < endOffset &&
                segment.EndOffset > startOffset)
            .SelectMany(segment => segment.TextNode.Ancestors<SdtElement>())
            .Select(element => element.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value)
            .FirstOrDefault(value =>
                value?.StartsWith("rtb-marker:", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static List<RecognizedMockData> ResolveOverlaps(
        IEnumerable<PrioritizedRecognition> candidates)
    {
        List<PrioritizedRecognition> selected = new();
        foreach (PrioritizedRecognition candidate in candidates
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.Item.StartOffset)
                     .ThenByDescending(item => item.Item.Length))
        {
            int candidateEnd = candidate.Item.StartOffset + candidate.Item.Length;
            bool overlaps = selected.Any(existing =>
            {
                int existingEnd = existing.Item.StartOffset + existing.Item.Length;
                return candidate.Item.StartOffset < existingEnd &&
                    existing.Item.StartOffset < candidateEnd;
            });
            if (!overlaps)
            {
                selected.Add(candidate);
            }
        }

        return selected
            .Select(item => item.Item)
            .OrderBy(item => item.StartOffset)
            .ToList();
    }

    private static bool FindThemeFill(WordprocessingDocument document)
    {
        OpenXmlPartRootElement? root = document.MainDocumentPart?.Document;
        return root?.Descendants<Shading>().Any(shading =>
            shading.ThemeFill is not null &&
            string.IsNullOrWhiteSpace(shading.Fill?.Value)) == true;
    }

    private static async Task<string> ComputeHashAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ValidateDocument(WordprocessingDocument document)
    {
        if (document.DocumentType != WordprocessingDocumentType.Document)
        {
            throw new InvalidTemplateFileException("只支持普通 DOCX 文档，不支持宏或模板类型。");
        }

        if (document.MainDocumentPart?.Document?.Body is null)
        {
            throw new InvalidTemplateFileException("DOCX 缺少主文档正文。");
        }
    }

    private sealed record PrioritizedRecognition(
        RecognizedMockData Item,
        MockDataRecognitionPriority Priority);
}
