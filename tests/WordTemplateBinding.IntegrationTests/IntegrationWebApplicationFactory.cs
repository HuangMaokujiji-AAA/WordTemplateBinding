using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WordTemplateBinding.IntegrationTests;

/// <summary>
/// 创建 Testing 环境下的集成测试宿主。
/// 进程级隔离配置由 integration.runsettings 在应用创建前注入。
/// </summary>
public sealed class IntegrationWebApplicationFactory
    : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
