using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 负责获取模板与绑定并导出可重复使用的 DOCX 模板。
/// </summary>
public sealed class ReusableTemplateWorkflowService
{
    private readonly ITemplateStore _templateStore;
    private readonly IBindingStore _bindingStore;
    private readonly IWordReusableTemplateRenderer _renderer;

    /// <summary>
    /// 初始化复用模板导出业务服务。
    /// </summary>
    /// <param name="templateStore">模板存储。</param>
    /// <param name="bindingStore">绑定存储。</param>
    /// <param name="renderer">复用模板渲染器。</param>
    public ReusableTemplateWorkflowService(
        ITemplateStore templateStore,
        IBindingStore bindingStore,
        IWordReusableTemplateRenderer renderer)
    {
        _templateStore = templateStore;
        _bindingStore = bindingStore;
        _renderer = renderer;
    }

    /// <summary>
    /// 导出指定模板当前绑定状态的可复用 DOCX。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回生成的可复用模板。</returns>
    public async Task<RenderedTemplate> ExportAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        TemplateDocument template = await _templateStore.GetAsync(templateId, cancellationToken)
            ?? throw new TemplateNotFoundException(templateId);
        IReadOnlyList<TemplateBinding> bindings =
            await _bindingStore.GetByTemplateAsync(templateId, cancellationToken);
        if (bindings.Count == 0)
        {
            throw new EmptyReusableTemplateBindingsException();
        }

        return await _renderer.RenderAsync(template, bindings, cancellationToken);
    }
}
