#pragma warning disable CS1591
using System.Data.Common;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

public interface IFileStorageService
{
    Task<StoredFile> StoreAsync(
        Stream source,
        FileStoreRequest request,
        CancellationToken cancellationToken = default);

    Task<FileObjectMetadata?> GetMetadataAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default);

    Task CopyToAsync(
        ulong fileObjectId,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<TemporaryFileLease> MaterializeTemporaryFileAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default);

    Task VerifyAsync(
        ulong fileObjectId,
        CancellationToken cancellationToken = default);
}

public interface ITemplateRepository
{
    Task<TemplateRecord> CreateAsync(
        TemplateCreateRequest request,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<TemplateRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<TemplateRecord?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<PagedResult<TemplateRecord>> ListAsync(
        TemplateListQuery query,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        ulong templateId,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken);

    Task<bool> ArchiveAsync(
        ulong templateId,
        CancellationToken cancellationToken);

    Task<bool> RestoreAsync(
        ulong templateId,
        CancellationToken cancellationToken);
}

public interface ITemplateVersionRepository
{
    Task<TemplateVersionRecord> CreateNextAsync(
        ulong templateId,
        ulong fileObjectId,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<TemplateVersionRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TemplateVersionRecord>> ListAsync(
        ulong templateId,
        CancellationToken cancellationToken);

    Task UpdateParsingAsync(ulong id, CancellationToken cancellationToken);

    Task CompleteAsync(
        ulong id,
        string status,
        string parseResultJson,
        uint elementCount,
        string? styleFingerprint,
        CancellationToken cancellationToken);

    Task FailAsync(
        ulong id,
        string parseResultJson,
        CancellationToken cancellationToken);
}

public interface ITemplateElementRepository
{
    Task ReplaceAsync(
        ulong templateVersionId,
        IReadOnlyList<TemplateElementRecord> elements,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TemplateElementRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken);

    Task<TemplateElementRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
}

public interface ITemplateSegmentRepository
{
    Task<IReadOnlyList<TemplateSegmentRecord>> ReplaceForVersionAsync(
        ulong templateVersionId,
        IReadOnlyList<TemplateSegmentDefinition> segments,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TemplateSegmentRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken);

    Task<TemplateSegmentRecord?> GetAsync(
        ulong segmentId,
        CancellationToken cancellationToken);

    Task SetPreviewAsync(
        ulong segmentId,
        ulong? previewFileObjectId,
        string previewStatus,
        string? errorMessage,
        CancellationToken cancellationToken);
}

public interface IProjectRepository
{
    Task<ProjectRecord> CreateAsync(
        string code,
        string name,
        string? description,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<ProjectRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<ProjectRecord?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<PagedResult<ProjectRecord>> ListAsync(
        string? query,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        ulong projectId,
        string name,
        string? description,
        string? status,
        uint expectedRowVersion,
        CancellationToken cancellationToken);

    Task<bool> ArchiveAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken);

    Task<bool> RestoreAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken);
}

public interface IChapterRepository
{
    Task<ChapterRecord> CreateAsync(
        ulong projectId,
        string code,
        string title,
        ulong? parentId,
        decimal sortKey,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<ChapterRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChapterRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        ulong chapterId,
        string code,
        string title,
        uint expectedRowVersion,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        ulong chapterId,
        uint expectedRowVersion,
        CancellationToken cancellationToken);

    Task<bool> ReorderAsync(
        ulong projectId,
        IReadOnlyList<(ulong ChapterId, ulong? ParentId, decimal SortKey)> items,
        CancellationToken cancellationToken);

    Task<int> CountAsync(
        ulong projectId,
        CancellationToken cancellationToken);

    Task<bool> HasChildrenAsync(
        ulong chapterId,
        CancellationToken cancellationToken);
}

public interface IDataConnectionRepository
{
    Task<DataConnectionRecord> CreateAsync(
        DataConnectionRecord connection,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<DataConnectionRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataConnectionRecord>> ListAsync(
        ulong? projectId,
        CancellationToken cancellationToken);

    Task UpdateTestResultAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken);
}

public interface IDataSourceRepository
{
    Task<DataSourceRecord> CreateAsync(
        DataSourceRecord source,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<DataSourceRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataSourceRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken);

    Task UpdateSchemaAsync(
        ulong id,
        string schemaJson,
        CancellationToken cancellationToken);
}

public interface IDataSnapshotRepository
{
    Task<DataSnapshotRecord> StartAsync(
        ulong dataSourceId,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        ulong snapshotId,
        string contentJson,
        string schemaJson,
        string contentHash,
        ulong rowCount,
        CancellationToken cancellationToken);

    Task FailAsync(
        ulong snapshotId,
        string safeMessage,
        CancellationToken cancellationToken);

    Task<DataSnapshotRecord?> GetAsync(ulong id, CancellationToken cancellationToken);
    Task<DataSnapshotRecord?> GetLatestReadyAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken);
}

public interface IDataFieldRepository
{
    Task ReplaceAsync(
        ulong snapshotId,
        IReadOnlyList<DataFieldRecord> fields,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DataFieldRecord>> ListAsync(
        ulong snapshotId,
        string? query,
        int limit,
        CancellationToken cancellationToken);

    Task<DataFieldRecord?> FindAsync(
        ulong snapshotId,
        string path,
        CancellationToken cancellationToken);
}

public interface IBindingSetRepository
{
    Task<BindingSetRecord> GetOrCreateDraftAsync(
        ulong chapterId,
        ulong templateVersionId,
        ulong? actorUserId,
        CancellationToken cancellationToken);

    Task<BindingSetRecord?> GetAsync(ulong id, CancellationToken cancellationToken);

    Task UpdateValidationAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken);

    Task ResetValidationAsync(ulong id, CancellationToken cancellationToken);
}

public interface IBindingItemRepository
{
    Task<BindingItemRecord> UpsertAsync(
        ulong bindingSetId,
        ulong templateElementId,
        BindingItemUpsert request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BindingItemRecord>> ListAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken);

    Task<BindingItemRecord?> GetAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken);
}

public interface IDataConnectionCredentialResolver
{
    DataConnectionCredential Resolve(string credentialRef);
}

public interface IDataSourceConnectionFactory
{
    Task<DbConnection> OpenAsync(
        DataConnectionRecord connection,
        CancellationToken cancellationToken = default);
}

public interface IDatabaseSchemaIntrospector
{
    Task<IReadOnlyList<string>> ListSchemasAsync(
        DataConnectionRecord connection,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(
        DataConnectionRecord connection,
        string? schema,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DatabaseColumnInfo>> ListColumnsAsync(
        DataConnectionRecord connection,
        string schema,
        string objectName,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadSampleAsync(
        DataConnectionRecord connection,
        string schema,
        string objectName,
        IReadOnlyList<DatabaseColumnInfo> columns,
        int limit,
        CancellationToken cancellationToken);
}

public interface IContextualDataSchemaProvider
{
    Task<IReadOnlyList<DataFieldNode>> GetSchemaAsync(
        DataSchemaContext context,
        CancellationToken cancellationToken);

    Task<DataSchemaSearchResult> SearchAsync(
        DataSchemaContext context,
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<DataFieldDefinition?> FindByPathAsync(
        DataSchemaContext context,
        string path,
        CancellationToken cancellationToken);
}

public interface ITemplateElementIdentityResolver
{
    TemplateElementIdentity Resolve(
        string locatorId,
        TextLocator locator,
        string? placeholderCandidatePath,
        string? contentControlTag);
}

public interface IBindingCandidateResolver
{
    Task<TemplateImportSummary> ResolveAsync(
        ulong bindingSetId,
        ulong dataSourceId,
        CancellationToken cancellationToken);
}

#pragma warning restore CS1591
