using System.Collections.Concurrent;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Stores;

/// <summary>
/// 使用线程安全字典保存模板快照。
/// </summary>
public sealed class InMemoryTemplateStore : ITemplateStore
{
    private readonly ConcurrentDictionary<Guid, TemplateDocument> _templates = new();

    /// <inheritdoc />
    public Task<TemplateDocument?> GetAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TemplateDocument? snapshot = _templates.TryGetValue(templateId, out TemplateDocument? template)
            ? template.CreateSnapshot()
            : null;
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task SaveAsync(
        TemplateDocument template,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _templates[template.Id] = template.CreateSnapshot();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 使用模板标识和定位标识组成的键保存当前绑定。
/// </summary>
public sealed class InMemoryBindingStore : IBindingStore
{
    private readonly ConcurrentDictionary<BindingKey, TemplateBinding> _bindings = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<TemplateBinding>> GetByTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TemplateBinding> snapshot = _bindings
            .Where(pair => pair.Key.TemplateId == templateId)
            .Select(pair => pair.Value with { })
            .OrderBy(binding => binding.CreatedAt)
            .ThenBy(binding => binding.LocatorId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        TemplateBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _bindings[new BindingKey(binding.TemplateId, binding.LocatorId)] = binding with { };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        Guid templateId,
        string locatorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool removed = _bindings.TryRemove(new BindingKey(templateId, locatorId), out _);
        return Task.FromResult(removed);
    }

    /// <summary>
    /// 表示内存绑定字典的复合键。
    /// </summary>
    /// <param name="TemplateId">模板唯一标识。</param>
    /// <param name="LocatorId">模拟数据定位标识。</param>
    private readonly record struct BindingKey(Guid TemplateId, string LocatorId);
}
