using WordTemplateBinding.Core.Enums;

namespace WordTemplateBinding.Core.Models;

/// <summary>
/// 表示模板扫描得到的一个模拟数据项。
/// </summary>
public sealed record MockDataItem
{
    /// <summary>
    /// 获取结构化定位信息生成的稳定标识。
    /// </summary>
    public required string LocatorId { get; init; }

    /// <summary>
    /// 获取模板中的原始模拟值。
    /// </summary>
    public required string MockValue { get; init; }

    /// <summary>
    /// 获取模拟数据类型。
    /// </summary>
    public required MockDataType DataType { get; init; }

    /// <summary>
    /// 获取模拟值的结构化定位信息。
    /// </summary>
    public required TextLocator Locator { get; init; }

    /// <summary>
    /// 获取模拟值所在段落的完整文本。
    /// </summary>
    public required string ParagraphText { get; init; }

    /// <summary>
    /// 获取对应的预览段落索引。
    /// </summary>
    public required int PreviewParagraphIndex { get; init; }

    /// <summary>
    /// 获取当前响应视图中是否已经绑定。
    /// </summary>
    public required bool IsBound { get; init; }

    /// <summary>
    /// 获取当前响应视图中绑定的数据字段路径。
    /// </summary>
    public string? BoundDataPath { get; init; }

    /// <summary>
    /// 获取显式双花括号标记中可用于 Schema 精确查找的候选路径。
    /// 普通数字识别结果为 <see langword="null"/>。
    /// </summary>
    public string? PlaceholderCandidatePath { get; init; }

    /// <summary>
    /// 获取识别来源。
    /// </summary>
    public string RecognitionKind { get; init; } = "Unknown";

    /// <summary>
    /// 获取包含当前标记的内容控件 Tag；不存在稳定 Tag 时为空。
    /// </summary>
    public string? ContentControlTag { get; init; }
}

/// <summary>
/// 表示一次 Word 模板扫描的完整结果。
/// </summary>
public sealed record TemplateScanResult
{
    /// <summary>
    /// 获取模板原始内容的 SHA-256 十六进制哈希。
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// 获取识别到的模拟数据项。
    /// </summary>
    public required IReadOnlyList<MockDataItem> MockItems { get; init; }

    /// <summary>
    /// 获取识别到的 Word 原生图表。
    /// </summary>
    public required IReadOnlyList<ChartTemplateItem> Charts { get; init; }

    /// <summary>
    /// 获取识别到的可重复填充 Word 表格。
    /// </summary>
    public IReadOnlyList<TableTemplateItem> Tables { get; init; } =
        Array.Empty<TableTemplateItem>();

    /// <summary>
    /// 获取结构化文本预览。
    /// </summary>
    public required DocumentPreview Preview { get; init; }

    /// <summary>
    /// 获取从本系统自定义 XML 部件读取的复用模板清单。
    /// </summary>
    public ReusableTemplateManifest BindingManifest { get; init; } = new();

    /// <summary>
    /// 获取扫描过程中产生的非阻断警告。
    /// </summary>
    public IReadOnlyList<TemplateParseWarning> Warnings { get; init; } =
        Array.Empty<TemplateParseWarning>();
}

/// <summary>
/// 表示保存在内存中的原始 Word 模板及其扫描状态。
/// </summary>
public sealed class TemplateDocument
{
    private readonly byte[] _originalBytes;

    /// <summary>
    /// 初始化模板文档，并对原始字节执行防御性复制。
    /// </summary>
    /// <param name="id">模板唯一标识。</param>
    /// <param name="originalFileName">净化后的原始文件名。</param>
    /// <param name="originalBytes">原始 DOCX 字节。</param>
    /// <param name="contentHash">模板内容哈希。</param>
    /// <param name="scanResult">模板扫描结果。</param>
    /// <param name="createdAt">创建时间。</param>
    /// <param name="updatedAt">更新时间。</param>
    /// <param name="importSummary">最近一次自动恢复摘要。</param>
    public TemplateDocument(
        Guid id,
        string originalFileName,
        ReadOnlySpan<byte> originalBytes,
        string contentHash,
        TemplateScanResult scanResult,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        TemplateImportSummary? importSummary = null)
    {
        Id = id;
        OriginalFileName = originalFileName;
        _originalBytes = originalBytes.ToArray();
        ContentHash = contentHash;
        ScanResult = scanResult;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        ImportSummary = importSummary ?? TemplateImportSummary.Empty;
    }

    /// <summary>
    /// 获取模板唯一标识。
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 获取净化后的原始文件名。
    /// </summary>
    public string OriginalFileName { get; }

    /// <summary>
    /// 获取模板内容哈希。
    /// </summary>
    public string ContentHash { get; }

    /// <summary>
    /// 获取当前扫描结果。
    /// </summary>
    public TemplateScanResult ScanResult { get; private set; }

    /// <summary>
    /// 获取模板创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 获取模板最近更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// 获取最近一次上传或重新扫描产生的自动绑定恢复摘要。
    /// </summary>
    public TemplateImportSummary ImportSummary { get; private set; }

    /// <summary>
    /// 返回原始模板字节的安全副本。
    /// </summary>
    /// <returns>返回新的字节数组，调用方修改该数组不会影响模板。</returns>
    public byte[] GetOriginalBytesCopy() => _originalBytes.ToArray();

    /// <summary>
    /// 使用新的扫描结果更新模板状态。
    /// </summary>
    /// <param name="scanResult">新的扫描结果。</param>
    /// <param name="updatedAt">本次更新时间。</param>
    public void UpdateScanResult(TemplateScanResult scanResult, DateTimeOffset updatedAt)
    {
        ScanResult = scanResult;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// 更新最近一次自动绑定恢复摘要。
    /// </summary>
    /// <param name="importSummary">恢复摘要。</param>
    public void UpdateImportSummary(TemplateImportSummary importSummary)
    {
        ImportSummary = importSummary;
    }

    /// <summary>
    /// 创建包含独立字节数组的模板快照。
    /// </summary>
    /// <returns>返回可安全交给调用方的模板副本。</returns>
    public TemplateDocument CreateSnapshot() =>
        new(
            Id,
            OriginalFileName,
            _originalBytes,
            ContentHash,
            ScanResult,
            CreatedAt,
            UpdatedAt,
            ImportSummary);
}

/// <summary>
/// 表示模板模拟值与数据字段之间的一条绑定关系。
/// </summary>
public sealed record TemplateBinding
{
    /// <summary>
    /// 获取所属模板标识。
    /// </summary>
    public required Guid TemplateId { get; init; }

    /// <summary>
    /// 获取绑定目标类型。
    /// </summary>
    public BindingTargetKind TargetKind { get; init; } = BindingTargetKind.Text;

    /// <summary>
    /// 获取模拟数据定位标识。
    /// </summary>
    public required string LocatorId { get; init; }

    /// <summary>
    /// 获取数据字段路径。
    /// </summary>
    public required string DataPath { get; init; }

    /// <summary>
    /// 获取数据字段类型。
    /// </summary>
    public required DataValueType DataType { get; init; }

    /// <summary>
    /// 获取图表字段映射配置，仅当 TargetKind=Chart 时有效。
    /// </summary>
    public ChartBindingMapping? ChartMapping { get; init; }

    /// <summary>
    /// 获取表格列映射和过滤配置，仅当 TargetKind=Table 时有效。
    /// </summary>
    public TableBindingMapping? TableMapping { get; init; }

    /// <summary>
    /// 获取绑定创建时间。
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 获取绑定更新时间。
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// 表示生成完成的 Word 报告。
/// </summary>
public sealed class RenderedReport
{
    private readonly byte[] _bytes;

    /// <summary>
    /// 初始化生成报告并保存字节副本。
    /// </summary>
    /// <param name="bytes">生成的 DOCX 字节。</param>
    /// <param name="fileName">安全的下载文件名。</param>
    public RenderedReport(ReadOnlySpan<byte> bytes, string fileName)
    {
        _bytes = bytes.ToArray();
        FileName = fileName;
    }

    /// <summary>
    /// 获取安全的下载文件名。
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// 返回生成报告字节的安全副本。
    /// </summary>
    /// <returns>返回独立的 DOCX 字节数组。</returns>
    public byte[] GetBytesCopy() => _bytes.ToArray();
}
