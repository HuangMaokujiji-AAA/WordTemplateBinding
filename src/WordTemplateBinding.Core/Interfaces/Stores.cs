using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 定义模板文档的持久化抽象。
/// </summary>
public interface ITemplateStore
{
    /// <summary>
    /// 按模板标识获取安全快照。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板快照；模板不存在时返回 <see langword="null"/>。</returns>
    Task<TemplateDocument?> GetAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存模板文档的安全快照。
    /// </summary>
    /// <param name="template">需要保存的模板。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步保存操作的任务。</returns>
    Task SaveAsync(TemplateDocument template, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义模板绑定关系的持久化抽象。
/// </summary>
public interface IBindingStore
{
    /// <summary>
    /// 获取指定模板的当前绑定快照。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回绑定关系只读列表。</returns>
    Task<IReadOnlyList<TemplateBinding>> GetByTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或覆盖同一定位标识的绑定。
    /// </summary>
    /// <param name="binding">需要保存的绑定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步保存操作的任务。</returns>
    Task UpsertAsync(TemplateBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定模板和定位标识的绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="locatorId">模拟数据定位标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回是否删除了已有绑定。</returns>
    Task<bool> DeleteAsync(
        Guid templateId,
        string locatorId,
        CancellationToken cancellationToken = default);
}
