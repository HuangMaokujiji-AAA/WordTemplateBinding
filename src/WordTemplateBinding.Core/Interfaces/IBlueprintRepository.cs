using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 定义组件契约的持久化操作。
/// </summary>
public interface IComponentContractRepository
{
    /// <summary>
    /// 获取指定模板版本的组件契约。
    /// </summary>
    Task<ComponentContractRecord?> GetAsync(
        ulong templateVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建或更新组件契约。
    /// </summary>
    Task UpsertAsync(
        ComponentContractRecord contract,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义报告蓝图的持久化操作。
/// </summary>
public interface IReportBlueprintRepository
{
    /// <summary>
    /// 创建蓝图。
    /// </summary>
    Task<BlueprintRecord> CreateAsync(
        string blueprintCode,
        string blueprintName,
        string? description,
        ulong? actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取蓝图。
    /// </summary>
    Task<BlueprintRecord?> GetAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按编码获取蓝图。
    /// </summary>
    Task<BlueprintRecord?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出蓝图。
    /// </summary>
    Task<PagedResult<BlueprintRecord>> ListAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新蓝图。
    /// </summary>
    Task<bool> UpdateAsync(
        ulong blueprintId,
        string? name,
        string? description,
        uint expectedRowVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 归档蓝图。
    /// </summary>
    Task<bool> ArchiveAsync(
        ulong blueprintId,
        uint expectedRowVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义蓝图版本的持久化操作。
/// </summary>
public interface IBlueprintVersionRepository
{
    /// <summary>
    /// 创建新版本（草稿）。
    /// </summary>
    Task<BlueprintVersionRecord> CreateDraftAsync(
        ulong blueprintId,
        ulong masterTemplateVersionId,
        string? configJson,
        ulong? actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取蓝图版本。
    /// </summary>
    Task<BlueprintVersionRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出蓝图的所有版本。
    /// </summary>
    Task<IReadOnlyList<BlueprintVersionRecord>> ListAsync(
        ulong blueprintId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发布蓝图版本。
    /// </summary>
    Task<bool> PublishAsync(
        ulong versionId,
        string dependencyHash,
        ulong? actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义蓝图节点的持久化操作。
/// </summary>
public interface IBlueprintNodeRepository
{
    /// <summary>
    /// 替换蓝图版本的全部节点。
    /// </summary>
    Task ReplaceAsync(
        ulong blueprintVersionId,
        IReadOnlyList<BlueprintNodeRecord> nodes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出蓝图版本的所有节点。
    /// </summary>
    Task<IReadOnlyList<BlueprintNodeRecord>> ListAsync(
        ulong blueprintVersionId,
        CancellationToken cancellationToken = default);
}
