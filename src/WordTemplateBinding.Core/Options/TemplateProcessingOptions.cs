using System.ComponentModel.DataAnnotations;

namespace WordTemplateBinding.Core.Options;

/// <summary>
/// 表示模板上传、扫描和定位过程的配置。
/// </summary>
public sealed class TemplateProcessingOptions
{
    /// <summary>
    /// 获取或设置允许上传的最大文件大小，单位为 MB。
    /// </summary>
    [Range(1, 100)]
    public int MaxUploadSizeMb { get; set; } = 20;

    /// <summary>
    /// 获取或设置用于识别小数模拟数据的正则表达式。
    /// </summary>
    [Required]
    public string MockNumberPattern { get; set; } =
        @"(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\.[0-9]+(?![A-Za-z0-9_.,])";

    /// <summary>
    /// 获取或设置用于识别整数模拟数据的正则表达式。
    /// </summary>
    [Required]
    public string MockIntegerPattern { get; set; } =
        @"(?<![A-Za-z0-9_.,-])-?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?![A-Za-z0-9_.,])";

    /// <summary>
    /// 获取或设置用于识别显式文本标记的正则表达式，必须包含名为 value 的捕获组。
    /// </summary>
    [Required]
    public string MockTextPattern { get; set; } =
        @"\{\{(?:text:(?<value>[^{}\r\n]+)|(?<value>[^{}:\r\n]+))\}\}";

    /// <summary>
    /// 获取或设置计算上下文哈希时在匹配值两侧保留的字符数。
    /// </summary>
    [Range(0, 200)]
    public int ContextLength { get; set; } = 20;

    /// <summary>
    /// 获取或设置单次正则匹配的超时时间，单位为毫秒。
    /// </summary>
    [Range(50, 5000)]
    public int RegexTimeoutMilliseconds { get; set; } = 250;

    /// <summary>
    /// 获取或设置单张图表最多分析的系列数量。
    /// </summary>
    [Range(1, 500)]
    public int MaxChartSeries { get; set; } = 100;

    /// <summary>
    /// 获取或设置每系列最多分析的数据点数量。
    /// </summary>
    [Range(1, 100000)]
    public int MaxChartPointsPerSeries { get; set; } = 20000;

    /// <summary>
    /// 获取或设置图表分析 JSON 的最大字节数（用于截断检测）。
    /// </summary>
    [Range(1024, 50_000_000)]
    public int MaxChartAnalysisJsonBytes { get; set; } = 5_242_880;
}
