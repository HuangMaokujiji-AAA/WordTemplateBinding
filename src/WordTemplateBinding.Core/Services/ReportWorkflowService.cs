using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 负责合并数据值并调用 Word 渲染器生成报告。
/// </summary>
public sealed class ReportWorkflowService
{
    private readonly ITemplateStore _templateStore;
    private readonly IBindingStore _bindingStore;
    private readonly IDataValueProvider _valueProvider;
    private readonly IWordReportRenderer _renderer;

    /// <summary>
    /// 初始化报告生成业务服务。
    /// </summary>
    /// <param name="templateStore">模板存储。</param>
    /// <param name="bindingStore">绑定存储。</param>
    /// <param name="valueProvider">演示数据来源。</param>
    /// <param name="renderer">Word 报告渲染器。</param>
    public ReportWorkflowService(
        ITemplateStore templateStore,
        IBindingStore bindingStore,
        IDataValueProvider valueProvider,
        IWordReportRenderer renderer)
    {
        _templateStore = templateStore;
        _bindingStore = bindingStore;
        _valueProvider = valueProvider;
        _renderer = renderer;
    }

    /// <summary>
    /// 使用演示数据和请求覆盖值生成独立 DOCX 报告。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="requestValues">请求中按字段路径提供的覆盖值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回生成的 Word 报告。</returns>
    public async Task<RenderedReport> GenerateAsync(
        Guid templateId,
        IReadOnlyDictionary<string, object?>? requestValues,
        CancellationToken cancellationToken = default)
    {
        TemplateDocument template = await _templateStore.GetAsync(templateId, cancellationToken)
            ?? throw new TemplateNotFoundException(templateId);
        IReadOnlyList<TemplateBinding> bindings =
            await _bindingStore.GetByTemplateAsync(templateId, cancellationToken);
        if (bindings.Count == 0)
        {
            throw new EmptyBindingsException();
        }

        IReadOnlyDictionary<string, object?> demoValues =
            await _valueProvider.GetValuesAsync(cancellationToken);
        Dictionary<string, object?> mergedValues = new(demoValues, StringComparer.Ordinal);
        if (requestValues is not null)
        {
            foreach ((string path, object? value) in requestValues)
            {
                mergedValues[path] = value;
            }
        }

        foreach (TemplateBinding binding in bindings)
        {
            if (!mergedValues.ContainsKey(binding.DataPath))
            {
                throw new MissingDataValueException(binding.DataPath);
            }
        }

        return await _renderer.RenderAsync(template, bindings, mergedValues, cancellationToken);
    }
}
