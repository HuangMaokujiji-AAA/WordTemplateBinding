#pragma warning disable CS1591
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.DataSchema;

/// <summary>
/// 从配置文件 ApplicationIdentity:DefaultActorUserId 读取当前操作人。
/// 后续接入认证系统时替换为从 HttpContext 读取的实现。
/// </summary>
public sealed class DevelopmentCurrentUserContext : ICurrentUserContext
{
    private readonly Lazy<ulong> _userId;

    public DevelopmentCurrentUserContext(ApplicationIdentityOptions options)
    {
        _userId = new Lazy<ulong>(() =>
        {
            if (string.IsNullOrWhiteSpace(options.DefaultActorUserId) ||
                !ulong.TryParse(options.DefaultActorUserId, out ulong parsed) ||
                parsed == 0)
            {
                throw new InvalidOperationException(
                    "ApplicationIdentity:DefaultActorUserId 必须配置为大于 0 的无符号整数。");
            }

            return parsed;
        });
    }

    public ulong UserId => _userId.Value;
}

#pragma warning restore CS1591
