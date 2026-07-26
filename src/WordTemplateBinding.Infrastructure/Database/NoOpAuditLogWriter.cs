#pragma warning disable CS1591
using WordTemplateBinding.Core.Interfaces;

namespace WordTemplateBinding.Infrastructure.Database;

/// <summary>
/// 空操作审计日志写入器，用于 InMemory 模式。
/// </summary>
public sealed class NoOpAuditLogWriter : IAuditLogWriter
{
    public Task WriteAsync(
        string action,
        string targetType,
        ulong targetId,
        ulong actorUserId,
        string? beforeJson,
        string? afterJson,
        string? traceId,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

#pragma warning restore CS1591
