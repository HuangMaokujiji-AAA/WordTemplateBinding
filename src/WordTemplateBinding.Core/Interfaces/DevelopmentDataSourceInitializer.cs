#pragma warning disable CS1591
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 幂等地为指定项目初始化内置 JSON 测试数据源、快照和字段目录。
/// </summary>
public interface IDevelopmentDataSourceInitializer
{
    /// <summary>
    /// 确保指定项目存在 READY 状态的开发测试 JSON 数据源。
    /// 首次调用创建数据源、快照和字段；后续调用若数据源已有 READY 快照则返回已有结果。
    /// </summary>
    /// <param name="projectId">所属项目 ID。</param>
    /// <param name="forceRefresh">
    /// 设为 <c>true</c> 时始终创建新快照，保留旧快照历史。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回初始化结果，包含数据源、快照和字段数量。</returns>
    Task<DevelopmentDataSourceInitializationResult> EnsureInitializedAsync(
        ulong projectId,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

#pragma warning restore CS1591
