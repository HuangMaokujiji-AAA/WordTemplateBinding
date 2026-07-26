#pragma warning disable CS1591
using System.Collections.Concurrent;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Stores;

public sealed class InMemoryPersistenceState
{
    internal long TemplateId;
    internal long TemplateVersionId;
    internal long TemplateElementId;
    internal long ProjectId;
    internal long ChapterId;
    internal long DataConnectionId;
    internal long DataSourceId;
    internal long SnapshotId;
    internal long DataFieldId;
    internal long BindingSetId;
    internal long BindingItemId;

    internal ConcurrentDictionary<ulong, TemplateRecord> Templates { get; } = new();
    internal ConcurrentDictionary<ulong, TemplateVersionRecord> Versions { get; } = new();
    internal ConcurrentDictionary<ulong, TemplateElementRecord> Elements { get; } = new();
    internal ConcurrentDictionary<ulong, ProjectRecord> Projects { get; } = new();
    internal ConcurrentDictionary<ulong, ChapterRecord> Chapters { get; } = new();
    internal ConcurrentDictionary<ulong, DataConnectionRecord> Connections { get; } = new();
    internal ConcurrentDictionary<ulong, DataSourceRecord> Sources { get; } = new();
    internal ConcurrentDictionary<ulong, DataSnapshotRecord> Snapshots { get; } = new();
    internal ConcurrentDictionary<ulong, DataFieldRecord> Fields { get; } = new();
    internal ConcurrentDictionary<ulong, BindingSetRecord> BindingSets { get; } = new();
    internal ConcurrentDictionary<ulong, BindingItemRecord> BindingItems { get; } = new();
    internal object SyncRoot { get; } = new();
}

public sealed class InMemoryTemplateRepository : ITemplateRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryTemplateRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<TemplateRecord> CreateAsync(
        TemplateCreateRequest request,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            if (_state.Templates.Values.Any(item =>
                    string.Equals(
                        item.TemplateCode,
                        request.TemplateCode,
                        StringComparison.Ordinal)))
            {
                throw new Core.Exceptions.TemplatePersistenceException(
                    "template_code_conflict",
                    $"模板编码 {request.TemplateCode} 已存在。");
            }

            ulong id = checked((ulong)++_state.TemplateId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TemplateRecord result = new()
            {
                Id = id,
                TemplateCode = request.TemplateCode,
                TemplateName = request.TemplateName,
                TemplateType = request.TemplateType,
                CategoryCode = request.CategoryCode,
                TemplateStatus = "ACTIVE",
                Description = request.Description,
                CurrentVersionNo = 0,
                CreatedAt = now,
                UpdatedAt = now,
                RowVersion = 0,
            };
            _state.Templates[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<TemplateRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Templates.TryGetValue(id, out TemplateRecord? result);
        return Task.FromResult(result);
    }

    public Task<TemplateRecord?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TemplateRecord? result = _state.Templates.Values.FirstOrDefault(item =>
            string.Equals(item.TemplateCode, code, StringComparison.Ordinal));
        return Task.FromResult(result);
    }

    public Task<PagedResult<TemplateRecord>> ListAsync(
        TemplateListQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<TemplateRecord> filtered = _state.Templates.Values
            .Where(item =>
                (string.IsNullOrWhiteSpace(query.Name) ||
                 item.TemplateName.Contains(query.Name, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(query.Code) ||
                 item.TemplateCode.Contains(query.Code, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(query.Type) ||
                 string.Equals(item.TemplateType, query.Type, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(query.Status) ||
                 string.Equals(item.TemplateStatus, query.Status, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id)
            .ToList();
        IReadOnlyList<TemplateRecord> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(new PagedResult<TemplateRecord>(
            page,
            filtered.Count,
            query.Page,
            query.PageSize));
    }
}

public sealed class InMemoryTemplateVersionRepository : ITemplateVersionRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryTemplateVersionRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<TemplateVersionRecord> CreateNextAsync(
        ulong templateId,
        ulong fileObjectId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            if (!_state.Templates.ContainsKey(templateId))
            {
                throw new Core.Exceptions.TemplateNotFoundException(templateId);
            }

            uint versionNo = checked((uint)(_state.Versions.Values
                .Where(item => item.TemplateId == templateId)
                .Select(item => item.VersionNo)
                .DefaultIfEmpty(0U)
                .Max() + 1));
            ulong id = checked((ulong)++_state.TemplateVersionId);
            TemplateVersionRecord result = new()
            {
                Id = id,
                TemplateId = templateId,
                VersionNo = versionNo,
                FileObjectId = fileObjectId,
                VersionStatus = "UPLOADED",
                ParserName = null,
                ParserVersion = null,
                ParseResultJson = null,
                ElementCount = 0,
                StyleFingerprint = null,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _state.Versions[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<TemplateVersionRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Versions.TryGetValue(id, out TemplateVersionRecord? result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TemplateVersionRecord>> ListAsync(
        ulong templateId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TemplateVersionRecord> result = _state.Versions.Values
            .Where(item => item.TemplateId == templateId)
            .OrderByDescending(item => item.VersionNo)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task UpdateParsingAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            TemplateVersionRecord current = Require(id);
            _state.Versions[id] = current with
            {
                VersionStatus = "PARSING",
                ParserName = "WordTemplateScanner",
                ParserVersion = "2.0",
            };
            return Task.CompletedTask;
        }
    }

    public Task CompleteAsync(
        ulong id,
        string status,
        string parseResultJson,
        uint elementCount,
        string? styleFingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            TemplateVersionRecord current = Require(id);
            _state.Versions[id] = current with
            {
                VersionStatus = status,
                ParserName = "WordTemplateScanner",
                ParserVersion = "2.0",
                ParseResultJson = parseResultJson,
                ElementCount = elementCount,
                StyleFingerprint = styleFingerprint,
            };
            TemplateRecord template = _state.Templates[current.TemplateId];
            _state.Templates[current.TemplateId] = template with
            {
                CurrentVersionNo = current.VersionNo,
                UpdatedAt = DateTimeOffset.UtcNow,
                RowVersion = template.RowVersion + 1,
            };
            return Task.CompletedTask;
        }
    }

    public Task FailAsync(
        ulong id,
        string parseResultJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            TemplateVersionRecord current = Require(id);
            _state.Versions[id] = current with
            {
                VersionStatus = "FAILED",
                ParserName = "WordTemplateScanner",
                ParserVersion = "2.0",
                ParseResultJson = parseResultJson,
            };
            return Task.CompletedTask;
        }
    }

    private TemplateVersionRecord Require(ulong id) =>
        _state.Versions.TryGetValue(id, out TemplateVersionRecord? record)
            ? record
            : throw new Core.Exceptions.TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{id}。");
}

public sealed class InMemoryTemplateElementRepository : ITemplateElementRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryTemplateElementRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task ReplaceAsync(
        ulong templateVersionId,
        IReadOnlyList<TemplateElementRecord> elements,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            Dictionary<string, TemplateElementRecord> existing = _state.Elements.Values
                .Where(item => item.TemplateVersionId == templateVersionId)
                .ToDictionary(item => item.ElementKey, StringComparer.OrdinalIgnoreCase);
            HashSet<ulong> retainedIds = new();

            foreach (TemplateElementRecord element in elements)
            {
                ulong id;
                if (existing.TryGetValue(element.ElementKey, out TemplateElementRecord? current))
                {
                    id = current.Id;
                    retainedIds.Add(id);
                }
                else
                {
                    id = checked((ulong)++_state.TemplateElementId);
                }

                _state.Elements[id] = element with
                {
                    Id = id,
                    TemplateVersionId = templateVersionId,
                };
            }

            foreach (TemplateElementRecord stale in existing.Values
                         .Where(item => !retainedIds.Contains(item.Id)))
            {
                _state.Elements[stale.Id] = stale with
                {
                    ParseStatus = "STALE",
                    ParseMessage = "重新扫描未找到此元素。",
                };
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TemplateElementRecord>> ListAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TemplateElementRecord> result = _state.Elements.Values
            .Where(item =>
                item.TemplateVersionId == templateVersionId &&
                !string.Equals(item.ParseStatus, "STALE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SortNo)
            .ThenBy(item => item.Id)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<TemplateElementRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Elements.TryGetValue(id, out TemplateElementRecord? result);
        return Task.FromResult(result);
    }
}

public sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryProjectRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<ProjectRecord> CreateAsync(
        string code,
        string name,
        string? description,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            ulong id = checked((ulong)++_state.ProjectId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProjectRecord result = new()
            {
                Id = id,
                ProjectCode = code,
                ProjectName = name,
                Description = description,
                ProjectStatus = "DRAFT",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _state.Projects[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<ProjectRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Projects.TryGetValue(id, out ProjectRecord? result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ProjectRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProjectRecord>>(
            _state.Projects.Values
                .OrderByDescending(item => item.UpdatedAt)
                .ToList()
                .AsReadOnly());
    }
}

public sealed class InMemoryChapterRepository : IChapterRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryChapterRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<ChapterRecord> CreateAsync(
        ulong projectId,
        string code,
        string title,
        ulong? parentId,
        decimal sortKey,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            ushort level = parentId.HasValue &&
                _state.Chapters.TryGetValue(parentId.Value, out ChapterRecord? parent)
                    ? checked((ushort)(parent.LevelNo + 1))
                    : (ushort)1;
            ulong id = checked((ulong)++_state.ChapterId);
            ChapterRecord result = new()
            {
                Id = id,
                ProjectId = projectId,
                ParentId = parentId,
                ChapterCode = code,
                Title = title,
                LevelNo = level,
                SortKey = sortKey,
                WorkflowStatus = "PENDING",
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _state.Chapters[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<ChapterRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Chapters.TryGetValue(id, out ChapterRecord? result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ChapterRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChapterRecord>>(
            _state.Chapters.Values
                .Where(item => item.ProjectId == projectId)
                .OrderBy(item => item.SortKey)
                .ToList()
                .AsReadOnly());
    }
}

public sealed class InMemoryDataConnectionRepository : IDataConnectionRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryDataConnectionRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<DataConnectionRecord> CreateAsync(
        DataConnectionRecord connection,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ulong id = checked((ulong)Interlocked.Increment(ref _state.DataConnectionId));
        DataConnectionRecord result = connection with
        {
            Id = id,
            ConnectionStatus = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _state.Connections[id] = result;
        return Task.FromResult(result);
    }

    public Task<DataConnectionRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Connections.TryGetValue(id, out DataConnectionRecord? result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DataConnectionRecord>> ListAsync(
        ulong? projectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DataConnectionRecord>>(
            _state.Connections.Values
                .Where(item => !projectId.HasValue || item.ProjectId == projectId)
                .OrderByDescending(item => item.Id)
                .ToList()
                .AsReadOnly());
    }

    public Task UpdateTestResultAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state.Connections.TryGetValue(id, out DataConnectionRecord? current))
        {
            _state.Connections[id] = current with
            {
                ConnectionStatus = status,
                LastTestedAt = DateTimeOffset.UtcNow,
                LastTestResultJson = resultJson,
            };
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryDataSourceRepository : IDataSourceRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryDataSourceRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<DataSourceRecord> CreateAsync(
        DataSourceRecord source,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ulong id = checked((ulong)Interlocked.Increment(ref _state.DataSourceId));
        DataSourceRecord result = source with
        {
            Id = id,
            SourceStatus = "ACTIVE",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _state.Sources[id] = result;
        return Task.FromResult(result);
    }

    public Task<DataSourceRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Sources.TryGetValue(id, out DataSourceRecord? result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DataSourceRecord>> ListAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DataSourceRecord>>(
            _state.Sources.Values
                .Where(item => item.ProjectId == projectId)
                .OrderByDescending(item => item.Id)
                .ToList()
                .AsReadOnly());
    }

    public Task UpdateSchemaAsync(
        ulong id,
        string schemaJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state.Sources.TryGetValue(id, out DataSourceRecord? current))
        {
            _state.Sources[id] = current with { SchemaJson = schemaJson };
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryDataSnapshotRepository : IDataSnapshotRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryDataSnapshotRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<DataSnapshotRecord> StartAsync(
        ulong dataSourceId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            ulong no = _state.Snapshots.Values
                .Where(item => item.DataSourceId == dataSourceId)
                .Select(item => item.SnapshotNo)
                .DefaultIfEmpty(0UL)
                .Max() + 1;
            ulong id = checked((ulong)++_state.SnapshotId);
            DataSnapshotRecord result = new()
            {
                Id = id,
                DataSourceId = dataSourceId,
                SnapshotNo = no,
                SnapshotStatus = "CAPTURING",
                ContentJson = null,
                SchemaJson = null,
                ContentHash = null,
                RowCount = null,
                CapturedAt = DateTimeOffset.UtcNow,
                ErrorMessage = null,
            };
            _state.Snapshots[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task CompleteAsync(
        ulong snapshotId,
        string contentJson,
        string schemaJson,
        string contentHash,
        ulong rowCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataSnapshotRecord current = _state.Snapshots[snapshotId];
        _state.Snapshots[snapshotId] = current with
        {
            SnapshotStatus = "READY",
            ContentJson = contentJson,
            SchemaJson = schemaJson,
            ContentHash = contentHash,
            RowCount = rowCount,
            ErrorMessage = null,
        };
        return Task.CompletedTask;
    }

    public Task FailAsync(
        ulong snapshotId,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataSnapshotRecord current = _state.Snapshots[snapshotId];
        _state.Snapshots[snapshotId] = current with
        {
            SnapshotStatus = "FAILED",
            ErrorMessage = safeMessage,
        };
        return Task.CompletedTask;
    }

    public Task<DataSnapshotRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.Snapshots.TryGetValue(id, out DataSnapshotRecord? result);
        return Task.FromResult(result);
    }

    public Task<DataSnapshotRecord?> GetLatestReadyAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataSnapshotRecord? result = _state.Snapshots.Values
            .Where(item =>
                item.DataSourceId == dataSourceId &&
                string.Equals(item.SnapshotStatus, "READY", StringComparison.Ordinal))
            .OrderByDescending(item => item.SnapshotNo)
            .FirstOrDefault();
        return Task.FromResult(result);
    }
}

public sealed class InMemoryDataFieldRepository : IDataFieldRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryDataFieldRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task ReplaceAsync(
        ulong snapshotId,
        IReadOnlyList<DataFieldRecord> fields,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            foreach (ulong id in _state.Fields.Values
                         .Where(item => item.SnapshotId == snapshotId)
                         .Select(item => item.Id)
                         .ToList())
            {
                _state.Fields.TryRemove(id, out _);
            }

            foreach (DataFieldRecord field in fields)
            {
                ulong id = checked((ulong)++_state.DataFieldId);
                _state.Fields[id] = field with { Id = id, SnapshotId = snapshotId };
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<DataFieldRecord>> ListAsync(
        ulong snapshotId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DataFieldRecord> result = _state.Fields.Values
            .Where(item =>
                item.SnapshotId == snapshotId &&
                (string.IsNullOrWhiteSpace(query) ||
                 item.FieldName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 item.FieldPath.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.DisplayOrder)
            .Take(limit)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<DataFieldRecord?> FindAsync(
        ulong snapshotId,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataFieldRecord? result = _state.Fields.Values.FirstOrDefault(item =>
            item.SnapshotId == snapshotId &&
            string.Equals(item.FieldPath, path, StringComparison.Ordinal));
        return Task.FromResult(result);
    }
}

public sealed class InMemoryBindingSetRepository : IBindingSetRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryBindingSetRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<BindingSetRecord> GetOrCreateDraftAsync(
        ulong chapterId,
        ulong templateVersionId,
        ulong? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            BindingSetRecord? existing = _state.BindingSets.Values.FirstOrDefault(item =>
                item.ChapterId == chapterId &&
                item.TemplateVersionId == templateVersionId &&
                string.Equals(item.BindingStatus, "DRAFT", StringComparison.Ordinal));
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            uint versionNo = checked((uint)(_state.BindingSets.Values
                .Where(item => item.ChapterId == chapterId)
                .Select(item => item.VersionNo)
                .DefaultIfEmpty(0U)
                .Max() + 1));
            ulong id = checked((ulong)++_state.BindingSetId);
            BindingSetRecord result = new()
            {
                Id = id,
                ChapterId = chapterId,
                VersionNo = versionNo,
                TemplateVersionId = templateVersionId,
                BindingStatus = "DRAFT",
                ValidationStatus = "NOT_VALIDATED",
                ValidationResultJson = null,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _state.BindingSets[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<BindingSetRecord?> GetAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state.BindingSets.TryGetValue(id, out BindingSetRecord? result);
        return Task.FromResult(result);
    }

    public Task UpdateValidationAsync(
        ulong id,
        string status,
        string resultJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BindingSetRecord current = _state.BindingSets[id];
        _state.BindingSets[id] = current with
        {
            ValidationStatus = status,
            ValidationResultJson = resultJson,
        };
        return Task.CompletedTask;
    }

    public Task ResetValidationAsync(ulong id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BindingSetRecord current = _state.BindingSets[id];
        _state.BindingSets[id] = current with
        {
            ValidationStatus = "NOT_VALIDATED",
            ValidationResultJson = null,
        };
        return Task.CompletedTask;
    }
}

public sealed class InMemoryBindingItemRepository : IBindingItemRepository
{
    private readonly InMemoryPersistenceState _state;

    public InMemoryBindingItemRepository(InMemoryPersistenceState state)
    {
        _state = state;
    }

    public Task<BindingItemRecord> UpsertAsync(
        ulong bindingSetId,
        ulong templateElementId,
        BindingItemUpsert request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_state.SyncRoot)
        {
            BindingItemRecord? current = _state.BindingItems.Values.FirstOrDefault(item =>
                item.BindingSetId == bindingSetId &&
                item.TemplateElementId == templateElementId &&
                string.Equals(
                    item.TargetProperty,
                    request.TargetProperty,
                    StringComparison.Ordinal));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ulong id = current?.Id ?? checked((ulong)++_state.BindingItemId);
            BindingItemRecord result = new()
            {
                Id = id,
                BindingSetId = bindingSetId,
                TemplateElementId = templateElementId,
                TargetProperty = request.TargetProperty,
                SourceKind = request.SourceKind,
                DataSourceId = request.DataSourceId,
                SourcePath = request.SourcePath,
                TransformConfigJson = request.TransformConfigJson,
                FormatConfigJson = request.FormatConfigJson,
                FallbackValueJson = request.FallbackValueJson,
                IsRequired = request.IsRequired,
                SortNo = current?.SortNo ?? 0,
                CreatedAt = current?.CreatedAt ?? now,
                UpdatedAt = now,
            };
            _state.BindingItems[id] = result;
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<BindingItemRecord>> ListAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BindingItemRecord>>(
            _state.BindingItems.Values
                .Where(item => item.BindingSetId == bindingSetId)
                .OrderBy(item => item.SortNo)
                .ThenBy(item => item.Id)
                .ToList()
                .AsReadOnly());
    }

    public Task<BindingItemRecord?> GetAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BindingItemRecord? result = _state.BindingItems.Values.FirstOrDefault(item =>
            item.BindingSetId == bindingSetId &&
            item.TemplateElementId == templateElementId);
        return Task.FromResult(result);
    }

    public Task<bool> DeleteAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool removed = false;
        foreach (ulong id in _state.BindingItems.Values
                     .Where(item =>
                         item.BindingSetId == bindingSetId &&
                         item.TemplateElementId == templateElementId)
                     .Select(item => item.Id)
                     .ToList())
        {
            removed |= _state.BindingItems.TryRemove(id, out _);
        }

        return Task.FromResult(removed);
    }
}

#pragma warning restore CS1591
