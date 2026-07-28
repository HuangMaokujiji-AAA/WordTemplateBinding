using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 使用 Schema 精确匹配显式占位符，并根据内嵌清单恢复图表绑定。
/// </summary>
public sealed class TemplateAutoBindingResolver : ITemplateAutoBindingResolver
{
    private readonly IBindingStore _bindingStore;
    private readonly IDataSchemaProvider _schemaProvider;
    private readonly IClock _clock;

    /// <summary>
    /// 初始化自动绑定恢复器。
    /// </summary>
    /// <param name="bindingStore">绑定存储。</param>
    /// <param name="schemaProvider">数据 Schema 来源。</param>
    /// <param name="clock">系统时间来源。</param>
    public TemplateAutoBindingResolver(
        IBindingStore bindingStore,
        IDataSchemaProvider schemaProvider,
        IClock clock)
    {
        _bindingStore = bindingStore;
        _schemaProvider = schemaProvider;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<TemplateImportSummary> ResolveAsync(
        TemplateDocument template,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TemplateBinding> currentBindings =
            await _bindingStore.GetByTemplateAsync(template.Id, cancellationToken);
        HashSet<string> boundLocatorIds = currentBindings
            .Select(binding => binding.LocatorId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> unresolved = new(StringComparer.Ordinal);
        List<string> warnings = new(template.ScanResult.BindingManifest.Warnings);
        int textBindingsRestored = 0;
        int chartBindingsRestored = 0;

        foreach (MockDataItem item in template.ScanResult.MockItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? candidate = item.PlaceholderCandidatePath;
            if (string.IsNullOrEmpty(candidate) || boundLocatorIds.Contains(item.LocatorId))
            {
                continue;
            }

            DataFieldDefinition? field =
                await _schemaProvider.FindByPathAsync(candidate, cancellationToken);
            if (field is null)
            {
                unresolved.Add(candidate);
                continue;
            }

            if (!field.IsBindable || field.Type == DataValueType.Array)
            {
                warnings.Add($"文本占位符 {candidate} 与当前字段类型 {field.Type} 不兼容，未恢复绑定。");
                continue;
            }

            await SaveBindingAsync(
                template.Id,
                item.LocatorId,
                field,
                BindingTargetKind.Text,
                cancellationToken);
            boundLocatorIds.Add(item.LocatorId);
            textBindingsRestored++;
        }

        foreach (ReusableTemplateChartBinding manifestBinding in
                 template.ScanResult.BindingManifest.ChartBindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChartTemplateItem? chart = ResolveChart(
                template.ScanResult.Charts,
                manifestBinding);
            if (chart is null)
            {
                warnings.Add(
                    $"图表绑定 {manifestBinding.DataPath} 无法唯一匹配当前文档中的 ChartPart，未恢复绑定。");
                continue;
            }

            if (boundLocatorIds.Contains(chart.LocatorId))
            {
                continue;
            }

            DataFieldDefinition? field = await _schemaProvider.FindByPathAsync(
                manifestBinding.DataPath,
                cancellationToken);
            if (field is null)
            {
                unresolved.Add(manifestBinding.DataPath);
                continue;
            }

            if (!field.IsBindable || field.Type != DataValueType.Array || !chart.IsBindable)
            {
                warnings.Add(
                    $"图表绑定 {manifestBinding.DataPath} 与当前字段或图表类型不兼容，未恢复绑定。");
                continue;
            }

            await SaveBindingAsync(
                template.Id,
                chart.LocatorId,
                field,
                BindingTargetKind.Chart,
                cancellationToken,
                manifestBinding.ChartMapping);
            boundLocatorIds.Add(chart.LocatorId);
            chartBindingsRestored++;
        }

        return new TemplateImportSummary
        {
            TextBindingsRestored = textBindingsRestored,
            ChartBindingsRestored = chartBindingsRestored,
            UnresolvedPlaceholders = unresolved.OrderBy(path => path, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
        };
    }

    private async Task SaveBindingAsync(
        Guid templateId,
        string locatorId,
        DataFieldDefinition field,
        BindingTargetKind targetKind,
        CancellationToken cancellationToken,
        ChartBindingMapping? chartMapping = null)
    {
        DateTimeOffset now = _clock.UtcNow;
        await _bindingStore.UpsertAsync(new TemplateBinding
        {
            TemplateId = templateId,
            TargetKind = targetKind,
            LocatorId = locatorId,
            DataPath = field.Path,
            DataType = field.Type,
            ChartMapping = chartMapping,
            CreatedAt = now,
            UpdatedAt = now,
        }, cancellationToken);
    }

    private static ChartTemplateItem? ResolveChart(
        IReadOnlyList<ChartTemplateItem> charts,
        ReusableTemplateChartBinding binding)
    {
        ChartTemplateItem? exact = SingleOrNull(charts.Where(chart =>
            string.Equals(chart.Locator.PartKey, binding.PartKey, StringComparison.Ordinal) &&
            string.Equals(
                chart.Locator.RelationshipId,
                binding.RelationshipId,
                StringComparison.Ordinal)));
        if (exact is not null)
        {
            return exact;
        }

        ChartTemplateItem? partAndOrder = SingleOrNull(charts.Where(chart =>
            string.Equals(chart.Locator.PartKey, binding.PartKey, StringComparison.Ordinal) &&
            chart.Locator.DocumentOrder == binding.DocumentOrder));
        return partAndOrder ?? SingleOrNull(charts.Where(
            chart => chart.Locator.DocumentOrder == binding.DocumentOrder));
    }

    private static ChartTemplateItem? SingleOrNull(IEnumerable<ChartTemplateItem> candidates)
    {
        ChartTemplateItem[] matches = candidates.Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
