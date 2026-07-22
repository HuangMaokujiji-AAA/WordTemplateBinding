namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示复用模板内嵌绑定清单中的一条图表绑定。
/// </summary>
public sealed record ReusableTemplateChartBinding
{
    /// <summary>
    /// 获取完整数据字段路径。
    /// </summary>
    public required string DataPath { get; init; }

    /// <summary>
    /// 获取图表部件 URI。
    /// </summary>
    public required string PartKey { get; init; }

    /// <summary>
    /// 获取主文档中的关系标识。
    /// </summary>
    public required string RelationshipId { get; init; }

    /// <summary>
    /// 获取图表在主文档中的出现顺序。
    /// </summary>
    public required int DocumentOrder { get; init; }
}

/// <summary>
/// 表示从 DOCX 自定义 XML 部件读取的本系统绑定清单。
/// </summary>
public sealed record ReusableTemplateManifest
{
    /// <summary>
    /// 获取当前支持的清单版本。
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// 获取可用于恢复图表绑定的清单项。
    /// </summary>
    public IReadOnlyList<ReusableTemplateChartBinding> ChartBindings { get; init; } =
        Array.Empty<ReusableTemplateChartBinding>();

    /// <summary>
    /// 获取读取清单时产生的非阻断警告。
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 表示上传或重新扫描复用模板后的自动恢复结果。
/// </summary>
public sealed record TemplateImportSummary
{
    /// <summary>
    /// 获取成功恢复的文本绑定数量。
    /// </summary>
    public int TextBindingsRestored { get; init; }

    /// <summary>
    /// 获取成功恢复的图表绑定数量。
    /// </summary>
    public int ChartBindingsRestored { get; init; }

    /// <summary>
    /// 获取当前 Schema 中不存在的占位符或清单字段路径。
    /// </summary>
    public IReadOnlyList<string> UnresolvedPlaceholders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取类型变化、清单损坏或图表定位失败等非阻断警告。
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取不包含任何恢复结果的摘要。
    /// </summary>
    public static TemplateImportSummary Empty { get; } = new();
}

/// <summary>
/// 表示导出完成的可复用 Word 模板。
/// </summary>
public sealed class RenderedTemplate
{
    private readonly byte[] _bytes;

    /// <summary>
    /// 初始化可复用模板并保存字节副本。
    /// </summary>
    /// <param name="bytes">生成的 DOCX 字节。</param>
    /// <param name="fileName">安全的下载文件名。</param>
    public RenderedTemplate(ReadOnlySpan<byte> bytes, string fileName)
    {
        _bytes = bytes.ToArray();
        FileName = fileName;
    }

    /// <summary>
    /// 获取安全的下载文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 返回导出模板字节的安全副本。
    /// </summary>
    /// <returns>返回独立的 DOCX 字节数组。</returns>
    public byte[] GetBytesCopy() => _bytes.ToArray();
}
