namespace WordTemplateBinding.Infrastructure.Database;

/// <summary>
/// 定义 Report Platform MySQL 数据库连接配置。
/// </summary>
public sealed class ReportPlatformDatabaseOptions
{
    /// <summary>
    /// 获取配置节名称。
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// 获取或设置远程 MySQL 主机名或 IP 地址。
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置远程 MySQL 端口。
    /// </summary>
    public uint Port { get; set; } = 3306;

    /// <summary>
    /// 获取或设置数据库名称。
    /// </summary>
    public string Database { get; set; } = "report_platform";

    /// <summary>
    /// 获取或设置数据库用户账号。
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置数据库密码。该值不得写入日志或接口响应。
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 MySQL TLS 模式。
    /// </summary>
    public string SslMode { get; set; } = "Preferred";

    /// <summary>
    /// 获取或设置建立连接的超时秒数。
    /// </summary>
    public uint ConnectionTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 获取或设置数据库命令的默认超时秒数。
    /// </summary>
    public uint DefaultCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 获取或设置连接池最大连接数。
    /// </summary>
    public uint MaximumPoolSize { get; set; } = 50;
}
