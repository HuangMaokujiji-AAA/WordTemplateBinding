#pragma warning disable CS1591
using Microsoft.Extensions.Logging;
using MySqlConnector;
using WordTemplateBinding.Core.Interfaces;

namespace WordTemplateBinding.Infrastructure.Database;

public sealed class MySqlAuditLogWriter : IAuditLogWriter
{
    private readonly IReportPlatformDatabaseConnectionFactory _connections;
    private readonly ILogger<MySqlAuditLogWriter> _logger;

    public MySqlAuditLogWriter(
        IReportPlatformDatabaseConnectionFactory connections,
        ILogger<MySqlAuditLogWriter> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task WriteAsync(
        string action,
        string targetType,
        ulong targetId,
        ulong actorUserId,
        string? beforeJson,
        string? afterJson,
        string? traceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rp_audit_log
                (action, target_type, target_id, actor_user_id,
                 before_json, after_json, trace_id, created_at)
            VALUES
                (@action, @targetType, @targetId, @actorUserId,
                 CAST(@beforeJson AS JSON), CAST(@afterJson AS JSON),
                 @traceId, UTC_TIMESTAMP(3));
            """;
        try
        {
            await using MySqlConnection connection =
                await _connections.OpenConnectionAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.AddParameter("@action", action);
            command.AddParameter("@targetType", targetType);
            command.AddParameter("@targetId", targetId);
            command.AddParameter("@actorUserId", actorUserId);
            command.AddParameter("@beforeJson", beforeJson);
            command.AddParameter("@afterJson", afterJson);
            command.AddParameter("@traceId", traceId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "写入审计日志失败: action={Action}, targetType={TargetType}, targetId={TargetId}",
                action, targetType, targetId);
        }
    }
}

#pragma warning restore CS1591
