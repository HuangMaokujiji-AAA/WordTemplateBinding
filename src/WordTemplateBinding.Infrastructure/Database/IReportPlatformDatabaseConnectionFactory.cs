using MySqlConnector;

namespace WordTemplateBinding.Infrastructure.Database;

/// <summary>
/// 创建并打开指向 Report Platform MySQL 数据库的连接。
/// </summary>
public interface IReportPlatformDatabaseConnectionFactory
{
    /// <summary>
    /// 获取当前是否已经填写全部必要连接参数。
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// 获取目标数据库名称。
    /// </summary>
    string DatabaseName { get; }

    /// <summary>
    /// 获取尚未填写的必要配置键。
    /// </summary>
    IReadOnlyList<string> MissingSettings { get; }

    /// <summary>
    /// 创建尚未打开的 MySQL 连接。
    /// </summary>
    /// <returns>返回使用统一连接池设置的连接。</returns>
    MySqlConnection CreateConnection();

    /// <summary>
    /// 创建并异步打开 MySQL 连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回已经打开的连接；调用方负责释放。</returns>
    Task<MySqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);
}
