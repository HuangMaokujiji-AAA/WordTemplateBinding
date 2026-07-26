using WordTemplateBinding.Infrastructure.Database;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// 提供远程数据库连接诊断接口。
/// </summary>
public static class DatabaseEndpoints
{
    /// <summary>
    /// 映射 Report Platform 数据库健康检查接口。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    public static void MapDatabaseEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/system/database/health",
                CheckDatabaseAsync)
            .WithName("CheckReportPlatformDatabase")
            .WithTags("System");
    }

    private static async Task<IResult> CheckDatabaseAsync(
        IReportPlatformDatabaseConnectionFactory connectionFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!connectionFactory.IsConfigured)
        {
            DatabaseHealthResponse response = new(
                Status: "not_configured",
                Provider: "MySQL",
                Database: connectionFactory.DatabaseName,
                ServerVersion: null,
                MissingSettings: connectionFactory.MissingSettings,
                Message: "数据库连接参数尚未填写。");
            return Results.Json(
                response,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection =
                await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DATABASE(), VERSION();";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            bool hasRow = await reader.ReadAsync(cancellationToken);

            DatabaseHealthResponse response = new(
                Status: hasRow ? "healthy" : "unhealthy",
                Provider: "MySQL",
                Database: hasRow && !reader.IsDBNull(0)
                    ? reader.GetString(0)
                    : connectionFactory.DatabaseName,
                ServerVersion: hasRow && !reader.IsDBNull(1)
                    ? reader.GetString(1)
                    : null,
                MissingSettings: Array.Empty<string>(),
                Message: hasRow
                    ? "数据库连接成功。"
                    : "数据库已连接，但探测查询没有返回结果。");
            return Results.Json(
                response,
                statusCode: hasRow
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ILogger logger =
                loggerFactory.CreateLogger("ReportPlatformDatabaseHealth");
            logger.LogWarning(
                exception,
                "连接 Report Platform 数据库失败。Database={Database}",
                connectionFactory.DatabaseName);

            DatabaseHealthResponse response = new(
                Status: "unavailable",
                Provider: "MySQL",
                Database: connectionFactory.DatabaseName,
                ServerVersion: null,
                MissingSettings: Array.Empty<string>(),
                Message: "无法连接数据库，请检查服务器地址、端口、账号、密码、TLS 和防火墙设置。");
            return Results.Json(
                response,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record DatabaseHealthResponse(
        string Status,
        string Provider,
        string Database,
        string? ServerVersion,
        IReadOnlyList<string> MissingSettings,
        string Message);
}
