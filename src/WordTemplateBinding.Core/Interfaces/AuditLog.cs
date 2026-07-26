#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 将业务审计事件写入 rp_audit_log。
/// 审计写入失败不能导致主业务失败。
/// </summary>
public interface IAuditLogWriter
{
    /// <summary>
    /// 尝试写入一条审计日志。失败时静默吞下并记录到诊断日志。
    /// </summary>
    /// <param name="action">操作名称，例如 CREATE_PROJECT。</param>
    /// <param name="targetType">目标类型，例如 rp_project。</param>
    /// <param name="targetId">目标记录主键。</param>
    /// <param name="actorUserId">操作人用户 ID。</param>
    /// <param name="beforeJson">修改前快照（可选）。</param>
    /// <param name="afterJson">修改后快照（可选）。</param>
    /// <param name="traceId">请求链路标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task WriteAsync(
        string action,
        string targetType,
        ulong targetId,
        ulong actorUserId,
        string? beforeJson,
        string? afterJson,
        string? traceId,
        CancellationToken cancellationToken);
}

#pragma warning restore CS1591
