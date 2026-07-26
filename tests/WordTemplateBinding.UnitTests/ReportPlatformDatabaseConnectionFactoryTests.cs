using MySqlConnector;
using WordTemplateBinding.Infrastructure.Database;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 验证 Report Platform MySQL 连接配置。
/// </summary>
public sealed class ReportPlatformDatabaseConnectionFactoryTests
{
    /// <summary>
    /// 验证空占位配置不会被误判为可连接。
    /// </summary>
    [Fact]
    public void EmptyCredentials_AreReportedAsMissing()
    {
        ReportPlatformDatabaseConnectionFactory factory =
            new(new ReportPlatformDatabaseOptions());

        Assert.False(factory.IsConfigured);
        Assert.Equal(
            new[]
            {
                "Database:Host",
                "Database:Username",
                "Database:Password",
            },
            factory.MissingSettings);
        Assert.Equal("report_platform", factory.DatabaseName);
    }

    /// <summary>
    /// 验证完整配置会生成指向 report_platform 的池化连接。
    /// </summary>
    [Fact]
    public void CompleteConfiguration_CreatesExpectedConnection()
    {
        ReportPlatformDatabaseOptions options = new()
        {
            Host = "192.0.2.10",
            Username = "report_user",
            Password = "secret",
        };
        ReportPlatformDatabaseConnectionFactory factory = new(options);

        using MySqlConnection connection = factory.CreateConnection();
        MySqlConnectionStringBuilder builder =
            new(connection.ConnectionString);

        Assert.True(factory.IsConfigured);
        Assert.Equal("192.0.2.10", builder.Server);
        Assert.Equal((uint)3306, builder.Port);
        Assert.Equal("report_platform", builder.Database);
        Assert.Equal("report_user", builder.UserID);
        Assert.True(builder.Pooling);
        Assert.False(builder.AllowLoadLocalInfile);
    }
}
