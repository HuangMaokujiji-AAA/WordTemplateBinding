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
/// 扫描主文档正文中受支持的模拟数据并生成结构化定位与预览。
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

            Body body = document.MainDocumentPart?.Document?.Body
                ?? throw new InvalidTemplateFileException("DOCX 缺少主文档正文。");
            IReadOnlyList<Paragraph> paragraphs = OpenXmlDocumentHelpers.GetMainDocumentParagraphs(body);
            string contentHash = Convert.ToHexString(SHA256.HashData(templateBytes.Span))
                .ToLowerInvariant();
            List<string> paragraphTexts = new(paragraphs.Count);
            List<MockDataItem> mockItems = new();

            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParagraphTextMap map = ParagraphTextMapBuilder.Build(paragraphs[paragraphIndex]);
                paragraphTexts.Add(map.FullText);

                List<RecognizedMockData> recognized = ResolveOverlaps(
                    _recognizers.SelectMany(recognizer => recognizer.Recognize(map)));

                for (int occurrenceIndex = 0; occurrenceIndex < recognized.Count; occurrenceIndex++)
                {
                    RecognizedMockData item = recognized[occurrenceIndex];
                    TextLocator locator = new()
                    {
                        PartKind = DocumentPartKind.MainDocument,
                        PartKey = OpenXmlDocumentHelpers.MainDocumentPartKey,
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
                        PreviewParagraphIndex = paragraphIndex,
                        IsBound = false,
                        BoundDataPath = null,
                    });
                }
            }

            IReadOnlyList<MockDataItem> readOnlyItems = mockItems.AsReadOnly();
            return Task.FromResult(new TemplateScanResult
            {
                ContentHash = contentHash,
                MockItems = readOnlyItems,
                Preview = _previewBuilder.Build(paragraphTexts.AsReadOnly(), readOnlyItems),
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
    /// 将多个识别器的结果合并为互不重叠的定位范围。
    /// </summary>
    /// <param name="candidates">全部候选识别结果。</param>
    /// <returns>返回按起始位置排列的非重叠结果。</returns>
    private static List<RecognizedMockData> ResolveOverlaps(
        IEnumerable<RecognizedMockData> candidates)
    {
        List<RecognizedMockData> selected = new();
        int selectedEnd = -1;

        // 同一起点优先保留较长范围；显式文字标记因此会覆盖其内部可能出现的数字候选。
        foreach (RecognizedMockData candidate in candidates
                     .OrderBy(item => item.StartOffset)
                     .ThenByDescending(item => item.Length))
        {
            if (candidate.StartOffset < selectedEnd)
            {
                continue;
            }

            selected.Add(candidate);
            selectedEnd = candidate.StartOffset + candidate.Length;
        }

        return selected;
    }

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
