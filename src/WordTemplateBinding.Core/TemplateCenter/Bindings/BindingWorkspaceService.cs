#pragma warning disable CS1591
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;
public sealed class BindingWorkspaceService
{
    private static readonly Regex TargetPropertyPattern = new(
        @"^(?:\$|categories|series\[\d+\]\.values)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private readonly IBindingSetRepository _sets;
    private readonly IBindingItemRepository _items;
    private readonly ITemplateVersionRepository _versions;
    private readonly ITemplateElementRepository _elements;
    private readonly IChapterRepository _chapters;
    private readonly IDataSourceRepository _sources;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly BindingSuggestionOptions _suggestions;
    private readonly ApplicationIdentityOptions _identity;

    public BindingWorkspaceService(
        IBindingSetRepository sets,
        IBindingItemRepository items,
        ITemplateVersionRepository versions,
        ITemplateElementRepository elements,
        IChapterRepository chapters,
        IDataSourceRepository sources,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        BindingSuggestionOptions suggestions,
        ApplicationIdentityOptions identity)
    {
        _sets = sets;
        _items = items;
        _versions = versions;
        _elements = elements;
        _chapters = chapters;
        _sources = sources;
        _snapshots = snapshots;
        _fields = fields;
        _suggestions = suggestions;
        _identity = identity;
    }

    public async Task<BindingSetRecord> GetOrCreateDraftAsync(
        ulong chapterId,
        ulong templateVersionId,
        CancellationToken cancellationToken)
    {
        TemplateVersionRecord version = await _versions.GetAsync(
            templateVersionId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{templateVersionId}。");
        if (!version.VersionStatus.StartsWith("READY", StringComparison.Ordinal))
        {
            throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {templateVersionId} 尚未就绪。");
        }

        if (await _chapters.GetAsync(chapterId, cancellationToken) is null)
        {
            throw new WorkspaceException("chapter_not_found", $"找不到章节：{chapterId}。");
        }

        return await _sets.GetOrCreateDraftAsync(
            chapterId,
            templateVersionId,
            ParseActor(),
            cancellationToken);
    }

    public async Task<BindingItemRecord> UpsertAsync(
        ulong bindingSetId,
        ulong templateElementId,
        BindingItemUpsert request,
        CancellationToken cancellationToken)
    {
        BindingContext context = await ValidateContextAsync(
            bindingSetId,
            templateElementId,
            request.DataSourceId,
            request.SourcePath,
            cancellationToken);
        if (!TargetPropertyPattern.IsMatch(request.TargetProperty))
        {
                throw new BindingValidationException("TargetProperty 不在允许的属性白名单中。");
        }

        if (!string.Equals(
                request.SourceKind,
                "DATA_SOURCE",
                StringComparison.Ordinal) ||
            request.SourcePath.Length > 1024)
        {
            throw new BindingValidationException(
                "本阶段只允许 DATA_SOURCE 来源，且字段路径不能超过 1024 字符。");
        }

        ValidateOptionalJson(request.TransformConfigJson, "TransformConfigJson");
        ValidateOptionalJson(request.FormatConfigJson, "FormatConfigJson");
        ValidateOptionalJson(request.FallbackValueJson, "FallbackValueJson");
        ValidateCompatibility(context.Element, context.Field);
        if (string.Equals(context.Element.ElementType, "TABLE", StringComparison.Ordinal))
        {
            ValidateTableMapping(request.FormatConfigJson);
        }
        BindingItemRecord saved = await _items.UpsertAsync(
            bindingSetId,
            templateElementId,
            request,
            cancellationToken);
        await _sets.ResetValidationAsync(bindingSetId, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<BindingItemRecord>> ListAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        await RequireSetAsync(bindingSetId, cancellationToken);
        return await _items.ListAsync(bindingSetId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        EnsureDraft(set);
        bool deleted = await _items.DeleteAsync(
            bindingSetId,
            templateElementId,
            cancellationToken);
        await _sets.ResetValidationAsync(bindingSetId, cancellationToken);
        return deleted;
    }

    public async Task<BindingValidationResult> ValidateAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        IReadOnlyList<TemplateElementRecord> elements = await _elements.ListAsync(
            set.TemplateVersionId,
            cancellationToken);
        IReadOnlyList<BindingItemRecord> items = await _items.ListAsync(
            bindingSetId,
            cancellationToken);
        List<BindingValidationItem> issues = new();
        Dictionary<ulong, TemplateElementRecord> elementIndex =
            elements.ToDictionary(item => item.Id);
        ChapterRecord? chapter = await _chapters.GetAsync(
            set.ChapterId,
            cancellationToken);
        foreach (TemplateElementRecord required in elements.Where(item => item.IsRequired))
        {
            if (!items.Any(item => item.TemplateElementId == required.Id))
            {
                issues.Add(new BindingValidationItem(
                    "REQUIRED_ELEMENT_UNBOUND",
                    "ERROR",
                    required.Id,
                    $"必填标记“{required.DisplayName ?? required.ElementKey}”尚未绑定。"));
            }
        }

        int invalidCount = 0;
        foreach (BindingItemRecord item in items)
        {
            if (!elementIndex.TryGetValue(
                    item.TemplateElementId,
                    out TemplateElementRecord? element))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "TEMPLATE_ELEMENT_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定引用的模板元素已不存在。"));
                continue;
            }

            if (!string.Equals(element.ParseStatus, "VALID", StringComparison.OrdinalIgnoreCase))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "TEMPLATE_ELEMENT_UNAVAILABLE",
                    "ERROR",
                    item.TemplateElementId,
                    $"模板元素当前不可绑定：{element.ParseMessage ?? element.ParseStatus}"));
                continue;
            }

            if (!item.DataSourceId.HasValue || string.IsNullOrWhiteSpace(item.SourcePath))
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "BINDING_SOURCE_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定缺少数据源或字段路径。"));
                continue;
            }

            DataSourceRecord? source = await _sources.GetAsync(
                item.DataSourceId.Value,
                cancellationToken);
            if (chapter is null ||
                source is null ||
                source.ProjectId != chapter.ProjectId)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "BINDING_SOURCE_PROJECT_MISMATCH",
                    "ERROR",
                    item.TemplateElementId,
                    "绑定数据源不存在或不属于当前章节项目。"));
                continue;
            }

            DataSnapshotRecord? snapshot = await _snapshots.GetLatestReadyAsync(
                item.DataSourceId.Value,
                cancellationToken);
            DataFieldRecord? field = snapshot is null
                ? null
                : await _fields.FindAsync(
                    snapshot.Id,
                    item.SourcePath,
                    cancellationToken);
            if (field is null)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_MISSING",
                    "ERROR",
                    item.TemplateElementId,
                    $"字段 {item.SourcePath} 已失效。"));
                continue;
            }

            if (!field.IsBindable)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_NOT_BINDABLE",
                    "ERROR",
                    item.TemplateElementId,
                    $"字段 {item.SourcePath} 当前不可绑定。"));
                continue;
            }

            try
            {
                ValidateCompatibility(element, field);
            }
            catch (BindingValidationException exception)
            {
                invalidCount++;
                issues.Add(new BindingValidationItem(
                    "DATA_FIELD_TYPE_CHANGED",
                    "ERROR",
                    item.TemplateElementId,
                    exception.Message));
            }
        }

        int requiredUnbound = issues.Count(item =>
            item.Code == "REQUIRED_ELEMENT_UNBOUND");
        string status = invalidCount > 0 || requiredUnbound > 0
            ? "ERROR"
            : issues.Count > 0
                ? "WARNING"
                : "VALID";
        BindingValidationResult result = new()
        {
            Status = status,
            Summary = new BindingValidationSummary(
                elements.Count,
                items.Count,
                requiredUnbound,
                invalidCount,
                issues.Count(item => item.Level == "WARNING")),
            Items = issues.AsReadOnly(),
        };
        await _sets.UpdateValidationAsync(
            bindingSetId,
            status,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<BindingSuggestion>> SuggestAsync(
        ulong elementId,
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        TemplateElementRecord element = await _elements.GetAsync(elementId, cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{elementId}。");
        IReadOnlyList<DataFieldRecord> fields = await ListSuggestionFieldsAsync(
            dataSourceId,
            cancellationToken);
        return BuildSuggestions(element, fields);
    }

    public async Task<IReadOnlyDictionary<ulong, IReadOnlyList<BindingSuggestion>>>
        SuggestManyAsync(
            IReadOnlyList<TemplateElementRecord> elements,
            ulong dataSourceId,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<DataFieldRecord> fields = await ListSuggestionFieldsAsync(
            dataSourceId,
            cancellationToken);
        Dictionary<ulong, IReadOnlyList<BindingSuggestion>> suggestions = new();
        foreach (TemplateElementRecord element in elements)
        {
            suggestions[element.Id] = BuildSuggestions(element, fields);
        }

        return suggestions;
    }

    private async Task<IReadOnlyList<DataFieldRecord>> ListSuggestionFieldsAsync(
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            dataSourceId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据源 {dataSourceId} 尚无 READY 快照。");
        return await _fields.ListAsync(
            snapshot.Id,
            null,
            5000,
            cancellationToken);
    }

    private IReadOnlyList<BindingSuggestion> BuildSuggestions(
        TemplateElementRecord element,
        IReadOnlyList<DataFieldRecord> fields)
    {
        EnsureElementBindable(element);
        string displayName = element.DisplayName ?? element.ElementKey;
        string normalizedDisplay = NormalizeName(displayName);
        HashSet<string> aliases = _suggestions.Aliases.TryGetValue(
                displayName,
                out string[]? configured)
            ? configured.Select(NormalizeName).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return fields
            .Where(field => field.IsBindable)
            .Select(field => ScoreSuggestion(
                element,
                field,
                displayName,
                normalizedDisplay,
                aliases))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.FieldPath, StringComparer.Ordinal)
            .Take(20)
            .ToList()
            .AsReadOnly();
    }

    public async Task<BindingPreview> PreviewAsync(
        ulong bindingSetId,
        ulong templateElementId,
        CancellationToken cancellationToken)
    {
        BindingItemRecord item = await _items.GetAsync(
            bindingSetId,
            templateElementId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "binding_item_not_found",
                "找不到指定模板元素的绑定。");
        if (!item.DataSourceId.HasValue || string.IsNullOrWhiteSpace(item.SourcePath))
        {
            throw new WorkspaceException(
                "binding_item_not_found",
                "绑定缺少数据源字段。");
        }

        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            item.DataSourceId.Value,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                "绑定数据源尚无 READY 快照。");
        DataFieldRecord field = await _fields.FindAsync(
            snapshot.Id,
            item.SourcePath,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_field_not_found",
                $"字段 {item.SourcePath} 已失效。");
        TemplateElementRecord element = await _elements.GetAsync(
            templateElementId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{templateElementId}。");
        EnsureElementBindable(element);
        return new BindingPreview
        {
            TemplateElementId = element.Id,
            DisplayName = element.DisplayName ?? element.ElementKey,
            SourcePath = field.FieldPath,
            RawValueJson = field.SampleValueJson,
            FormattedValue = FormatSample(field.SampleValueJson),
            DataType = field.DataType,
            SnapshotId = snapshot.Id,
        };
    }

    private async Task<BindingContext> ValidateContextAsync(
        ulong bindingSetId,
        ulong templateElementId,
        ulong dataSourceId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await RequireSetAsync(bindingSetId, cancellationToken);
        EnsureDraft(set);
        TemplateElementRecord element = await _elements.GetAsync(
            templateElementId,
            cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_element_not_found",
                $"找不到模板元素：{templateElementId}。");
        if (element.TemplateVersionId != set.TemplateVersionId)
        {
            throw new BindingValidationException("模板元素不属于绑定配置固定的模板版本。");
        }

        EnsureElementBindable(element);
        ChapterRecord chapter = await _chapters.GetAsync(set.ChapterId, cancellationToken)
            ?? throw new WorkspaceException(
                "chapter_not_found",
                $"找不到章节：{set.ChapterId}。");
        DataSourceRecord source = await _sources.GetAsync(dataSourceId, cancellationToken)
            ?? throw new WorkspaceException(
                "data_source_not_found",
                $"找不到数据源：{dataSourceId}。");
        if (chapter.ProjectId != source.ProjectId)
        {
            throw new WorkspaceException(
                "cross_project_binding_forbidden",
                "不允许绑定其他项目的数据源。");
        }

        DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
            dataSourceId,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_snapshot_not_ready",
                $"数据源 {dataSourceId} 尚无 READY 快照。");
        DataFieldRecord field = await _fields.FindAsync(
            snapshot.Id,
            sourcePath,
            cancellationToken)
            ?? throw new WorkspaceException(
                "data_field_not_found",
                $"找不到字段：{sourcePath}。");
        if (!field.IsBindable)
        {
            throw new BindingValidationException($"字段 {sourcePath} 当前不可绑定。");
        }

        return new BindingContext(set, element, source, snapshot, field);
    }

    private async Task<BindingSetRecord> RequireSetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _sets.GetAsync(id, cancellationToken)
        ?? throw new WorkspaceException(
            "binding_set_not_found",
            $"找不到绑定配置：{id}。");

    private static void EnsureDraft(BindingSetRecord set)
    {
        if (!string.Equals(set.BindingStatus, "DRAFT", StringComparison.Ordinal))
        {
            throw new WorkspaceException(
                "binding_set_read_only",
                $"绑定配置 {set.Id} 已发布，不能修改。");
        }
    }

    private static void EnsureElementBindable(TemplateElementRecord element)
    {
        if (!string.Equals(element.ParseStatus, "VALID", StringComparison.OrdinalIgnoreCase))
        {
            throw new BindingValidationException(
                $"模板元素当前不可绑定：{element.ParseMessage ?? element.ParseStatus}");
        }
    }

    private static void ValidateCompatibility(
        TemplateElementRecord element,
        DataFieldRecord field)
    {
        if (string.Equals(element.ElementType, "TEXT", StringComparison.Ordinal))
        {
            if (field.DataType is DataValueType.Array or DataValueType.Object or
                DataValueType.Binary)
            {
                throw new BindingValidationException(
                    $"文字元素不能绑定 {field.DataType} 字段 {field.FieldPath}。");
            }

            using JsonDocument locator = JsonDocument.Parse(element.LocatorJson);
            if (locator.RootElement.TryGetProperty("dataType", out JsonElement typeElement) &&
                TryReadMockDataType(typeElement, out MockDataType mockType) &&
                mockType is MockDataType.Decimal or MockDataType.Integer &&
                field.DataType is not (DataValueType.Integer or DataValueType.Decimal))
            {
                throw new BindingValidationException(
                    $"数字模板元素不能绑定 {field.DataType} 字段 {field.FieldPath}。");
            }

            return;
        }

        if (string.Equals(element.ElementType, "CHART", StringComparison.Ordinal) &&
            field.DataType != DataValueType.Array)
        {
            throw new BindingValidationException("图表元素必须绑定 Array 字段。");
        }

        if (string.Equals(element.ElementType, "TABLE", StringComparison.Ordinal) &&
            field.DataType != DataValueType.Array)
        {
            throw new BindingValidationException("表格元素必须绑定 Array 字段。");
        }
    }

    private static bool TryReadMockDataType(
        JsonElement element,
        out MockDataType value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Enum.TryParse(element.GetString(), true, out value);
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int numeric) &&
            Enum.IsDefined(typeof(MockDataType), numeric))
        {
            value = (MockDataType)numeric;
            return true;
        }

        value = default;
        return false;
    }

    private static void ValidateOptionalJson(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
            if (document.RootElement.ValueKind == JsonValueKind.Undefined)
            {
                throw new JsonException();
            }
        }
        catch (JsonException exception)
        {
            throw new BindingValidationException($"{name} 必须是有效 JSON：{exception.Message}");
        }
    }

    private static void ValidateTableMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BindingValidationException("表格元素必须提供列映射配置。");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("tableMapping", out JsonElement tableMapping))
            {
                root = tableMapping;
            }

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("columns", out JsonElement columns) ||
                columns.ValueKind != JsonValueKind.Array ||
                columns.GetArrayLength() == 0)
            {
                throw new BindingValidationException("表格列映射不能为空。");
            }
        }
        catch (JsonException exception)
        {
            throw new BindingValidationException(
                $"表格列映射配置无效：{exception.Message}");
        }
    }

    private static BindingSuggestion ScoreSuggestion(
        TemplateElementRecord element,
        DataFieldRecord field,
        string displayName,
        string normalizedDisplay,
        IReadOnlySet<string> aliases)
    {
        List<string> reasons = new();
        int score = 0;
        string? suggestedSourcePath = ReadSuggestedSourcePath(element.BindingSchemaJson);
        if (string.Equals(
                suggestedSourcePath,
                field.FieldPath,
                StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
            reasons.Add("命中模板自动识别的数据集合");
        }
        string leaf = field.FieldPath.Split('.').Last();
        string normalizedFieldName = NormalizeName(field.FieldName);
        string normalizedLeaf = NormalizeName(leaf);
        if (string.Equals(displayName, field.FieldName, StringComparison.OrdinalIgnoreCase))
        {
            score += 75;
            reasons.Add("字段注释完全匹配");
        }
        else if (normalizedDisplay == normalizedFieldName)
        {
            score += 60;
            reasons.Add("名称归一化后匹配");
        }

        if (normalizedDisplay == normalizedLeaf)
        {
            score += 55;
            reasons.Add("字段末级名称匹配");
        }

        if (aliases.Contains(normalizedLeaf) || aliases.Contains(normalizedFieldName))
        {
            score += 65;
            reasons.Add("命中配置同义词");
        }

        bool collectionTarget = string.Equals(
                                    element.ElementType,
                                    "CHART",
                                    StringComparison.Ordinal) ||
                                string.Equals(
                                    element.ElementType,
                                    "TABLE",
                                    StringComparison.Ordinal);
        bool typeCompatible = collectionTarget
            ? field.DataType == DataValueType.Array
            : field.DataType is not (
                DataValueType.Array or DataValueType.Object or DataValueType.Binary);
        if (typeCompatible)
        {
            score += 20;
            reasons.Add("数据类型兼容");
        }
        else
        {
            score = 0;
            reasons.Clear();
        }

        return new BindingSuggestion(
            field.FieldPath,
            Math.Min(score, 100),
            reasons.AsReadOnly());
    }

    private static string? ReadSuggestedSourcePath(string? bindingSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(bindingSchemaJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bindingSchemaJson);
            return document.RootElement.TryGetProperty(
                    "suggestedSourcePath",
                    out JsonElement value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeName(string value) =>
        new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string? FormatSample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.ToString();
    }

    private ulong? ParseActor() =>
        ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) && actor > 0
            ? actor
            : null;

    private sealed record BindingContext(
        BindingSetRecord Set,
        TemplateElementRecord Element,
        DataSourceRecord Source,
        DataSnapshotRecord Snapshot,
        DataFieldRecord Field);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

#pragma warning restore CS1591
