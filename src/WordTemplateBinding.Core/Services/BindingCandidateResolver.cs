#pragma warning disable CS1591
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
        List<string> unresolved = new();
        List<string> warnings = new();
        int textCount = 0;
        int chartCount = 0;
        foreach (TemplateElementRecord element in elements)
        {
            IReadOnlyList<BindingSuggestion> suggestions =
                await _workspace.SuggestAsync(
                    element.Id,
                    dataSourceId,
                    cancellationToken);
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
                },
                cancellationToken);
            if (string.Equals(element.ElementType, "CHART", StringComparison.Ordinal))
            {
                chartCount++;
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
            UnresolvedPlaceholders = unresolved.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
        };
    }
}

#pragma warning restore CS1591
