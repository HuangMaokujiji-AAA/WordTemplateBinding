#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 提供当前请求或会话的操作人用户标识。
/// 后续接入认证系统时替换实现即可，无需修改业务服务。
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// 获取当前操作人用户 ID。
    /// </summary>
    ulong UserId { get; }
}

#pragma warning restore CS1591
