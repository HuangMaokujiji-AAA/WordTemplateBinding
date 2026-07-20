using System.Text.RegularExpressions;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 使用统一的编译选项和超时创建模板扫描正则表达式。
/// </summary>
internal static class ConfiguredRegexFactory
{
    /// <summary>
    /// 创建正则表达式；包含前后查找等无回溯引擎不支持的语法时自动回退。
    /// </summary>
    /// <param name="pattern">正则表达式文本。</param>
    /// <param name="timeoutMilliseconds">匹配超时毫秒数。</param>
    /// <returns>返回配置完成的正则表达式。</returns>
    internal static Regex Create(string pattern, int timeoutMilliseconds)
    {
        RegexOptions commonOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);

        try
        {
            return new Regex(
                pattern,
                commonOptions | RegexOptions.NonBacktracking,
                timeout);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, commonOptions, timeout);
        }
        catch (ArgumentException)
        {
            return new Regex(pattern, commonOptions, timeout);
        }
    }
}
