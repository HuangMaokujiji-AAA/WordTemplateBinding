#pragma warning disable CS1591
using WordTemplateBinding.Core.Enums;

namespace WordTemplateBinding.Core.Models;

public sealed record FileStoreRequest
{
    public required string OriginalName { get; init; }
    public required string MimeType { get; init; }
    public required string FileExtension { get; init; }
    public required long ExpectedLength { get; init; }
    public string? ExpectedSha256 { get; init; }
    public string BucketName { get; init; } = "default";
    public string? MetadataJson { get; init; }
    public ulong? CreatedBy { get; init; }
}

public sealed record StoredFile
{
    public required ulong FileObjectId { get; init; }
    public required string ObjectKey { get; init; }
    public required long FileSize { get; init; }
    public required string Sha256 { get; init; }
    public required int ChunkSize { get; init; }
    public required int TotalChunks { get; init; }
}

public sealed record FileObjectMetadata
{
    public required ulong Id { get; init; }
    public required string ObjectKey { get; init; }
    public required string OriginalName { get; init; }
    public string? FileExtension { get; init; }
    public string? MimeType { get; init; }
    public required long FileSize { get; init; }
    public string? Sha256 { get; init; }
    public required int ChunkSize { get; init; }
    public required int TotalChunks { get; init; }
    public required string ObjectStatus { get; init; }
    public DateTimeOffset? UploadCompletedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}

public sealed class TemporaryFileLease : IAsyncDisposable
{
    private int _disposed;

    public TemporaryFileLease(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public FileStream OpenRead() =>
        new(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await Task.Yield();
        try
        {
            File.Delete(Path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

public sealed record TemplateRecord
{
    public required ulong Id { get; init; }
    public required string TemplateCode { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateType { get; init; }
    public string? CategoryCode { get; init; }
    public required string TemplateStatus { get; init; }
    public string? Description { get; init; }
    public required uint CurrentVersionNo { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required uint RowVersion { get; init; }
}

public sealed record TemplateVersionRecord
{
    public required ulong Id { get; init; }
    public required ulong TemplateId { get; init; }
    public required uint VersionNo { get; init; }
    public required ulong FileObjectId { get; init; }
    public required string VersionStatus { get; init; }
    public string? ParserName { get; init; }
    public string? ParserVersion { get; init; }
    public string? ParseResultJson { get; init; }
    public required uint ElementCount { get; init; }
    public string? StyleFingerprint { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record TemplateElementRecord
{
    public required ulong Id { get; init; }
    public required ulong TemplateVersionId { get; init; }
    public ulong? SegmentId { get; init; }
    public required string ElementKey { get; init; }
    public required string ElementType { get; init; }
    public required string LocatorType { get; init; }
    public string? DisplayName { get; init; }
    public required string LocatorJson { get; init; }
    public string? BindingSchemaJson { get; init; }
    public string? DefaultValueJson { get; init; }
    public required bool IsRequired { get; init; }
    public required int SortNo { get; init; }
    public uint SegmentLocalOrder { get; init; }
    public required string ParseStatus { get; init; }
    public string? ParseMessage { get; init; }
}

public sealed record TemplateSegmentRecord
{
    public required ulong Id { get; init; }
    public required ulong TemplateVersionId { get; init; }
    public ulong? ParentSegmentId { get; init; }
    public required string SegmentKey { get; init; }
    public required string SegmentName { get; init; }
    public required string SegmentType { get; init; }
    public required string AnchorType { get; init; }
    public required string StartAnchorJson { get; init; }
    public string? EndAnchorJson { get; init; }
    public required uint DocumentOrderStart { get; init; }
    public required uint DocumentOrderEnd { get; init; }
    public required string SegmentStatus { get; init; }
    public string? SegmentFingerprint { get; init; }
    public ulong? PreviewFileObjectId { get; init; }
    public required string PreviewStatus { get; init; }
    public string? PreviewErrorMessage { get; init; }
    public required int SortNo { get; init; }
    public required uint RowVersion { get; init; }
    public ulong? CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public ulong? UpdatedBy { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TemplateSegmentDefinition
{
    public required string SegmentKey { get; init; }
    public required string SegmentName { get; init; }
    public string? ParentSegmentKey { get; init; }
    public required string SegmentType { get; init; }
    public required string AnchorType { get; init; }
    public required string StartAnchorJson { get; init; }
    public string? EndAnchorJson { get; init; }
    public required uint DocumentOrderStart { get; init; }
    public required uint DocumentOrderEnd { get; init; }
    public required string SegmentStatus { get; init; }
    public required string SegmentFingerprint { get; init; }
    public required int SortNo { get; init; }
    public IReadOnlySet<int> MainDocumentParagraphIndexes { get; init; } =
        new HashSet<int>();
    public IReadOnlySet<string> MainDocumentChartRelationshipIds { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> TextBoxLocatorKeys { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
    public int Depth { get; init; }
}

public sealed record TemplateSegmentDiagnostic(
    string Code,
    string Level,
    string Message,
    string? SegmentKey = null);

public sealed record TemplateSegmentScanResult
{
    public required IReadOnlyList<TemplateSegmentDefinition> Segments { get; init; }
    public IReadOnlyList<TemplateSegmentDiagnostic> Diagnostics { get; init; } =
        Array.Empty<TemplateSegmentDiagnostic>();
}

public sealed record TemplateSegmentListItem
{
    public required TemplateSegmentRecord Segment { get; init; }
    public required int ElementCount { get; init; }
    public required int BoundCount { get; init; }
    public required int RequiredMissingCount { get; init; }
}

public sealed record TemplateOutlineBlock
{
    public required string BlockId { get; init; }
    public required string BlockType { get; init; }
    public required string DisplayText { get; init; }
    public string? SegmentKey { get; init; }
    public required bool CanSelect { get; init; }
    public required int Depth { get; init; }
    public IReadOnlyList<TemplateOutlineBlock> Children { get; init; } =
        Array.Empty<TemplateOutlineBlock>();
}

public sealed record TemplateSegmentOutline
{
    public required ulong TemplateVersionId { get; init; }
    public required string ContentHash { get; init; }
    public required IReadOnlyList<TemplateOutlineBlock> Blocks { get; init; }
}

public sealed record InsertTemplateSegmentBoundaryRequest
{
    public required string SegmentKey { get; init; }
    public required string SegmentName { get; init; }
    public required string StartBlockId { get; init; }
    public required string EndBlockId { get; init; }
    public required string ExpectedContentHash { get; init; }
}

public sealed record TemplateParseWarning(string Code, string Message);

public sealed record TemplateParseResult
{
    public required TemplateScanResult ScanResult { get; init; }
    public IReadOnlyList<TemplateParseWarning> Warnings { get; init; } =
        Array.Empty<TemplateParseWarning>();
    public TemplateImportSummary ImportSummary { get; init; } = TemplateImportSummary.Empty;
}

public sealed record TemplateCreateRequest
{
    public required string TemplateCode { get; init; }
    public required string TemplateName { get; init; }
    public string TemplateType { get; init; } = "SECTION";
    public string? CategoryCode { get; init; }
    public string? Description { get; init; }
}

public sealed record UpdateTemplateRequest
{
    public string? TemplateName { get; init; }
    public string? CategoryCode { get; init; }
    public string? Description { get; init; }
    public string? TemplateStatus { get; init; }
    public uint ExpectedRowVersion { get; init; }
}

public sealed record TemplateListQuery
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    long Total,
    int Page,
    int PageSize);

public sealed record TemplateVersionView
{
    public required TemplateRecord Template { get; init; }
    public required TemplateVersionRecord Version { get; init; }
    public required FileObjectMetadata File { get; init; }
    public required IReadOnlyList<TemplateElementRecord> Elements { get; init; }
    public required TemplateParseResult ParseResult { get; init; }
}

public sealed record ProjectRecord
{
    public required ulong Id { get; init; }
    public required string ProjectCode { get; init; }
    public required string ProjectName { get; init; }
    public string? Description { get; init; }
    public required string ProjectStatus { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required uint RowVersion { get; init; }
}

public sealed record ChapterRecord
{
    public required ulong Id { get; init; }
    public required ulong ProjectId { get; init; }
    public ulong? ParentId { get; init; }
    public required string ChapterCode { get; init; }
    public required string Title { get; init; }
    public required ushort LevelNo { get; init; }
    public required decimal SortKey { get; init; }
    public required string WorkflowStatus { get; init; }
    public required bool IsEnabled { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required uint RowVersion { get; init; }
}

public sealed record DataConnectionConfig(
    string Host,
    uint Port,
    string Database,
    string SslMode);

public sealed record DataConnectionCredential(string Username, string Password);

public sealed record DataConnectionRecord
{
    public required ulong Id { get; init; }
    public ulong? ProjectId { get; init; }
    public required string ConnectionName { get; init; }
    public required string ConnectionType { get; init; }
    public required DataConnectionConfig Config { get; init; }
    public string? CredentialRef { get; init; }
    public required string ConnectionStatus { get; init; }
    public DateTimeOffset? LastTestedAt { get; init; }
    public string? LastTestResultJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record DatabaseObjectInfo(
    string Schema,
    string ObjectName,
    string ObjectType);

public sealed record DatabaseColumnInfo
{
    public required string Schema { get; init; }
    public required string ObjectName { get; init; }
    public required string ColumnName { get; init; }
    public required string DatabaseType { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsPrimaryKey { get; init; }
    public string? Comment { get; init; }
    public required DataValueType DataType { get; init; }
    public required bool IsBindable { get; init; }
    public required int Ordinal { get; init; }
}

public sealed record DataSourceRecord
{
    public required ulong Id { get; init; }
    public required ulong ProjectId { get; init; }
    public required ulong ConnectionId { get; init; }
    public required string SourceCode { get; init; }
    public required string SourceName { get; init; }
    public required string SourceType { get; init; }
    public required string SourceStatus { get; init; }
    public required string SchemaName { get; init; }
    public required string ObjectType { get; init; }
    public required string ObjectName { get; init; }
    public string? SchemaJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record DataSnapshotRecord
{
    public required ulong Id { get; init; }
    public required ulong DataSourceId { get; init; }
    public required ulong SnapshotNo { get; init; }
    public required string SnapshotStatus { get; init; }
    public string? ContentJson { get; init; }
    public string? SchemaJson { get; init; }
    public string? ContentHash { get; init; }
    public ulong? RowCount { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record DataFieldRecord
{
    public required ulong Id { get; init; }
    public required ulong SnapshotId { get; init; }
    public required string FieldPath { get; init; }
    public required string FieldName { get; init; }
    public string? Comment { get; init; }
    public required DataValueType DataType { get; init; }
    public required bool IsArray { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsBindable { get; init; }
    public string? SampleValueJson { get; init; }
    public required int DisplayOrder { get; init; }
}

public sealed record DataSchemaContext(ulong DataSourceId, ulong? SnapshotId = null);

public sealed record DevelopmentDataSourceInitializationResult
{
    public required ulong ProjectId { get; init; }
    public required ulong DataSourceId { get; init; }
    public required ulong SnapshotId { get; init; }
    public required int FieldCount { get; init; }
    public required bool Created { get; init; }
    public required bool Refreshed { get; init; }
}

public sealed record BindingSetRecord
{
    public required ulong Id { get; init; }
    public required ulong ChapterId { get; init; }
    public required uint VersionNo { get; init; }
    public required ulong TemplateVersionId { get; init; }
    public required string BindingStatus { get; init; }
    public required string ValidationStatus { get; init; }
    public string? ValidationResultJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record BindingItemRecord
{
    public required ulong Id { get; init; }
    public required ulong BindingSetId { get; init; }
    public required ulong TemplateElementId { get; init; }
    public required string TargetProperty { get; init; }
    public required string SourceKind { get; init; }
    public ulong? DataSourceId { get; init; }
    public string? SourcePath { get; init; }
    public string? TransformConfigJson { get; init; }
    public string? FormatConfigJson { get; init; }
    public string? FallbackValueJson { get; init; }
    public required bool IsRequired { get; init; }
    public required int SortNo { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record BindingItemUpsert
{
    public string TargetProperty { get; init; } = "$";
    public string SourceKind { get; init; } = "DATA_SOURCE";
    public required ulong DataSourceId { get; init; }
    public required string SourcePath { get; init; }
    public string? TransformConfigJson { get; init; }
    public string? FormatConfigJson { get; init; }
    public string? FallbackValueJson { get; init; }
    public bool IsRequired { get; init; }
}

public sealed record BindingValidationItem(
    string Code,
    string Level,
    ulong? TemplateElementId,
    string Message);

public sealed record BindingValidationSummary(
    int ElementCount,
    int BoundCount,
    int RequiredUnboundCount,
    int InvalidBindingCount,
    int WarningCount);

public sealed record BindingValidationResult
{
    public required string Status { get; init; }
    public required BindingValidationSummary Summary { get; init; }
    public required IReadOnlyList<BindingValidationItem> Items { get; init; }
}

public sealed record BindingSuggestion(
    string FieldPath,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record BindingPreview
{
    public required ulong TemplateElementId { get; init; }
    public required string DisplayName { get; init; }
    public required string SourcePath { get; init; }
    public string? RawValueJson { get; init; }
    public string? FormattedValue { get; init; }
    public required DataValueType DataType { get; init; }
    public required ulong SnapshotId { get; init; }
}

public sealed record TemplateElementIdentity(
    string ElementKey,
    string Strategy,
    string? StableMarkerKey);

#pragma warning restore CS1591
