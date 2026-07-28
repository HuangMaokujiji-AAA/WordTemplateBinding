#pragma warning disable CS1591
using System.Text.Json;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;

public sealed class TemplateSegmentService
{
    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly ITemplateSegmentRepository _segments;
    private readonly ITemplateElementRepository _elements;
    private readonly ITemplateVersionRepository _versions;
    private readonly IBindingSetRepository _bindingSets;
    private readonly IBindingItemRepository _bindingItems;
    private readonly IFileStorageService _files;
    private readonly IWordSegmentPreviewRenderer _renderer;
    private readonly IWordTemplateSegmentEditor _editor;
    private readonly TemplateCatalogService _catalog;
    private readonly DatabaseFileStorageOptions _fileOptions;

    public TemplateSegmentService(
        ITemplateSegmentRepository segments,
        ITemplateElementRepository elements,
        ITemplateVersionRepository versions,
        IBindingSetRepository bindingSets,
        IBindingItemRepository bindingItems,
        IFileStorageService files,
        IWordSegmentPreviewRenderer renderer,
        IWordTemplateSegmentEditor editor,
        TemplateCatalogService catalog,
        DatabaseFileStorageOptions fileOptions)
    {
        _segments = segments;
        _elements = elements;
        _versions = versions;
        _bindingSets = bindingSets;
        _bindingItems = bindingItems;
        _files = files;
        _renderer = renderer;
        _editor = editor;
        _catalog = catalog;
        _fileOptions = fileOptions;
    }

    public async Task<IReadOnlyList<TemplateSegmentListItem>> ListAsync(
        ulong templateVersionId,
        ulong? bindingSetId,
        CancellationToken cancellationToken)
    {
        _ = await _versions.GetAsync(templateVersionId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{templateVersionId}。");
        IReadOnlyList<TemplateSegmentRecord> segments =
            await _segments.ListAsync(templateVersionId, cancellationToken);
        IReadOnlyList<TemplateElementRecord> elements =
            await _elements.ListAsync(templateVersionId, cancellationToken);
        if (segments.Count > 0 &&
            elements.Any(element =>
                element.SegmentId is null &&
                string.Equals(
                    element.ElementType,
                    "CHART",
                    StringComparison.OrdinalIgnoreCase)))
        {
            // 修复旧版片段归属逻辑留下的未归属主文档图表。重扫只更新解析元数据，
            // 原始不可变 DOCX 字节保持不变。
            await _catalog.RescanAsync(templateVersionId, cancellationToken);
            segments = await _segments.ListAsync(
                templateVersionId,
                cancellationToken);
            elements = await _elements.ListAsync(
                templateVersionId,
                cancellationToken);
        }

        if (segments.Count == 0)
        {
            TemplateVersionRecord version =
                await _versions.GetAsync(templateVersionId, cancellationToken)
                ?? throw new TemplatePersistenceException(
                    "template_version_not_found",
                    $"找不到模板版本：{templateVersionId}。");
            FileObjectMetadata? file =
                await _files.GetMetadataAsync(version.FileObjectId, cancellationToken);
            TemplateSegmentDefinition fallback = new()
            {
                SegmentKey = "full-document",
                SegmentName = "完整模板",
                SegmentType = "ROOT",
                AnchorType = "VIRTUAL",
                StartAnchorJson =
                    """{"partKind":"MainDocument","partKey":"/word/document.xml","locatorType":"VIRTUAL"}""",
                DocumentOrderStart = 0,
                DocumentOrderEnd = checked((uint)Math.Max(0, elements.Count - 1)),
                SegmentStatus = "READY",
                SegmentFingerprint = file?.Sha256 ??
                    version.StyleFingerprint ??
                    new string('0', 64),
                SortNo = 0,
            };
            segments = await _segments.ReplaceForVersionAsync(
                templateVersionId,
                new[] { fallback },
                actorUserId: null,
                cancellationToken);
            TemplateSegmentRecord root = segments.Single();
            uint localOrder = 0;
            elements = elements.Select(element =>
                IsMainDocumentElement(element)
                    ? element with
                    {
                        SegmentId = root.Id,
                        SegmentLocalOrder = localOrder++,
                    }
                    : element with
                    {
                        SegmentId = null,
                        SegmentLocalOrder = 0,
                    })
                .ToList()
                .AsReadOnly();
            await _elements.ReplaceAsync(
                templateVersionId,
                elements,
                cancellationToken);
        }
        HashSet<ulong> boundElementIds = new();
        if (bindingSetId is not null)
        {
            BindingSetRecord? bindingSet =
                await _bindingSets.GetAsync(bindingSetId.Value, cancellationToken);
            if (bindingSet?.TemplateVersionId == templateVersionId)
            {
                boundElementIds = (await _bindingItems.ListAsync(
                        bindingSetId.Value,
                        cancellationToken))
                    .Select(item => item.TemplateElementId)
                    .ToHashSet();
            }
        }
        return segments.Select(segment =>
        {
            List<TemplateElementRecord> scoped = elements
                .Where(element => element.SegmentId == segment.Id)
                .ToList();
            return new TemplateSegmentListItem
            {
                Segment = segment,
                ElementCount = scoped.Count,
                BoundCount = scoped.Count(element => boundElementIds.Contains(element.Id)),
                RequiredMissingCount = scoped.Count(element =>
                    element.IsRequired && !boundElementIds.Contains(element.Id)),
            };
        }).ToList().AsReadOnly();
    }

    public async Task<TemplateSegmentRecord> GetAsync(
        ulong segmentId,
        CancellationToken cancellationToken) =>
        await _segments.GetAsync(segmentId, cancellationToken)
        ?? throw new TemplatePersistenceException(
            "template_segment_not_found",
            $"找不到模板片段：{segmentId}。");

    public async Task<IReadOnlyList<TemplateElementRecord>> ListElementsAsync(
        ulong segmentId,
        CancellationToken cancellationToken)
    {
        TemplateSegmentRecord segment = await GetAsync(segmentId, cancellationToken);
        return (await _elements.ListAsync(segment.TemplateVersionId, cancellationToken))
            .Where(element => element.SegmentId == segmentId)
            .OrderBy(element => element.SegmentLocalOrder)
            .ThenBy(element => element.Id)
            .ToList()
            .AsReadOnly();
    }

    public async Task<(ulong FileObjectId, string FileName)> GetOrCreatePreviewAsync(
        ulong segmentId,
        CancellationToken cancellationToken)
    {
        TemplateSegmentRecord segment = await GetAsync(segmentId, cancellationToken);
        if (segment.PreviewFileObjectId is not null &&
            string.Equals(segment.PreviewStatus, "READY", StringComparison.OrdinalIgnoreCase) &&
            await _files.GetMetadataAsync(segment.PreviewFileObjectId.Value, cancellationToken)
                is not null)
        {
            return (segment.PreviewFileObjectId.Value, $"{segment.SegmentKey}-preview.docx");
        }

        TemplateVersionRecord version =
            await _versions.GetAsync(segment.TemplateVersionId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{segment.TemplateVersionId}。");
        await _segments.SetPreviewAsync(
            segmentId, null, "GENERATING", null, cancellationToken);
        try
        {
            await using TemporaryFileLease lease =
                await _files.MaterializeTemporaryFileAsync(
                    version.FileObjectId,
                    cancellationToken);
            await using Stream preview =
                await _renderer.RenderAsync(lease.Path, segment, cancellationToken);
            StoredFile stored = await _files.StoreAsync(
                preview,
                new FileStoreRequest
                {
                    OriginalName = $"{segment.SegmentKey}-preview.docx",
                    MimeType = DocxMimeType,
                    FileExtension = "docx",
                    ExpectedLength = preview.Length,
                    BucketName = _fileOptions.BucketName,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        purpose = "TEMPLATE_SEGMENT_PREVIEW",
                        segmentId = segment.Id.ToString(),
                        segment.SegmentFingerprint,
                        rendererVersion = "1",
                    }),
                },
                cancellationToken);
            await _segments.SetPreviewAsync(
                segmentId, stored.FileObjectId, "READY", null, cancellationToken);
            return (stored.FileObjectId, $"{segment.SegmentKey}-preview.docx");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _segments.SetPreviewAsync(
                segmentId,
                null,
                "FAILED",
                exception.Message.Length <= 1000
                    ? exception.Message
                    : exception.Message[..1000],
                CancellationToken.None);
            throw new TemplatePersistenceException(
                "segment_preview_failed",
                $"片段预览生成失败：{exception.Message}",
                exception);
        }
    }

    public async Task<TemplateSegmentOutline> GetOutlineAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        (TemplateVersionRecord version, FileObjectMetadata file) =
            await GetVersionFileAsync(templateVersionId, cancellationToken);
        await using TemporaryFileLease lease =
            await _files.MaterializeTemporaryFileAsync(
                version.FileObjectId,
                cancellationToken);
        IReadOnlyList<TemplateOutlineBlock> blocks =
            await _editor.ReadOutlineAsync(lease.Path, cancellationToken);
        return new TemplateSegmentOutline
        {
            TemplateVersionId = templateVersionId,
            ContentHash = file.Sha256 ??
                throw new TemplatePersistenceException(
                    "template_content_hash_missing",
                    "模板文件缺少内容哈希，不能安全编辑边界。"),
            Blocks = blocks,
        };
    }

    public async Task<TemplateVersionView> InsertBoundaryAsync(
        ulong templateVersionId,
        InsertTemplateSegmentBoundaryRequest request,
        CancellationToken cancellationToken)
    {
        (TemplateVersionRecord version, FileObjectMetadata file) =
            await GetVersionFileAsync(templateVersionId, cancellationToken);
        EnsureExpectedHash(file, request.ExpectedContentHash);
        try
        {
            await using TemporaryFileLease lease =
                await _files.MaterializeTemporaryFileAsync(
                    version.FileObjectId,
                    cancellationToken);
            await using Stream edited = await _editor.InsertBoundaryAsync(
                lease.Path,
                request,
                cancellationToken);
            return await _catalog.UploadVersionAsync(
                version.TemplateId,
                BuildEditedFileName(file.OriginalName),
                edited.Length,
                edited,
                actorUserId: null,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new TemplatePersistenceException(
                "segment_boundary_invalid",
                exception.Message,
                exception);
        }
    }

    public async Task<TemplateVersionView> SaveBoundariesAsync(
        ulong templateVersionId,
        SaveTemplateSegmentBoundariesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Boundaries is null ||
            request.Boundaries.Count is < 1 or > 50)
        {
            throw new TemplatePersistenceException(
                "segment_boundary_batch_invalid",
                "一次必须保存 1～50 个片段边界。");
        }

        (TemplateVersionRecord version, FileObjectMetadata file) =
            await GetVersionFileAsync(templateVersionId, cancellationToken);
        EnsureExpectedHash(file, request.ExpectedContentHash);
        IReadOnlyList<InsertTemplateSegmentBoundaryRequest> boundaries =
            request.Boundaries
                .Select(boundary => new InsertTemplateSegmentBoundaryRequest
                {
                    SegmentKey = boundary.SegmentKey,
                    SegmentName = boundary.SegmentName,
                    StartBlockId = boundary.StartBlockId,
                    EndBlockId = boundary.EndBlockId,
                    ExpectedContentHash = request.ExpectedContentHash,
                })
                .ToList()
                .AsReadOnly();
        try
        {
            await using TemporaryFileLease lease =
                await _files.MaterializeTemporaryFileAsync(
                    version.FileObjectId,
                    cancellationToken);
            await using Stream edited = await _editor.InsertBoundariesAsync(
                lease.Path,
                boundaries,
                cancellationToken);
            return await _catalog.UploadVersionAsync(
                version.TemplateId,
                BuildEditedFileName(file.OriginalName),
                edited.Length,
                edited,
                actorUserId: null,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new TemplatePersistenceException(
                "segment_boundary_invalid",
                exception.Message,
                exception);
        }
    }

    public async Task<TemplateVersionView> RemoveBoundaryAsync(
        ulong templateVersionId,
        string segmentKey,
        string expectedContentHash,
        CancellationToken cancellationToken)
    {
        (TemplateVersionRecord version, FileObjectMetadata file) =
            await GetVersionFileAsync(templateVersionId, cancellationToken);
        EnsureExpectedHash(file, expectedContentHash);
        try
        {
            await using TemporaryFileLease lease =
                await _files.MaterializeTemporaryFileAsync(
                    version.FileObjectId,
                    cancellationToken);
            await using Stream edited = await _editor.RemoveBoundaryAsync(
                lease.Path,
                segmentKey,
                cancellationToken);
            return await _catalog.UploadVersionAsync(
                version.TemplateId,
                BuildEditedFileName(file.OriginalName),
                edited.Length,
                edited,
                actorUserId: null,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new TemplatePersistenceException(
                "segment_boundary_invalid",
                exception.Message,
                exception);
        }
    }

    private async Task<(TemplateVersionRecord Version, FileObjectMetadata File)>
        GetVersionFileAsync(
            ulong templateVersionId,
            CancellationToken cancellationToken)
    {
        TemplateVersionRecord version =
            await _versions.GetAsync(templateVersionId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{templateVersionId}。");
        FileObjectMetadata file =
            await _files.GetMetadataAsync(version.FileObjectId, cancellationToken)
            ?? throw new DatabaseFileException(
                "database_file_not_found",
                $"找不到模板文件：{version.FileObjectId}。");
        return (version, file);
    }

    private static void EnsureExpectedHash(
        FileObjectMetadata file,
        string expectedContentHash)
    {
        if (string.IsNullOrWhiteSpace(file.Sha256) ||
            !string.Equals(
                file.Sha256,
                expectedContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new TemplatePersistenceException(
                "segment_fingerprint_changed",
                "模板版本内容已变化，请刷新边界结构后重试。");
        }
    }

    private static string BuildEditedFileName(string originalName)
    {
        string stem = Path.GetFileNameWithoutExtension(originalName);
        return $"{stem}-segments-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.docx";
    }

    private static bool IsMainDocumentElement(TemplateElementRecord element)
    {
        // 当前图表扫描器只从 MainDocumentPart 读取图表引用；图表 locator 的
        // partKey 是 ChartPart 路径而不是 /word/document.xml。
        if (string.Equals(
                element.ElementType,
                "CHART",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using JsonDocument locator = JsonDocument.Parse(element.LocatorJson);
            JsonElement root = locator.RootElement;
            if (root.TryGetProperty("partKind", out JsonElement partKind))
            {
                return string.Equals(
                    partKind.GetString(),
                    "MainDocument",
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
}

#pragma warning restore CS1591
