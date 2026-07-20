using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 负责模板上传、获取与重新扫描的业务编排。
/// </summary>
public sealed class TemplateWorkflowService
{
    private readonly ITemplateStore _templateStore;
    private readonly IBindingStore _bindingStore;
    private readonly IWordTemplateScanner _scanner;
    private readonly TemplateProcessingOptions _options;
    private readonly IClock _clock;

    /// <summary>
    /// 初始化模板业务服务。
    /// </summary>
    /// <param name="templateStore">模板存储。</param>
    /// <param name="bindingStore">绑定存储。</param>
    /// <param name="scanner">Word 模板扫描器。</param>
    /// <param name="options">模板处理配置。</param>
    /// <param name="clock">系统时间来源。</param>
    public TemplateWorkflowService(
        ITemplateStore templateStore,
        IBindingStore bindingStore,
        IWordTemplateScanner scanner,
        TemplateProcessingOptions options,
        IClock clock)
    {
        _templateStore = templateStore;
        _bindingStore = bindingStore;
        _scanner = scanner;
        _options = options;
        _clock = clock;
    }

    /// <summary>
    /// 验证并保存一个新的 DOCX 模板。
    /// </summary>
    /// <param name="fileName">客户端提供的文件名。</param>
    /// <param name="templateBytes">上传得到的模板字节。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回已保存的模板快照。</returns>
    /// <exception cref="InvalidTemplateFileException">扩展名、文件名或内容无效时抛出。</exception>
    /// <exception cref="TemplateTooLargeException">文件超过配置限制时抛出。</exception>
    /// <exception cref="NoMockDataFoundException">未识别到受支持的模拟数据时抛出。</exception>
    public async Task<TemplateDocument> UploadAsync(
        string fileName,
        ReadOnlyMemory<byte> templateBytes,
        CancellationToken cancellationToken = default)
    {
        string safeFileName = SanitizeUploadFileName(fileName);
        ValidateSize(templateBytes.Length);

        TemplateScanResult scanResult = await _scanner.ScanAsync(templateBytes, cancellationToken);
        EnsureMockItemsExist(scanResult);

        DateTimeOffset now = _clock.UtcNow;
        TemplateDocument template = new(
            Guid.NewGuid(),
            safeFileName,
            templateBytes.Span,
            scanResult.ContentHash,
            scanResult,
            now,
            now);

        await _templateStore.SaveAsync(template, cancellationToken);
        return template.CreateSnapshot();
    }

    /// <summary>
    /// 获取指定模板。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回模板快照。</returns>
    /// <exception cref="TemplateNotFoundException">找不到模板时抛出。</exception>
    public async Task<TemplateDocument> GetAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        return await _templateStore.GetAsync(templateId, cancellationToken)
            ?? throw new TemplateNotFoundException(templateId);
    }

    /// <summary>
    /// 使用不可变原始字节重新扫描模板，并删除已经失效的绑定。
    /// </summary>
    /// <param name="templateId">模板唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回重新扫描后的模板快照。</returns>
    /// <exception cref="TemplateNotFoundException">找不到模板时抛出。</exception>
    /// <exception cref="NoMockDataFoundException">重新扫描未识别到模拟数据时抛出。</exception>
    public async Task<TemplateDocument> RescanAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        TemplateDocument template = await GetAsync(templateId, cancellationToken);
        TemplateScanResult scanResult = await _scanner.ScanAsync(
            template.GetOriginalBytesCopy(),
            cancellationToken);
        EnsureMockItemsExist(scanResult);

        HashSet<string> validLocatorIds = scanResult.MockItems
            .Select(item => item.LocatorId)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<TemplateBinding> bindings =
            await _bindingStore.GetByTemplateAsync(templateId, cancellationToken);

        foreach (TemplateBinding binding in bindings.Where(
                     binding => !validLocatorIds.Contains(binding.LocatorId)))
        {
            await _bindingStore.DeleteAsync(templateId, binding.LocatorId, cancellationToken);
        }

        template.UpdateScanResult(scanResult, _clock.UtcNow);
        await _templateStore.SaveAsync(template, cancellationToken);
        return template.CreateSnapshot();
    }

    /// <summary>
    /// 将客户端文件名转换为仅包含文件名部分的安全 DOCX 名称。
    /// </summary>
    /// <param name="fileName">客户端文件名。</param>
    /// <returns>返回安全文件名。</returns>
    private static string SanitizeUploadFileName(string fileName)
    {
        string normalizedFileName = (fileName ?? string.Empty).Replace('\\', '/');
        string safeFileName = Path.GetFileName(normalizedFileName).Trim();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            !string.Equals(Path.GetExtension(safeFileName), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidTemplateFileException("只允许上传扩展名为 .docx 的文件。");
        }

        return safeFileName;
    }

    /// <summary>
    /// 校验上传字节长度，避免空文件和超限文件进入 OpenXML 扫描。
    /// </summary>
    /// <param name="length">上传字节长度。</param>
    private void ValidateSize(int length)
    {
        if (length == 0)
        {
            throw new InvalidTemplateFileException("上传的 DOCX 文件不能为空。");
        }

        long maxBytes = _options.MaxUploadSizeMb * 1024L * 1024L;
        if (length > maxBytes)
        {
            throw new TemplateTooLargeException(_options.MaxUploadSizeMb);
        }
    }

    /// <summary>
    /// 确保扫描结果能够进入第一阶段绑定流程。
    /// </summary>
    /// <param name="scanResult">模板扫描结果。</param>
    private static void EnsureMockItemsExist(TemplateScanResult scanResult)
    {
        if (scanResult.MockItems.Count == 0)
        {
            throw new NoMockDataFoundException();
        }
    }
}
