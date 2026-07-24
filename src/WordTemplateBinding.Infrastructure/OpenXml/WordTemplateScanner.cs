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
/// 扫描主文档正文和页脚中受支持的模拟数据并生成结构化定位与预览。
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
    /// <param name="recognizers">模拟数据识别器集合。</param>
    /// <param name="locatorIdGenerator">定位标识生成器。</param>
    /// <param name="previewBuilder">结构化预览构建器。</param>
    /// <param name="options">模板处理配置。</param>
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
    public Task<TemplateScanResult> ScanAsync(
        ReadOnlyMemory<byte> templateBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using MemoryStream stream = new(templateBytes.ToArray(), writable: false);
            using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
            ValidateDocument(document);

            MainDocumentPart mainPart = document.MainDocumentPart
                ?? throw new InvalidTemplateFileException("DOCX 缺少主文档部件。");
            Body body = mainPart.Document?.Body
                ?? throw new InvalidTemplateFileException("DOCX 缺少主文档正文。");
            string contentHash = Convert.ToHexString(SHA256.HashData(templateBytes.Span))
                .ToLowerInvariant();
            List<string> paragraphTexts = new();
            List<MockDataItem> mockItems = new();

            ScanPart(
                OpenXmlDocumentHelpers.GetMainDocumentParagraphs(body),
                DocumentPartKind.MainDocument,
                OpenXmlDocumentHelpers.MainDocumentPartKey,
                contentHash,
                paragraphTexts,
                mockItems,
                cancellationToken);

            foreach (FooterPart footerPart in mainPart.FooterParts
                         .OrderBy(part => part.Uri.OriginalString, StringComparer.Ordinal))
            {
                if (footerPart.Footer is null)
                {
                    continue;
                }

                ScanPart(
                    OpenXmlDocumentHelpers.GetTextParagraphs(footerPart.Footer),
                    DocumentPartKind.Footer,
                    footerPart.Uri.OriginalString,
                    contentHash,
                    paragraphTexts,
                    mockItems,
                    cancellationToken);
            }

            IReadOnlyList<ChartTemplateItem> charts = OpenXmlChartReader.Read(
                mainPart,
                contentHash,
                _locatorIdGenerator);
            ReusableTemplateManifest manifest = ReusableTemplateManifestSerializer.Read(mainPart);

            IReadOnlyList<MockDataItem> readOnlyItems = mockItems.AsReadOnly();
            return Task.FromResult(new TemplateScanResult
            {
                ContentHash = contentHash,
                MockItems = readOnlyItems,
                Charts = charts,
                Preview = _previewBuilder.Build(paragraphTexts.AsReadOnly(), readOnlyItems),
                BindingManifest = manifest,
            });
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

    /// <summary>
    /// 扫描一个独立 Word 文档部件中的段落，并使用部件内段落索引生成定位。
    /// </summary>
    private void ScanPart(
        IReadOnlyList<Paragraph> paragraphs,
        DocumentPartKind partKind,
        string partKey,
        string contentHash,
        ICollection<string> paragraphTexts,
        ICollection<MockDataItem> mockItems,
        CancellationToken cancellationToken)
    {
        for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParagraphTextMap map = ParagraphTextMapBuilder.Build(paragraphs[paragraphIndex]);
            int previewParagraphIndex = paragraphTexts.Count;
            paragraphTexts.Add(map.FullText);

            List<RecognizedMockData> recognized = ResolveOverlaps(
                _recognizers.SelectMany(recognizer => recognizer
                    .Recognize(map)
                    .Select(item => new PrioritizedRecognition(
                        item,
                        recognizer.Priority))));

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
                });
            }
        }
    }

    /// <summary>
    /// 将多个识别器的结果合并为互不重叠的定位范围。
    /// </summary>
    /// <param name="candidates">全部候选识别结果。</param>
    /// <returns>返回按起始位置排列的非重叠结果。</returns>
    private static List<RecognizedMockData> ResolveOverlaps(
        IEnumerable<PrioritizedRecognition> candidates)
    {
        List<PrioritizedRecognition> selected = new();

        // 人工意图优先于自动推断；同优先级仍保持原有的“较早起点、同起点较长范围”规则。
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
            if (overlaps)
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected
            .Select(item => item.Item)
            .OrderBy(item => item.StartOffset)
            .ToList();
    }

    private sealed record PrioritizedRecognition(
        RecognizedMockData Item,
        MockDataRecognitionPriority Priority);

    /// <summary>
    /// 校验文档类型以及主文档正文是否存在。
    /// </summary>
    /// <param name="document">已经打开的 Word 文档。</param>
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
}
