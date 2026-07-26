using System.Globalization;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Interfaces;

/// <summary>
/// 定义 Word 模板扫描能力。
/// </summary>
public interface IWordTemplateScanner
{
    /// <summary>
    /// 从可定位的 DOCX 流扫描模板；扫描器不负责文件存储。
    /// </summary>
    /// <param name="seekableDocxStream">可读、可定位的 DOCX 流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板扫描结果。</returns>
    Task<TemplateScanResult> ScanAsync(
        Stream seekableDocxStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描原始 DOCX 字节并构建模拟数据与预览结果。
    /// </summary>
    /// <param name="templateBytes">原始模板字节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板扫描结果。</returns>
    Task<TemplateScanResult> ScanAsync(
        ReadOnlyMemory<byte> templateBytes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义基于模板副本生成 Word 报告的能力。
/// </summary>
public interface IWordReportRenderer
{
    /// <summary>
    /// 根据模板、绑定关系和数据值生成独立报告。
    /// </summary>
    /// <param name="template">原始模板快照。</param>
    /// <param name="bindings">当前绑定关系。</param>
    /// <param name="values">按字段路径索引的数据值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回生成的 DOCX 报告。</returns>
    Task<RenderedReport> RenderAsync(
        TemplateDocument template,
        IReadOnlyCollection<TemplateBinding> bindings,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义数据字段树及字段查找能力。
/// </summary>
public interface IDataSchemaProvider
{
    /// <summary>
    /// 获取完整数据字段树。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回数据字段根节点。</returns>
    Task<IReadOnlyList<DataFieldNode>> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按路径精确查找字段。
    /// </summary>
    /// <param name="dataPath">字段路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回字段定义；字段不存在时返回 <see langword="null"/>。</returns>
    Task<DataFieldDefinition?> FindByPathAsync(
        string dataPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称或路径搜索字段节点。
    /// </summary>
    /// <param name="query">搜索文本。</param>
    /// <param name="maxResults">最大返回数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回字段搜索结果。</returns>
    Task<DataSchemaSearchResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取字段树中的叶子字段数量。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回叶子字段总数。</returns>
    Task<int> GetLeafCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义演示数据值来源。
/// </summary>
public interface IDataValueProvider
{
    /// <summary>
    /// 获取按字段路径索引的演示值快照。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回只读数据字典。</returns>
    Task<IReadOnlyDictionary<string, object?>> GetValuesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义结构化定位标识生成能力。
/// </summary>
public interface ILocatorIdGenerator
{
    /// <summary>
    /// 根据模板哈希和结构化定位信息生成稳定标识。
    /// </summary>
    /// <param name="templateHash">模板内容哈希。</param>
    /// <param name="locator">结构化定位信息。</param>
    /// <returns>返回 Base64Url 编码的 SHA-256 标识。</returns>
    string Generate(string templateHash, TextLocator locator);

    /// <summary>
    /// 根据模板哈希和图表定位信息生成稳定标识。
    /// </summary>
    /// <param name="templateHash">模板内容哈希。</param>
    /// <param name="locator">图表定位信息。</param>
    /// <returns>返回 Base64Url 编码的 SHA-256 标识。</returns>
    string Generate(string templateHash, ChartLocator locator);
}

/// <summary>
/// 定义从已绑定模板导出可重复使用 DOCX 模板的能力。
/// </summary>
public interface IWordReusableTemplateRenderer
{
    /// <summary>
    /// 将文本绑定写成字段路径占位符，并将图表绑定写入内嵌清单。
    /// </summary>
    /// <param name="template">原始模板快照。</param>
    /// <param name="bindings">当前绑定关系。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回独立的可复用 DOCX 模板。</returns>
    Task<RenderedTemplate> RenderAsync(
        TemplateDocument template,
        IReadOnlyCollection<TemplateBinding> bindings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义根据显式占位符和图表清单恢复绑定关系的能力。
/// </summary>
public interface ITemplateAutoBindingResolver
{
    /// <summary>
    /// 为扫描完成的模板恢复当前 Schema 中仍然有效的绑定。
    /// </summary>
    /// <param name="template">已经保存并具有新 Locator 的模板。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回本次恢复摘要与非阻断警告。</returns>
    Task<TemplateImportSummary> ResolveAsync(
        TemplateDocument template,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义从扫描结果构建文档预览的能力。
/// </summary>
public interface IDocumentPreviewBuilder
{
    /// <summary>
    /// 根据段落文本和模拟数据构建结构化预览。
    /// </summary>
    /// <param name="paragraphTexts">按文档顺序排列的段落文本。</param>
    /// <param name="mockItems">识别到的模拟数据。</param>
    /// <returns>返回结构化文档预览。</returns>
    DocumentPreview Build(
        IReadOnlyList<string> paragraphTexts,
        IReadOnlyList<MockDataItem> mockItems);
}

/// <summary>
/// 定义数据值到 Word 文本的安全格式化能力。
/// </summary>
public interface IDataValueFormatter
{
    /// <summary>
    /// 按声明类型和区域文化格式化数据值。
    /// </summary>
    /// <param name="value">需要格式化的值。</param>
    /// <param name="valueType">字段声明类型。</param>
    /// <param name="culture">格式化使用的区域文化。</param>
    /// <returns>返回可写入 Word 文本节点的字符串。</returns>
    string Format(object? value, DataValueType valueType, CultureInfo culture);
}
