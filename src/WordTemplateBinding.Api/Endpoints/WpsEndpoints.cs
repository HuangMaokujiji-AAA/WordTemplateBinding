using WordTemplateBinding.Api.Services;

namespace WordTemplateBinding.Api.Endpoints;

/// <summary>
/// WPS 真实分页预览相关的 API 端点。
/// 提供 WPS 状态检测和 PDF 预览功能。
/// </summary>
public static class WpsEndpoints
{
    /// <summary>
    /// 注册 WPS 相关端点到 <see cref="WebApplication"/>。
    /// </summary>
    /// <param name="app">ASP.NET Core 应用构建器。</param>
    public static void MapWpsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wps").WithTags("WPS Preview");

        group.MapGet("/status", GetWpsStatus)
            .WithName("GetWpsStatus")
            .WithDescription("检测 WPS 自动化组件是否可用");

        group.MapGet("/capabilities", GetWpsCapabilities)
            .WithName("GetWpsCapabilities")
            .WithDescription("获取 WPS 预览功能能力");
    }

    private static IResult GetWpsStatus(WpsPdfConverter converter)
    {
        var status = converter.GetStatus();
        return Results.Ok(status);
    }

    private static IResult GetWpsCapabilities(WpsPdfConverter converter)
    {
        var status = converter.GetStatus();
        var capabilities = new WpsCapabilities
        {
            IsAvailable = status.IsAvailable,
            Features = new WpsFeatureFlags
            {
                RealPagination = status.IsAvailable,
                NativeCharts = status.IsAvailable,
                AuthenticFormatting = status.IsAvailable,
                BookmarkAnchors = true
            },
            Limitations = status.IsAvailable
                ? Array.Empty<string>()
                : new[] { "WPS 自动化组件不可用，请安装 Windows 桌面版 WPS" },
            Recommendations = status.IsAvailable
                ? Array.Empty<string>()
                : new[]
                {
                    "安装 WPS Office 桌面版",
                    "首次运行 WPS 并关闭欢迎界面",
                    "确保 WPS 可以正常打开 DOCX 文件"
                }
        };
        return Results.Ok(capabilities);
    }
}

/// <summary>
/// WPS 预览能力描述，用于前端判断可用的功能特性。
/// </summary>
public sealed class WpsCapabilities
{
    /// <summary>
    /// WPS 自动化组件是否可用。
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// 当前可用的具体功能开关集合。
    /// </summary>
    public WpsFeatureFlags Features { get; init; } = new();

    /// <summary>
    /// 已知的功能限制说明。
    /// </summary>
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 改善体验的推荐操作。
    /// </summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}

/// <summary>
/// WPS 各项具体功能的开关。
/// </summary>
public sealed class WpsFeatureFlags
{
    /// <summary>真实分页预览。</summary>
    public bool RealPagination { get; init; }

    /// <summary>原生图表渲染。</summary>
    public bool NativeCharts { get; init; }

    /// <summary>Word 原版排版还原。</summary>
    public bool AuthenticFormatting { get; init; }

    /// <summary>书签锚点定位。</summary>
    public bool BookmarkAnchors { get; init; }
}
