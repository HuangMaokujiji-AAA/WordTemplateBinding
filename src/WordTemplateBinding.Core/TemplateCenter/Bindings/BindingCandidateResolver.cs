#pragma warning disable CS1591
using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

public sealed class BindingCandidateResolver : IBindingCandidateResolver
{
    private const int AutoBindThreshold = 80;
    private readonly IBindingSetRepository _sets;
    private readonly ITemplateElementRepository _elements;
    private readonly BindingWorkspaceService _workspace;

    public BindingCandidateResolver(
        IBindingSetRepository sets,
        ITemplateElementRepository elements,
        BindingWorkspaceService workspace)
    {
        _sets = sets;
        _elements = elements;
        _workspace = workspace;
    }

    public async Task<TemplateImportSummary> ResolveAsync(
        ulong bindingSetId,
        ulong dataSourceId,
        CancellationToken cancellationToken)
    {
        BindingSetRecord set = await _sets.GetAsync(bindingSetId, cancellationToken)
            ?? throw new Exceptions.WorkspaceException(
                "binding_set_not_found",
                $"找不到绑定配置：{bindingSetId}。");
        IReadOnlyList<TemplateElementRecord> elements = await _elements.ListAsync(
            set.TemplateVersionId,
            cancellationToken);
        IReadOnlyDictionary<ulong, IReadOnlyList<BindingSuggestion>> suggestionIndex =
            await _workspace.SuggestManyAsync(
                elements,
                dataSourceId,
                cancellationToken);
        List<string> unresolved = new();
        List<string> warnings = new();
        int textCount = 0;
        int chartCount = 0;
        int tableCount = 0;
        foreach (TemplateElementRecord element in elements)
        {
            IReadOnlyList<BindingSuggestion> suggestions = suggestionIndex[element.Id];
            BindingSuggestion? best = suggestions.FirstOrDefault();
            if (best is null || best.Score < AutoBindThreshold)
            {
                unresolved.Add(element.DisplayName ?? element.ElementKey);
                continue;
            }

            if (suggestions.Count > 1 && suggestions[1].Score == best.Score)
            {
                unresolved.Add(element.DisplayName ?? element.ElementKey);
                warnings.Add(
                    $"元素 {element.ElementKey} 存在并列候选，未自动绑定。");
                continue;
            }

            await _workspace.UpsertAsync(
                bindingSetId,
                element.Id,
                new BindingItemUpsert
                {
                    DataSourceId = dataSourceId,
                    SourcePath = best.FieldPath,
                    TargetProperty = "$",
                    SourceKind = "DATA_SOURCE",
                    FormatConfigJson = BuildTableFormatConfig(element),
                },
                cancellationToken);
            if (string.Equals(element.ElementType, "CHART", StringComparison.Ordinal))
            {
                chartCount++;
            }
            else if (string.Equals(element.ElementType, "TABLE", StringComparison.Ordinal))
            {
                tableCount++;
            }
            else
            {
                textCount++;
            }
        }

        return new TemplateImportSummary
        {
            TextBindingsRestored = textCount,
            ChartBindingsRestored = chartCount,
            TableBindingsRestored = tableCount,
            UnresolvedPlaceholders = unresolved.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
        };
    }

    private static string? BuildTableFormatConfig(TemplateElementRecord element)
    {
        if (!string.Equals(element.ElementType, "TABLE", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(element.BindingSchemaJson))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(element.BindingSchemaJson);
        JsonElement root = document.RootElement;
        IReadOnlyList<TableColumnBinding> columns = root.GetProperty("columns")
            .Deserialize<List<TableColumnBinding>>(JsonOptions)
            ?? new List<TableColumnBinding>();
        TableBindingMapping mapping = new()
        {
            HeaderRowCount = root.TryGetProperty("headerRowCount", out JsonElement headerRows)
                ? headerRows.GetInt32()
                : 1,
            Columns = columns,
            FilterField = root.TryGetProperty("filterField", out JsonElement filterField) &&
                          filterField.ValueKind == JsonValueKind.String
                ? filterField.GetString()
                : null,
            FilterValue = root.TryGetProperty("filterValue", out JsonElement filterValue) &&
                          filterValue.ValueKind == JsonValueKind.String
                ? filterValue.GetString()
                : null,
        };
        return JsonSerializer.Serialize(new { tableMapping = mapping }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}

#pragma warning restore CS1591
