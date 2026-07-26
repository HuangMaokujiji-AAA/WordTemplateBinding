#pragma warning disable CS1591
using System.Text.Json;
using System.Text.Json.Serialization;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

public sealed class BindingSetDocumentService
{
    private readonly IBindingSetRepository _sets;
    private readonly IBindingItemRepository _items;
    private readonly ITemplateVersionRepository _versions;
    private readonly ITemplateElementRepository _elements;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly IFileStorageService _files;
    private readonly IWordReportRenderer _reports;
    private readonly IWordReusableTemplateRenderer _reusableTemplates;
    private readonly BindingWorkspaceService _bindings;

    public BindingSetDocumentService(
        IBindingSetRepository sets,
        IBindingItemRepository items,
        ITemplateVersionRepository versions,
        ITemplateElementRepository elements,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        IFileStorageService files,
        IWordReportRenderer reports,
        IWordReusableTemplateRenderer reusableTemplates,
        BindingWorkspaceService bindings)
    {
        _sets = sets;
        _items = items;
        _versions = versions;
        _elements = elements;
        _snapshots = snapshots;
        _fields = fields;
        _files = files;
        _reports = reports;
        _reusableTemplates = reusableTemplates;
        _bindings = bindings;
    }

    public async Task<RenderedReport> GenerateReportAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        BindingValidationResult validation = await _bindings.ValidateAsync(
            bindingSetId,
            cancellationToken);
        if (!string.Equals(validation.Status, "VALID", StringComparison.Ordinal))
        {
            throw new BindingValidationException(
                "绑定配置校验未通过，不能生成报告。");
        }

        RenderContext context = await BuildContextAsync(bindingSetId, cancellationToken);
        return await _reports.RenderAsync(
            context.Template,
            context.ReportBindings,
            context.Values,
            cancellationToken);
    }

    public async Task<RenderedTemplate> ExportReusableTemplateAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        RenderContext context = await BuildContextAsync(bindingSetId, cancellationToken);
        return await _reusableTemplates.RenderAsync(
            context.Template,
            context.ReusableBindings,
            cancellationToken);
    }

    private async Task<RenderContext> BuildContextAsync(
        ulong bindingSetId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await _sets.GetAsync(bindingSetId, cancellationToken)
            ?? throw new WorkspaceException(
                "binding_set_not_found",
                $"找不到绑定配置：{bindingSetId}。");
        TemplateVersionRecord version = await _versions.GetAsync(
                set.TemplateVersionId,
                cancellationToken)
            ?? throw new TemplatePersistenceException(
                "template_version_not_found",
                $"找不到模板版本：{set.TemplateVersionId}。");
        IReadOnlyList<BindingItemRecord> items = await _items.ListAsync(
            bindingSetId,
            cancellationToken);
        if (items.Count == 0)
        {
            throw new EmptyBindingsException();
        }

        IReadOnlyList<TemplateElementRecord> elements = await _elements.ListAsync(
            version.Id,
            cancellationToken);
        Dictionary<ulong, TemplateElementRecord> elementIndex =
            elements.ToDictionary(item => item.Id);
        List<TemplateBinding> reportBindings = new(items.Count);
        List<TemplateBinding> reusableBindings = new(items.Count);
        Dictionary<string, object?> values = new(StringComparer.Ordinal);
        foreach (BindingItemRecord item in items)
        {
            if (!elementIndex.TryGetValue(
                    item.TemplateElementId,
                    out TemplateElementRecord? element))
            {
                throw new BindingValidationException(
                    $"绑定引用的模板元素 {item.TemplateElementId} 不存在。");
            }

            if (!item.DataSourceId.HasValue || string.IsNullOrWhiteSpace(item.SourcePath))
            {
                throw new BindingValidationException(
                    $"模板元素 {item.TemplateElementId} 的绑定缺少数据源字段。");
            }

            DataSnapshotRecord snapshot = await _snapshots.GetLatestReadyAsync(
                    item.DataSourceId.Value,
                    cancellationToken)
                ?? throw new WorkspaceException(
                    "data_snapshot_not_ready",
                    $"数据源 {item.DataSourceId.Value} 尚无 READY 快照。");
            DataFieldRecord field = await _fields.FindAsync(
                    snapshot.Id,
                    item.SourcePath,
                    cancellationToken)
                ?? throw new WorkspaceException(
                    "data_field_not_found",
                    $"找不到字段：{item.SourcePath}。");
            string locatorId = GetLocatorId(element);
            object? value = ResolveSnapshotValue(snapshot.ContentJson, item.SourcePath);
            if (value is null && !string.IsNullOrWhiteSpace(item.FallbackValueJson))
            {
                value = JsonSerializer.Deserialize<JsonElement>(
                    item.FallbackValueJson).Clone();
            }

            string runtimeDataPath = $"binding:{item.Id}";
            values[runtimeDataPath] = value;
            TemplateBinding binding = new()
            {
                TemplateId = Guid.Empty,
                TargetKind = string.Equals(
                    element.ElementType,
                    "CHART",
                    StringComparison.Ordinal)
                    ? BindingTargetKind.Chart
                    : BindingTargetKind.Text,
                LocatorId = locatorId,
                DataPath = runtimeDataPath,
                DataType = field.DataType,
                ChartMapping = DeserializeChartMapping(item.FormatConfigJson),
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
            };
            reportBindings.Add(binding);
            reusableBindings.Add(binding with { DataPath = item.SourcePath });
        }

        await using TemporaryFileLease lease =
            await _files.MaterializeTemporaryFileAsync(
                version.FileObjectId,
                cancellationToken);
        byte[] original = await File.ReadAllBytesAsync(lease.Path, cancellationToken);
        TemplateScanResult scanResult = await LoadScanResultAsync(version);
        FileObjectMetadata file = await _files.GetMetadataAsync(
                version.FileObjectId,
                cancellationToken)
            ?? throw new DatabaseFileException(
                "database_file_not_found",
                $"找不到模板版本文件：{version.FileObjectId}。");
        TemplateDocument template = new(
            Guid.Empty,
            file.OriginalName,
            original,
            scanResult.ContentHash,
            scanResult,
            version.CreatedAt,
            version.CreatedAt);
        return new RenderContext(
            template,
            reportBindings.AsReadOnly(),
            reusableBindings.AsReadOnly(),
            values);
    }

    private static Task<TemplateScanResult> LoadScanResultAsync(
        TemplateVersionRecord version)
    {
        if (string.IsNullOrWhiteSpace(version.ParseResultJson))
        {
            throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {version.Id} 尚无解析结果。");
        }

        TemplateParseResult result =
            JsonSerializer.Deserialize<TemplateParseResult>(
                version.ParseResultJson,
                JsonOptions)
            ?? throw new TemplatePersistenceException(
                "template_version_not_ready",
                $"模板版本 {version.Id} 的解析结果无效。");
        return Task.FromResult(result.ScanResult);
    }

    private static string GetLocatorId(TemplateElementRecord element)
    {
        using JsonDocument document = JsonDocument.Parse(element.LocatorJson);
        if (!document.RootElement.TryGetProperty("locatorId", out JsonElement value) ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new BindingValidationException(
                $"模板元素 {element.Id} 缺少 locatorId。");
        }

        return value.GetString()!;
    }

    private static object? ResolveSnapshotValue(string? contentJson, string path)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(contentJson);
        if (!document.RootElement.TryGetProperty(
                "sampleRows",
                out JsonElement rows) ||
            rows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string[] segments = path.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 1 &&
            string.Equals(segments[0], "rows", StringComparison.OrdinalIgnoreCase))
        {
            return rows.Clone();
        }

        JsonElement current;
        if (rows.GetArrayLength() == 0)
        {
            return null;
        }

        current = rows[0];
        int start = segments.Length > 0 &&
                    string.Equals(segments[0], "rows", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        for (int index = start; index < segments.Length; index++)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segments[index], out current))
            {
                return null;
            }
        }

        return current.Clone();
    }

    private static ChartBindingMapping? DeserializeChartMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("chartMapping", out JsonElement wrapped))
            {
                root = wrapped;
            }

            return root.Deserialize<ChartBindingMapping>(JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new BindingValidationException(
                $"图表 format_config_json 无效：{exception.Message}");
        }
    }

    private sealed record RenderContext(
        TemplateDocument Template,
        IReadOnlyCollection<TemplateBinding> ReportBindings,
        IReadOnlyCollection<TemplateBinding> ReusableBindings,
        IReadOnlyDictionary<string, object?> Values);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

#pragma warning restore CS1591
