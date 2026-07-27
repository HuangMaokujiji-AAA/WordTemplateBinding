#pragma warning disable CS1591
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Api.Contracts;

internal sealed record CreateProjectRequest(
    string ProjectCode,
    string ProjectName,
    string? Description,
    bool CreateDefaultChapter = true,
    bool InitializeMockDataSource = true);

internal sealed record UpdateProjectRequest(
    string ProjectName,
    string? Description,
    string? ProjectStatus,
    uint RowVersion);

internal sealed record ArchiveProjectRequest(uint RowVersion);

internal sealed record CreateChapterRequest(
    string ChapterCode,
    string Title,
    string? ParentId,
    decimal SortKey = 0);

internal sealed record UpdateChapterRequest(
    string ChapterCode,
    string Title,
    uint RowVersion);

internal sealed record ChapterOrderItem(
    string ChapterId,
    string? ParentId,
    decimal SortKey);

internal sealed record DevDataSourceInitRequest(bool ForceRefresh = false);

internal sealed record CreateDataConnectionRequest(
    string? ProjectId,
    string ConnectionName,
    string ConnectionType,
    DataConnectionConfig Config,
    string? CredentialRef);

internal sealed record CreateDataSourceRequest(
    string ProjectId,
    string ConnectionId,
    string SourceCode,
    string SourceName,
    string SourceType,
    string SchemaName,
    string ObjectType,
    string ObjectName);

internal sealed record BulkImportScevl2024Request(
    string ConnectionId,
    string SchemaName = "scevl2024",
    string ObjectNamePrefix = "data_专业监测_",
    string SourceCodePrefix = "scevl2024_");

internal sealed record CreateBindingSetRequest(
    string ChapterId,
    string TemplateVersionId);

internal sealed record UpsertBindingItemRequest(
    string DataSourceId,
    string SourcePath,
    string TargetProperty = "$",
    string SourceKind = "DATA_SOURCE",
    string? TransformConfigJson = null,
    string? FormatConfigJson = null,
    string? FallbackValueJson = null,
    bool IsRequired = false);

internal static class PersistentApiMapper
{
    internal static object Template(TemplateRecord value) => new
    {
        id = value.Id.ToString(),
        value.TemplateCode,
        value.TemplateName,
        value.TemplateType,
        value.CategoryCode,
        value.TemplateStatus,
        value.Description,
        value.CurrentVersionNo,
        value.CreatedAt,
        value.UpdatedAt,
        value.RowVersion,
    };

    internal static object Version(TemplateVersionRecord value) => new
    {
        id = value.Id.ToString(),
        templateId = value.TemplateId.ToString(),
        value.VersionNo,
        fileObjectId = value.FileObjectId.ToString(),
        value.VersionStatus,
        value.ParserName,
        value.ParserVersion,
        value.ElementCount,
        value.StyleFingerprint,
        value.CreatedAt,
    };

    internal static object Element(TemplateElementRecord value) => new
    {
        id = value.Id.ToString(),
        templateVersionId = value.TemplateVersionId.ToString(),
        segmentId = value.SegmentId?.ToString(),
        value.ElementKey,
        value.ElementType,
        value.LocatorType,
        value.DisplayName,
        locator = ParseJson(value.LocatorJson),
        bindingSchema = ParseJson(value.BindingSchemaJson),
        defaultValue = ParseJson(value.DefaultValueJson),
        value.IsRequired,
        value.SortNo,
        value.SegmentLocalOrder,
        value.ParseStatus,
        value.ParseMessage,
    };

    internal static object Segment(TemplateSegmentListItem value) => new
    {
        id = value.Segment.Id.ToString(),
        templateVersionId = value.Segment.TemplateVersionId.ToString(),
        parentSegmentId = value.Segment.ParentSegmentId?.ToString(),
        value.Segment.SegmentKey,
        value.Segment.SegmentName,
        value.Segment.SegmentType,
        value.Segment.AnchorType,
        value.Segment.DocumentOrderStart,
        value.Segment.DocumentOrderEnd,
        value.Segment.SegmentStatus,
        value.Segment.PreviewStatus,
        value.Segment.PreviewErrorMessage,
        value.Segment.SortNo,
        value.ElementCount,
        bindingProgress = new
        {
            total = value.ElementCount,
            bound = value.BoundCount,
            requiredMissing = value.RequiredMissingCount,
        },
        value.Segment.RowVersion,
    };

    internal static object SegmentDetail(TemplateSegmentRecord value) => new
    {
        id = value.Id.ToString(),
        templateVersionId = value.TemplateVersionId.ToString(),
        parentSegmentId = value.ParentSegmentId?.ToString(),
        value.SegmentKey,
        value.SegmentName,
        value.SegmentType,
        value.AnchorType,
        startAnchor = ParseJson(value.StartAnchorJson),
        endAnchor = ParseJson(value.EndAnchorJson),
        value.DocumentOrderStart,
        value.DocumentOrderEnd,
        value.SegmentStatus,
        value.SegmentFingerprint,
        previewFileObjectId = value.PreviewFileObjectId?.ToString(),
        value.PreviewStatus,
        value.PreviewErrorMessage,
        value.SortNo,
        value.RowVersion,
    };

    internal static object SegmentOutline(TemplateSegmentOutline value) => new
    {
        templateVersionId = value.TemplateVersionId.ToString(),
        value.ContentHash,
        blocks = value.Blocks.Select(OutlineBlock),
    };

    private static object OutlineBlock(TemplateOutlineBlock value) => new
    {
        value.BlockId,
        value.BlockType,
        value.DisplayText,
        value.SegmentKey,
        value.CanSelect,
        value.Depth,
        children = value.Children.Select(OutlineBlock),
    };

    internal static object VersionView(TemplateVersionView value) => new
    {
        template = Template(value.Template),
        version = Version(value.Version),
        file = new
        {
            id = value.File.Id.ToString(),
            value.File.OriginalName,
            value.File.MimeType,
            value.File.FileSize,
            value.File.Sha256,
            value.File.ObjectStatus,
        },
        elements = value.Elements.Select(Element),
        parseResult = value.ParseResult,
    };

    internal static object Project(ProjectRecord value) => new
    {
        projectId = value.Id.ToString(),
        value.ProjectCode,
        value.ProjectName,
        value.Description,
        value.ProjectStatus,
        value.CreatedAt,
        value.UpdatedAt,
        value.RowVersion,
    };

    internal static object Chapter(ChapterRecord value) => new
    {
        id = value.Id.ToString(),
        projectId = value.ProjectId.ToString(),
        parentId = value.ParentId?.ToString(),
        value.ChapterCode,
        value.Title,
        value.LevelNo,
        value.SortKey,
        value.WorkflowStatus,
        value.IsEnabled,
        value.CreatedAt,
    };

    internal static object Connection(DataConnectionRecord value) => new
    {
        id = value.Id.ToString(),
        projectId = value.ProjectId?.ToString(),
        value.ConnectionName,
        value.ConnectionType,
        value.Config,
        value.CredentialRef,
        value.ConnectionStatus,
        value.LastTestedAt,
        lastTestResult = ParseJson(value.LastTestResultJson),
        value.CreatedAt,
    };

    internal static object DataSource(DataSourceRecord value) => new
    {
        id = value.Id.ToString(),
        projectId = value.ProjectId.ToString(),
        connectionId = value.ConnectionId.ToString(),
        value.SourceCode,
        value.SourceName,
        value.SourceType,
        value.SourceStatus,
        value.SchemaName,
        value.ObjectType,
        value.ObjectName,
        schema = ParseJson(value.SchemaJson),
        value.CreatedAt,
    };

    internal static object Snapshot(DataSnapshotRecord value) => new
    {
        id = value.Id.ToString(),
        dataSourceId = value.DataSourceId.ToString(),
        value.SnapshotNo,
        value.SnapshotStatus,
        content = ParseJson(value.ContentJson),
        schema = ParseJson(value.SchemaJson),
        value.ContentHash,
        value.RowCount,
        value.CapturedAt,
        value.ErrorMessage,
    };

    internal static object Field(DataFieldRecord value) => new
    {
        id = value.Id.ToString(),
        snapshotId = value.SnapshotId.ToString(),
        value.FieldPath,
        value.FieldName,
        value.Comment,
        dataType = value.DataType.ToString(),
        value.IsArray,
        value.IsNullable,
        value.IsBindable,
        sampleValue = ParseJson(value.SampleValueJson),
        value.DisplayOrder,
    };

    internal static object BulkImport(DataSourceBulkImportResult value) => new
    {
        objectNamePrefix = value.ObjectNamePrefix,
        value.Created,
        value.Skipped,
        value.Failed,
        items = value.Items.Select(item => new
        {
            item.ObjectName,
            item.Status,
            item.Message,
            dataSourceId = item.DataSourceId,
        }),
    };

    internal static object BindingSet(BindingSetRecord value) => new
    {
        id = value.Id.ToString(),
        chapterId = value.ChapterId.ToString(),
        value.VersionNo,
        templateVersionId = value.TemplateVersionId.ToString(),
        value.BindingStatus,
        value.ValidationStatus,
        validationResult = ParseJson(value.ValidationResultJson),
        value.CreatedAt,
    };

    internal static object BindingItem(BindingItemRecord value) => new
    {
        id = value.Id.ToString(),
        bindingSetId = value.BindingSetId.ToString(),
        templateElementId = value.TemplateElementId.ToString(),
        value.TargetProperty,
        value.SourceKind,
        dataSourceId = value.DataSourceId?.ToString(),
        value.SourcePath,
        transformConfig = ParseJson(value.TransformConfigJson),
        formatConfig = ParseJson(value.FormatConfigJson),
        fallbackValue = ParseJson(value.FallbackValueJson),
        value.IsRequired,
        value.SortNo,
        value.CreatedAt,
        value.UpdatedAt,
    };

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    }
}

#pragma warning restore CS1591
