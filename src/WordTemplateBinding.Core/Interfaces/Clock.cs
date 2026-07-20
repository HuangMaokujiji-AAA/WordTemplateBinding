namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 提供可替换的 UTC 系统时间来源。
/// </summary>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 时间。
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 使用系统时间的默认时钟实现。
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>
    /// 获取共享系统时钟实例。
    /// </summary>
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
