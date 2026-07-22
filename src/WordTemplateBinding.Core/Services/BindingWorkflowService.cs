using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 负责模板绑定关系的校验、保存与删除。
/// </summary>
public sealed class BindingWorkflowService
{
    private readonly ITemplateStore _templateStore;
    private readonly IBindingStore _bindingStore;
    private readonly IDataSchemaProvider _schemaProvider;
    private readonly IClock _clock;

    /// <summary>
    /// 初始化绑定业务服务。
    /// </summary>
    /// <param name="templateStore">模板存储。</param>
    /// <param name="bindingStore">绑定存储。</param>
    /// <param name="schemaProvider">数据字段来源。</param>
    /// <param name="clock">系统时间来源。</param>
    public BindingWorkflowService(
        ITemplateStore templateStore,
        IBindingStore bindingStore,
        IDataSchemaProvider schemaProvider,
        IClock clock)
    {
        _templateStore = templateStore;
        _bindingStore = bindingStore;
        _schemaProvider = schemaProvider;
        _clock = clock;
    }

    /// <summary>
    /// 新增或覆盖一条模板绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="locatorId">模拟数据定位标识。</param>
    /// <param name="dataPath">数据字段路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="chartMapping">可选的图表字段映射配置。</param>
    /// <returns>返回保存后的绑定。</returns>
    public async Task<TemplateBinding> UpsertAsync(
        Guid templateId,
        string locatorId,
        string dataPath,
        CancellationToken cancellationToken = default,
        ChartBindingMapping? chartMapping = null)
    {
        TemplateDocument template = await _templateStore.GetAsync(templateId, cancellationToken)
            ?? throw new TemplateNotFoundException(templateId);
        MockDataItem? mockItem = template.ScanResult.MockItems.FirstOrDefault(
            item => string.Equals(item.LocatorId, locatorId, StringComparison.Ordinal));
        ChartTemplateItem? chartItem = template.ScanResult.Charts.FirstOrDefault(
            item => string.Equals(item.LocatorId, locatorId, StringComparison.Ordinal));
        if (mockItem is null && chartItem is null)
        {
            throw new LocatorNotFoundException(locatorId);
        }

        DataFieldDefinition field = await _schemaProvider.FindByPathAsync(dataPath, cancellationToken)
            ?? throw new DataFieldNotFoundException(dataPath);
        bool isReusablePathPlaceholder = false;
        if (!string.IsNullOrEmpty(mockItem?.PlaceholderCandidatePath))
        {
            DataFieldDefinition? placeholderField = await _schemaProvider.FindByPathAsync(
                mockItem.PlaceholderCandidatePath,
                cancellationToken);
            isReusablePathPlaceholder = placeholderField is
            {
                IsBindable: true,
                Type: not DataValueType.Array,
            };
        }

        BindingTargetKind targetKind = chartItem is null
            ? BindingTargetKind.Text
            : BindingTargetKind.Chart;
        ValidateCompatibility(mockItem, chartItem, field, isReusablePathPlaceholder);

        // Validate chart mapping
        if (chartMapping is not null && chartItem?.DataDefinition is not null)
        {
            ValidateChartMapping(chartMapping, chartItem);
        }

        IReadOnlyList<TemplateBinding> currentBindings =
            await _bindingStore.GetByTemplateAsync(templateId, cancellationToken);
        TemplateBinding? current = currentBindings.FirstOrDefault(
            binding => string.Equals(binding.LocatorId, locatorId, StringComparison.Ordinal));
        DateTimeOffset now = _clock.UtcNow;
        TemplateBinding binding = new()
        {
            TemplateId = templateId,
            TargetKind = targetKind,
            LocatorId = locatorId,
            DataPath = field.Path,
            DataType = field.Type,
            ChartMapping = chartMapping,
            CreatedAt = current?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        await _bindingStore.UpsertAsync(binding, cancellationToken);
        return binding;
    }

    private static void ValidateChartMapping(
        ChartBindingMapping mapping,
        ChartTemplateItem chartItem)
    {
        var def = chartItem.DataDefinition
            ?? throw new BindingValidationException("图表没有数据定义，无法验证映射。");

        if (!def.WriteCapability.Contains("workbook", StringComparison.OrdinalIgnoreCase) &&
            !def.WriteCapability.Contains("cache", StringComparison.OrdinalIgnoreCase))
            throw new BindingValidationException("该图表不支持数据写回，无法保存绑定。");

        if (mapping.SeriesMappings.Count != def.Series.Count)
            throw new BindingValidationException(
                $"图表 \"{chartItem.Title}\" 需要 {def.Series.Count} 个系列字段映射，但只提供了 {mapping.SeriesMappings.Count} 个。");

        var usedIndices = new HashSet<int>();
        foreach (var sm in mapping.SeriesMappings)
        {
            if (sm.SeriesIndex < 0 || sm.SeriesIndex >= def.Series.Count)
                throw new BindingValidationException(
                    $"系列索引 {sm.SeriesIndex} 超出图表 \"{chartItem.Title}\" 的系列范围 0–{def.Series.Count - 1}。");
            if (!usedIndices.Add(sm.SeriesIndex))
                throw new BindingValidationException(
                    $"系列索引 {sm.SeriesIndex} 被重复映射。");
            if (string.IsNullOrWhiteSpace(sm.ValueField))
                throw new BindingValidationException(
                    $"图表 \"{chartItem.Title}\" 的系列 \"{def.Series[sm.SeriesIndex].Name}\" 尚未选择数据字段。");
        }
    }

    /// <summary>
    /// 获取指定模板的全部绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回绑定关系只读列表。</returns>
    public async Task<IReadOnlyList<TemplateBinding>> GetByTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        if (await _templateStore.GetAsync(templateId, cancellationToken) is null)
        {
            throw new TemplateNotFoundException(templateId);
        }

        return await _bindingStore.GetByTemplateAsync(templateId, cancellationToken);
    }

    /// <summary>
    /// 删除一条模板绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="locatorId">模拟数据定位标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回是否删除了已有绑定。</returns>
    public async Task<bool> DeleteAsync(
        Guid templateId,
        string locatorId,
        CancellationToken cancellationToken = default)
    {
        if (await _templateStore.GetAsync(templateId, cancellationToken) is null)
        {
            throw new TemplateNotFoundException(templateId);
        }

        return await _bindingStore.DeleteAsync(templateId, locatorId, cancellationToken);
    }

    /// <summary>
    /// 校验模拟数据类型与字段类型是否满足第一阶段兼容矩阵。
    /// </summary>
    /// <param name="mockItem">模板模拟数据。</param>
    /// <param name="chartItem">模板图表目标。</param>
    /// <param name="field">数据字段定义。</param>
    /// <param name="isReusablePathPlaceholder">目标是否为已确认的复用模板字段路径占位符。</param>
    private static void ValidateCompatibility(
        MockDataItem? mockItem,
        ChartTemplateItem? chartItem,
        DataFieldDefinition field,
        bool isReusablePathPlaceholder)
    {
        if (!field.IsBindable)
        {
            throw new BindingValidationException($"字段 {field.Path} 在第一阶段不可绑定。");
        }

        if (chartItem is not null)
        {
            if (!chartItem.IsBindable)
            {
                throw new BindingValidationException(
                    $"图表 {chartItem.Title} 没有可写的数据系列缓存。");
            }

            if (field.Type != DataValueType.Array)
            {
                throw new BindingValidationException(
                    $"图表只能绑定集合字段，{field.Path} 的类型是 {field.Type}。");
            }

            return;
        }

        if (mockItem is null)
        {
            throw new BindingValidationException("绑定目标不存在。");
        }

        bool isCompatible = isReusablePathPlaceholder
            ? field.Type != DataValueType.Array
            : mockItem.DataType switch
        {
            MockDataType.Decimal or MockDataType.Integer =>
                field.Type is DataValueType.Integer or DataValueType.Decimal,
            MockDataType.String => field.Type == DataValueType.String,
            _ => false,
        };
        if (!isCompatible)
        {
            throw new BindingValidationException(
                $"{mockItem.DataType} 型模拟数据不能绑定到 {field.Type} 字段 {field.Path}。");
        }
    }
}
