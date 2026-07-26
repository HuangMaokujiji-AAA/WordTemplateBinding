using MySqlConnector;

namespace WordTemplateBinding.Infrastructure.Database;

/// <summary>
/// 基于应用配置创建 Report Platform MySQL 连接。
/// </summary>
public sealed class ReportPlatformDatabaseConnectionFactory
    : IReportPlatformDatabaseConnectionFactory
{
    private readonly ReportPlatformDatabaseOptions _options;

    /// <summary>
    /// 初始化数据库连接工厂。
    /// </summary>
    /// <param name="options">数据库连接配置。</param>
    public ReportPlatformDatabaseConnectionFactory(
        ReportPlatformDatabaseOptions options)
    {
        _options = options;
        MissingSettings = FindMissingSettings(options).AsReadOnly();
    }

    /// <inheritdoc />
    public bool IsConfigured => MissingSettings.Count == 0;

    /// <inheritdoc />
    public string DatabaseName => _options.Database;

    /// <inheritdoc />
    public IReadOnlyList<string> MissingSettings { get; }

    /// <inheritdoc />
    public MySqlConnection CreateConnection()
    {
        EnsureConfigured();

        if (!Enum.TryParse(
                _options.SslMode,
                ignoreCase: true,
                out MySqlSslMode sslMode))
        {
            throw new InvalidOperationException(
                $"Database:SslMode 的值“{_options.SslMode}”无效。");
        }

        if (_options.Port == 0)
        {
            throw new InvalidOperationException("Database:Port 必须大于 0。");
        }

        if (_options.MaximumPoolSize == 0)
        {
            throw new InvalidOperationException(
                "Database:MaximumPoolSize 必须大于 0。");
        }

        MySqlConnectionStringBuilder builder = new()
        {
            Server = _options.Host.Trim(),
            Port = _options.Port,
            Database = _options.Database.Trim(),
            UserID = _options.Username.Trim(),
            Password = _options.Password,
            SslMode = sslMode,
            ConnectionTimeout = _options.ConnectionTimeoutSeconds,
            DefaultCommandTimeout = _options.DefaultCommandTimeoutSeconds,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = _options.MaximumPoolSize,
            ConnectionReset = true,
            PersistSecurityInfo = false,
            AllowLoadLocalInfile = false,
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    /// <inheritdoc />
    public async Task<MySqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        MySqlConnection connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static List<string> FindMissingSettings(
        ReportPlatformDatabaseOptions options)
    {
        List<string> missing = new();
        AddIfBlank(missing, "Database:Host", options.Host);
        AddIfBlank(missing, "Database:Database", options.Database);
        AddIfBlank(missing, "Database:Username", options.Username);
        AddIfBlank(missing, "Database:Password", options.Password);
        return missing;
    }

    private static void AddIfBlank(
        ICollection<string> missing,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(key);
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                $"数据库连接尚未配置：{string.Join(", ", MissingSettings)}。");
        }
    }
}
