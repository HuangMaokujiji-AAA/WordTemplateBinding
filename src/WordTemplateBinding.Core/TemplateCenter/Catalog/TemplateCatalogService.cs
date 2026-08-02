#pragma warning disable CS1591
using System.Text.Json;
using System.Text.Json.Serialization;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;

public sealed class TemplateCatalogService
{
    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly ITemplateRepository _templates;
    private readonly ITemplateVersionRepository _versions;
    private readonly ITemplateElementRepository _elements;
    private readonly ITemplateSegmentRepository _segments;
    private readonly IFileStorageService _files;
    private readonly IWordTemplateScanner _scanner;
    private readonly IWordTemplateSegmentScanner _segmentScanner;
    private readonly ITemplateElementIdentityResolver _identityResolver;
    private readonly TemplateProcessingOptions _options;
    private readonly DatabaseFileStorageOptions _fileOptions;

    public TemplateCatalogService(
        ITemplateRepository templates,
        ITemplateVersionRepository versions,
        ITemplateElementRepository elements,
        ITemplateSegmentRepository segments,
        IFileStorageService files,
        IWordTemplateScanner scanner,
        IWordTemplateSegmentScanner segmentScanner,
        ITemplateElementIdentityResolver identityResolver,
        TemplateProcessingOptions options,
        DatabaseFileStorageOptions fileOptions)
    {
        _templates = templates;
        _versions = versions;
        _elements = elements;
        _segments = segments;
        _files = files;
        _scanner = scanner;
        _segmentScanner = segmentScanner;
        _identityResolver = identityResolver;
        _options = options;
        _fileOptions = fileOptions;
    }

    public Task<PagedResult<TemplateRecord>> ListAsync(
        TemplateListQuery query,
        CancellationToken cancellationToken) =>
        _templates.ListAsync(query, cancellationToken);

    public async Task<TemplateRecord> GetTemplateAsync(
        ulong templateId,
        CancellationToken cancellationToken) =>
        await _templates.GetAsync(templateId, cancellationToken)
        ?? throw new TemplateNotFoundException(templateId);

    public async Task<IReadOnlyList<TemplateVersionRecord>> ListVersionsAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        await GetTemplateAsync(templateId, cancellationToken);
        return await _versions.ListAsync(templateId, cancellationToken);
    }

    public async Task<TemplateVersionView> CreateAsync(
        TemplateCreateRequest request,
        string fileName,
        long fileLength,
        Stream file,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);
        ValidateFile(fileName, fileLength);
        if (await _templates.GetByCodeAsync(request.TemplateCode, cancellationToken) is not null)
        {
            throw new TemplatePersistenceException(
                "template_code_conflict",
                $"模板编码 {request.TemplateCode} 已存在。");
        }

        StoredFile stored = await StoreFileAsync(
            fileName,
            fileLength,
            file,
            actorUserId,
            cancellationToken);
        TemplateRecord template = await _templates.CreateAsync(
            request,
            actorUserId,
            cancellationToken);
        TemplateVersionRecord version = await _versions.CreateNextAsync(
            template.Id,
            stored.FileObjectId,
            actorUserId,
            cancellationToken);
        return await ParseAsync(version, cancellationToken);
    }

    public async Task<TemplateVersionView> UploadVersionAsync(
        ulong templateId,
        string fileName,
        long fileLength,
        Stream file,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        await GetTemplateAsync(templateId, cancellationToken);
        ValidateFile(fileName, fileLength);
        StoredFile stored = await StoreFileAsync(
            fileName,
            fileLength,
            file,
            actorUserId,
            cancellationToken);
        TemplateVersionRecord version = await _versions.CreateNextAsync(
            templateId,
            stored.FileObjectId,
            actorUserId,
            cancellationToken);
        return await ParseAsync(version, cancellationToken);
    }

    public async Task<TemplateVersionView> GetVersionAsync(
        ulong versionId,
        CancellationToken cancellationToken)
    {
        TemplateVersionRecord version = await _versions.GetAsync(versionId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{versionId}。");
        TemplateRecord template = await GetTemplateAsync(
            version.TemplateId,
            cancellationToken);
        FileObjectMetadata file = await _files.GetMetadataAsync(
            version.FileObjectId,
            cancellationToken)
            ?? throw new DatabaseFileException(
                "database_file_not_found",
                $"找不到模板版本文件：{version.FileObjectId}。");
        IReadOnlyList<TemplateElementRecord> elements =
            await _elements.ListAsync(versionId, cancellationToken);
        TemplateParseResult parseResult = DeserializeParseResult(version);
        return new TemplateVersionView
        {
            Template = template,
            Version = version,
            File = file,
            Elements = elements,
            ParseResult = parseResult,
        };
    }

    public async Task<TemplateVersionView> GetCurrentVersionAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        TemplateRecord template = await GetTemplateAsync(templateId, cancellationToken);
        TemplateVersionRecord? current = (await _versions.ListAsync(
                templateId,
                cancellationToken))
            .FirstOrDefault(item => item.VersionNo == template.CurrentVersionNo);
        if (current is null)
        {
            throw new TemplatePersistenceException(
                "template_version_not_found",
                $"模板 {templateId} 尚无可用版本。");
        }

        return await GetVersionAsync(current.Id, cancellationToken);
    }

    public async Task<TemplateVersionView> RescanAsync(
        ulong versionId,
        CancellationToken cancellationToken)
    {
        TemplateVersionRecord version = await _versions.GetAsync(versionId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{versionId}。");
        return await ParseAsync(version, cancellationToken);
    }

    public async Task<TemplateRecord> UpdateTemplateAsync(
        ulong templateId,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken)
    {
        TemplateRecord before = await GetTemplateAsync(templateId, cancellationToken);
        await _templates.UpdateAsync(templateId, request, cancellationToken);
        return await GetTemplateAsync(templateId, cancellationToken);
    }

    public async Task ArchiveTemplateAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        TemplateRecord before = await GetTemplateAsync(templateId, cancellationToken);
        await _templates.ArchiveAsync(templateId, cancellationToken);
    }

    public async Task RestoreTemplateAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        await _templates.RestoreAsync(templateId, cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateVersionRecord>> GetTemplateVersionsAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        await GetTemplateAsync(templateId, cancellationToken);
        return await _versions.ListAsync(templateId, cancellationToken);
    }

    private async Task<TemplateVersionView> ParseAsync(
        TemplateVersionRecord version,
        CancellationToken cancellationToken)
    {
        await _versions.UpdateParsingAsync(version.Id, cancellationToken);
        try
        {
            await using TemporaryFileLease lease =
                await _files.MaterializeTemporaryFileAsync(
                    version.FileObjectId,
                    cancellationToken);
            await using FileStream input = lease.OpenRead();
            TemplateScanResult scanResult = await _scanner.ScanAsync(
                input,
                cancellationToken);
            input.Position = 0;
            TemplateSegmentScanResult segmentScan =
                await _segmentScanner.ScanAsync(input, cancellationToken);
            IReadOnlyList<TemplateSegmentRecord> segments =
                await _segments.ReplaceForVersionAsync(
                    version.Id,
                    segmentScan.Segments,
                    actorUserId: null,
                    cancellationToken);
            List<TemplateElementRecord> elements = BuildElements(
                version.Id,
                scanResult,
                segmentScan,
                segments);
            await _elements.ReplaceAsync(version.Id, elements, cancellationToken);
            int unassignedMainDocumentElements = elements.Count(element =>
                element.SegmentId is null && IsMainDocumentElement(element));
            IReadOnlyList<TemplateParseWarning> segmentWarnings =
                segmentScan.Diagnostics.Select(item =>
                        new TemplateParseWarning(item.Code, item.Message))
                    .Concat(unassignedMainDocumentElements == 0
                        ? Array.Empty<TemplateParseWarning>()
                        : new[]
                        {
                            new TemplateParseWarning(
                                "SEGMENT_ELEMENT_UNASSIGNED",
                                $"{unassignedMainDocumentElements} 个正文模板元素不在任何有效片段范围内。"),
                        })
                    .ToList()
                    .AsReadOnly();

            TemplateParseResult parseResult = new()
            {
                ScanResult = scanResult,
                Warnings = scanResult.Warnings.Concat(segmentWarnings)
                    .ToList()
                    .AsReadOnly(),
                ImportSummary = new TemplateImportSummary
                {
                    UnresolvedPlaceholders = scanResult.MockItems
                        .Select(item => item.PlaceholderCandidatePath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToList()
                        .AsReadOnly(),
                    Warnings = scanResult.BindingManifest.Warnings,
                },
            };
            string json = JsonSerializer.Serialize(parseResult, JsonOptions);
            string status = parseResult.Warnings.Count == 0
                ? "READY"
                : "READY_WITH_WARNINGS";
            await _versions.CompleteAsync(
                version.Id,
                status,
                json,
                checked((uint)elements.Count),
                null,
                cancellationToken);
            return await GetVersionAsync(version.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string failureJson = JsonSerializer.Serialize(
                new
                {
                    warnings = new[]
                    {
                        new
                        {
                            code = "PARSE_FAILED",
                            message = SafeParseMessage(exception),
                        },
                    },
                },
                JsonOptions);
            await _versions.FailAsync(version.Id, failureJson, CancellationToken.None);
            throw;
        }
    }

    private List<TemplateElementRecord> BuildElements(
        ulong versionId,
        TemplateScanResult scanResult,
        TemplateSegmentScanResult segmentScan,
        IReadOnlyList<TemplateSegmentRecord> segmentRecords)
    {
        List<TemplateElementRecord> result =
            new(scanResult.MockItems.Count + scanResult.Charts.Count + scanResult.Tables.Count);
        int sort = 0;
        Dictionary<string, TemplateSegmentRecord> recordsByKey = segmentRecords
            .ToDictionary(item => item.SegmentKey, StringComparer.Ordinal);
        Dictionary<ulong, uint> localOrders = new();
        foreach (MockDataItem item in scanResult.MockItems)
        {
            TemplateSegmentRecord? assigned = FindTextSegment(
                item,
                segmentScan.Segments,
                recordsByKey);
            TemplateElementIdentity identity = _identityResolver.Resolve(
                item.LocatorId,
                item.Locator,
                item.PlaceholderCandidatePath,
                item.ContentControlTag);
            string[] allowedTypes = item.DataType switch
            {
                MockDataType.Decimal or MockDataType.Integer =>
                    new[] { "Integer", "Decimal" },
                _ => new[] { "String", "Integer", "Decimal", "Date", "Boolean" },
            };
            result.Add(new TemplateElementRecord
            {
                Id = 0,
                TemplateVersionId = versionId,
                SegmentId = assigned?.Id,
                ElementKey = identity.ElementKey,
                ElementType = "TEXT",
                LocatorType = "OPENXML_TEXT_RANGE",
                DisplayName = item.MockValue,
                LocatorJson = JsonSerializer.Serialize(
                    new
                    {
                        locatorId = item.LocatorId,
                        item.Locator.PartKind,
                        item.Locator.PartKey,
                        item.Locator.ParagraphIndex,
                        item.Locator.StartOffset,
                        item.Locator.Length,
                        item.Locator.OccurrenceIndex,
                        item.Locator.OriginalValue,
                        item.Locator.ContextHash,
                        item.RecognitionKind,
                        item.PlaceholderCandidatePath,
                        stableMarkerKey = identity.StableMarkerKey,
                        dataType = item.DataType,
                    },
                    JsonOptions),
                BindingSchemaJson = JsonSerializer.Serialize(
                    new
                    {
                        targetProperty = "$",
                        allowedTypes,
                        identityStrategy = identity.Strategy,
                    },
                    JsonOptions),
                DefaultValueJson = JsonSerializer.Serialize(item.MockValue, JsonOptions),
                IsRequired = false,
                SortNo = sort++,
                SegmentLocalOrder = NextLocalOrder(assigned?.Id, localOrders),
                ParseStatus = "VALID",
                ParseMessage = null,
            });
        }

        foreach (ChartTemplateItem chart in scanResult.Charts)
        {
            TemplateSegmentRecord? assigned = FindChartSegment(
                chart,
                segmentScan.Segments,
                recordsByKey);
            result.Add(new TemplateElementRecord
            {
                Id = 0,
                TemplateVersionId = versionId,
                SegmentId = assigned?.Id,
                ElementKey = $"chart:{chart.LocatorId}",
                ElementType = "CHART",
                LocatorType = "RELATIONSHIP",
                DisplayName = chart.Title,
                LocatorJson = JsonSerializer.Serialize(
                    new
                    {
                        locatorId = chart.LocatorId,
                        chart.Locator.PartKey,
                        chart.Locator.RelationshipId,
                        chart.Locator.DocumentOrder,
                    },
                    JsonOptions),
                BindingSchemaJson = JsonSerializer.Serialize(
                    new
                    {
                        targetProperty = "$",
                        allowedTypes = new[] { "Array" },
                        chart.ChartType,
                        chart.IsBindable,
                        dataDefinition = chart.DataDefinition,
                    },
                    JsonOptions),
                DefaultValueJson = null,
                IsRequired = false,
                SortNo = sort++,
                SegmentLocalOrder = NextLocalOrder(assigned?.Id, localOrders),
                ParseStatus = chart.IsBindable ? "VALID" : "UNSUPPORTED",
                ParseMessage = chart.IsBindable ? null : "图表没有可写的数据系列缓存。",
            });
        }

        foreach (TableTemplateItem table in scanResult.Tables)
        {
            TemplateSegmentRecord? assigned = FindTableSegment(
                table,
                segmentScan.Segments,
                recordsByKey);
            IReadOnlyList<TableColumnBinding> columns = table.Columns
                .Where(column => !string.IsNullOrWhiteSpace(column.SuggestedField))
                .Select(column => new TableColumnBinding
                {
                    ColumnIndex = column.ColumnIndex,
                    Header = column.Header,
                    SourceField = column.SuggestedField!,
                    FallbackValue = column.SuggestedField is "monitoringDisplay" ? "-" : null,
                })
                .ToList()
                .AsReadOnly();
            result.Add(new TemplateElementRecord
            {
                Id = 0,
                TemplateVersionId = versionId,
                SegmentId = assigned?.Id,
                ElementKey = $"table:{table.LocatorId}",
                ElementType = "TABLE",
                LocatorType = "OPENXML_TABLE",
                DisplayName = table.Title,
                LocatorJson = JsonSerializer.Serialize(
                    new
                    {
                        locatorId = table.LocatorId,
                        partKind = DocumentPartKind.MainDocument,
                        table.Locator.PartKey,
                        table.Locator.TableIndex,
                        table.Locator.FirstParagraphIndex,
                        table.Locator.HeaderSignature,
                    },
                    JsonOptions),
                BindingSchemaJson = JsonSerializer.Serialize(
                    new
                    {
                        targetProperty = "$",
                        allowedTypes = new[] { "Array" },
                        table.SuggestedSourcePath,
                        table.ContextLabel,
                        table.HeaderRowCount,
                        table.TemplateRowCount,
                        tableColumns = table.Columns,
                        columns,
                        filterField = string.Equals(
                            table.SuggestedSourcePath,
                            "majorColleges",
                            StringComparison.Ordinal)
                            ? "collegeName"
                            : null,
                        filterValue = string.Equals(
                            table.SuggestedSourcePath,
                            "majorColleges",
                            StringComparison.Ordinal)
                            ? table.ContextLabel
                            : null,
                    },
                    JsonOptions),
                DefaultValueJson = null,
                IsRequired = false,
                SortNo = sort++,
                SegmentLocalOrder = NextLocalOrder(assigned?.Id, localOrders),
                ParseStatus = table.IsBindable ? "VALID" : "UNSUPPORTED",
                ParseMessage = table.IsBindable ? null : "表格缺少可复制的数据行或有效列。",
            });
        }

        return result;
    }

    private static TemplateSegmentRecord? FindTextSegment(
        MockDataItem item,
        IReadOnlyList<TemplateSegmentDefinition> definitions,
        IReadOnlyDictionary<string, TemplateSegmentRecord> records)
    {
        if (item.Locator.PartKind == DocumentPartKind.TextBox)
        {
            string key = $"{item.Locator.PartKey}:{item.Locator.ParagraphIndex}";
            TemplateSegmentDefinition? textBoxDefinition = definitions
                .Where(segment => segment.TextBoxLocatorKeys.Contains(key))
                .OrderByDescending(segment => segment.Depth)
                .ThenBy(segment => segment.DocumentOrderEnd - segment.DocumentOrderStart)
                .FirstOrDefault();
            return textBoxDefinition is null
                ? null
                : records.GetValueOrDefault(textBoxDefinition.SegmentKey);
        }

        if (item.Locator.PartKind != DocumentPartKind.MainDocument)
        {
            return null;
        }

        TemplateSegmentDefinition? definition = definitions
            .Where(segment =>
                segment.MainDocumentParagraphIndexes.Contains(item.Locator.ParagraphIndex))
            .OrderByDescending(segment => segment.Depth)
            .ThenBy(segment => segment.DocumentOrderEnd - segment.DocumentOrderStart)
            .FirstOrDefault();
        return definition is null ? null : records.GetValueOrDefault(definition.SegmentKey);
    }

    private static TemplateSegmentRecord? FindChartSegment(
        ChartTemplateItem chart,
        IReadOnlyList<TemplateSegmentDefinition> definitions,
        IReadOnlyDictionary<string, TemplateSegmentRecord> records)
    {
        // OpenXmlChartReader currently scans chart references from MainDocumentPart
        // only. ChartLocator.PartKey identifies the ChartPart
        // (/word/charts/chartN.xml), while segment membership is determined by the
        // relationship ID on the drawing in /word/document.xml.
        TemplateSegmentDefinition? definition = definitions
            .Where(segment => segment.MainDocumentChartRelationshipIds.Contains(
                chart.Locator.RelationshipId))
            .OrderByDescending(segment => segment.Depth)
            .ThenBy(segment => segment.DocumentOrderEnd - segment.DocumentOrderStart)
            .FirstOrDefault();
        return definition is null ? null : records.GetValueOrDefault(definition.SegmentKey);
    }

    private static TemplateSegmentRecord? FindTableSegment(
        TableTemplateItem table,
        IReadOnlyList<TemplateSegmentDefinition> definitions,
        IReadOnlyDictionary<string, TemplateSegmentRecord> records)
    {
        TemplateSegmentDefinition? definition = definitions
            .Where(segment => segment.MainDocumentParagraphIndexes.Contains(
                table.Locator.FirstParagraphIndex))
            .OrderByDescending(segment => segment.Depth)
            .ThenBy(segment => segment.DocumentOrderEnd - segment.DocumentOrderStart)
            .FirstOrDefault();
        return definition is null ? null : records.GetValueOrDefault(definition.SegmentKey);
    }

    private static uint NextLocalOrder(
        ulong? segmentId,
        IDictionary<ulong, uint> localOrders)
    {
        if (segmentId is null)
        {
            return 0;
        }

        localOrders.TryGetValue(segmentId.Value, out uint value);
        localOrders[segmentId.Value] = value + 1;
        return value;
    }

    private static bool IsMainDocumentElement(TemplateElementRecord element)
    {
        try
        {
            using JsonDocument locator = JsonDocument.Parse(element.LocatorJson);
            JsonElement root = locator.RootElement;
            if (root.TryGetProperty("partKind", out JsonElement partKind))
            {
                return string.Equals(
                    partKind.GetString(),
                    nameof(DocumentPartKind.MainDocument),
                    StringComparison.OrdinalIgnoreCase);
            }

            return root.TryGetProperty("partKey", out JsonElement partKey) &&
                   string.Equals(
                       partKey.GetString(),
                       "/word/document.xml",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<StoredFile> StoreFileAsync(
        string fileName,
        long fileLength,
        Stream file,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        return await _files.StoreAsync(
            file,
            new FileStoreRequest
            {
                OriginalName = SanitizeFileName(fileName),
                MimeType = DocxMimeType,
                FileExtension = "docx",
                ExpectedLength = fileLength,
                BucketName = _fileOptions.BucketName,
                CreatedBy = actorUserId,
                MetadataJson = JsonSerializer.Serialize(
                    new { purpose = "TEMPLATE_VERSION" },
                    JsonOptions),
            },
            cancellationToken);
    }

    private TemplateParseResult DeserializeParseResult(TemplateVersionRecord version)
    {
        if (string.IsNullOrWhiteSpace(version.ParseResultJson))
        {
            throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {version.Id} 尚未完成解析。");
        }

        try
        {
            return JsonSerializer.Deserialize<TemplateParseResult>(
                       version.ParseResultJson,
                       JsonOptions)
                   ?? throw new JsonException("解析结果为空。");
        }
        catch (JsonException exception)
        {
            throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {version.Id} 的解析结果不可用。",
                exception);
        }
    }

    private void ValidateFile(string fileName, long length)
    {
        SanitizeFileName(fileName);
        if (length <= 0)
        {
            throw new InvalidTemplateFileException("上传的 DOCX 文件不能为空。");
        }

        long max = _options.MaxUploadSizeMb * 1024L * 1024L;
        if (length > max)
        {
            throw new TemplateTooLargeException(_options.MaxUploadSizeMb);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        string normalized = (fileName ?? string.Empty).Replace('\\', '/');
        string safe = Path.GetFileName(normalized).Trim();
        if (string.IsNullOrWhiteSpace(safe) ||
            !string.Equals(Path.GetExtension(safe), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidTemplateFileException("只允许上传扩展名为 .docx 的文件。");
        }

        return safe;
    }

    private static void ValidateCreateRequest(TemplateCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateCode) ||
            request.TemplateCode.Length > 64 ||
            !request.TemplateCode.All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new TemplatePersistenceException(
                "template_code_invalid",
                "模板编码只能包含字母、数字、下划线和连字符，且长度不能超过 64。");
        }

        if (string.IsNullOrWhiteSpace(request.TemplateName) ||
            request.TemplateName.Length > 255)
        {
            throw new TemplatePersistenceException(
                "template_name_invalid",
                "模板名称不能为空且长度不能超过 255。");
        }
    }

    private static string SafeParseMessage(Exception exception) => exception switch
    {
        WordTemplateBindingException business => business.Message,
        _ => "模板解析失败，请检查 DOCX 文件结构。",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}

#pragma warning restore CS1591
