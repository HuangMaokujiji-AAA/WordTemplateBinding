using System.Runtime.InteropServices;

#if WINDOWS
using System.Threading;
#endif

namespace WordTemplateBinding.Api.Services;

/// <summary>
/// 通过 WPS 文字 COM 自动化把 DOCX 导出为 PDF，仅在 Windows 平台可用。
/// </summary>
public sealed class WpsPdfConverter
{
    private readonly string[] _progIds;
    private readonly TimeSpan _timeout;
    private readonly string _tempRoot;
    private readonly ILogger<WpsPdfConverter> _logger;
    private readonly bool _isWindows;

    /// <summary>
    /// 构造 <see cref="WpsPdfConverter"/>。
    /// </summary>
    /// <param name="configuration">应用配置，用于读取 WPS 配置段。</param>
    /// <param name="logger">日志记录器。</param>
    public WpsPdfConverter(
        IConfiguration configuration,
        ILogger<WpsPdfConverter> logger)
    {
        _logger = logger;

        _isWindows = OperatingSystem.IsWindows();

        _progIds = configuration.GetSection("Wps:ProgIds").Get<string[]>()
                   ?? new[] { "KWPS.Application", "wps.Application", "WPS.Application" };

        var timeoutSeconds = configuration.GetValue<int?>("Wps:TimeoutSeconds") ?? 90;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 600));

        var configuredRoot = configuration["Wps:TempRoot"];
        _tempRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WordTemplateBinding",
                "temp")
            : configuredRoot;

        Directory.CreateDirectory(_tempRoot);
        _logger.LogInformation("WPS PDF 转换器已初始化，临时目录: {TempRoot}", _tempRoot);
    }

    /// <summary>
    /// 获取当前环境下 WPS 的可用状态。
    /// </summary>
    /// <returns>WPS 状态信息。</returns>
    public WpsStatus GetStatus()
    {
        if (!_isWindows)
        {
            return new WpsStatus
            {
                IsWindows = false,
                IsAvailable = false,
                Message = "当前环境不是 Windows，无法使用 WPS 自动化组件。"
            };
        }

#if WINDOWS
        foreach (var progId in _progIds)
        {
            try
            {
                if (Type.GetTypeFromProgID(progId, throwOnError: false) is not null)
                {
                    _logger.LogInformation("检测到 WPS 自动化组件: {ProgId}", progId);
                    return new WpsStatus
                    {
                        IsWindows = true,
                        IsAvailable = true,
                        ProgId = progId,
                        Message = $"已检测到 WPS 自动化组件：{progId}"
                    };
                }
            }
            catch
            {
            }
        }

        _logger.LogWarning("未检测到 WPS 自动化组件");
        return new WpsStatus
        {
            IsWindows = true,
            IsAvailable = false,
            Message = "未检测到 WPS 文字 COM 自动化组件。请安装 Windows 桌面版 WPS。"
        };
#else
        return new WpsStatus
        {
            IsWindows = false,
            IsAvailable = false,
            Message = "WPS 自动化组件仅在 Windows 平台上可用。"
        };
#endif
    }

#if WINDOWS
    public async Task<PdfConversionResult> ConvertDocxToPdfAsync(
        byte[] docxBytes,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var jobDirectory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);

        var safeName = SanitizeFileName(
            Path.GetFileNameWithoutExtension(originalFileName),
            "template");

        var docxPath = Path.Combine(jobDirectory, safeName + ".docx");
        var pdfPath = Path.Combine(jobDirectory, safeName + ".pdf");

        _logger.LogInformation("开始转换 DOCX 到 PDF: {FileName}", originalFileName);

        await File.WriteAllBytesAsync(docxPath, docxBytes, cancellationToken);

        try
        {
            var conversionTask = RunStaAsync(() => ConvertCore(docxPath, pdfPath));
            var completed = await Task.WhenAny(
                conversionTask,
                Task.Delay(_timeout, cancellationToken));

            if (completed != conversionTask)
            {
                _logger.LogError("WPS 转换超时: {Timeout} 秒", _timeout.TotalSeconds);
                throw new TimeoutException(
                    $"WPS 转换超过 {_timeout.TotalSeconds:0} 秒。请检查 WPS 是否弹出了首次运行、文件修复或安全提示窗口。");
            }

            var result = await conversionTask;

            if (!File.Exists(pdfPath))
            {
                _logger.LogError("WPS 未生成 PDF 文件");
                throw new IOException("WPS 未生成 PDF 文件。");
            }

            _logger.LogInformation("PDF 转换成功，共 {PageCount} 页", result.PageCount);

            return new PdfConversionResult
            {
                PdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken),
                PageCount = result.PageCount,
                ProgId = result.ProgId
            };
        }
        finally
        {
            TryDeleteDirectory(jobDirectory);
        }
    }

    private (int PageCount, string ProgId) ConvertCore(
        string docxPath,
        string pdfPath)
    {
        object? appObject = null;
        object? documentObject = null;
        string? selectedProgId = null;

        try
        {
            foreach (var progId in _progIds)
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: false);
                if (type is null) continue;

                try
                {
                    appObject = Activator.CreateInstance(type);
                    if (appObject is not null)
                    {
                        selectedProgId = progId;
                        _logger.LogDebug("成功创建 WPS 应用对象: {ProgId}", progId);
                        break;
                    }
                }
                catch
                {
                    appObject = null;
                }
            }

            if (appObject is null || selectedProgId is null)
            {
                throw new InvalidOperationException(
                    "无法创建 WPS 文字自动化对象。请确认安装的是 Windows 桌面版 WPS。");
            }

            dynamic app = appObject;
            TrySet(() => app.Visible = false);
            TrySet(() => app.DisplayAlerts = 0);

            documentObject = app.Documents.Open(Path.GetFullPath(docxPath));
            dynamic document = documentObject;

            var pageCount = 1;
            try
            {
                pageCount = Math.Max(1, Convert.ToInt32(document.ComputeStatistics(2)));
            }
            catch
            {
                pageCount = 1;
            }

            try
            {
                document.ExportAsFixedFormat(Path.GetFullPath(pdfPath), 17);
            }
            catch
            {
                document.SaveAs2(Path.GetFullPath(pdfPath), 17);
            }

            WaitForFile(pdfPath, TimeSpan.FromSeconds(20));
            return (pageCount, selectedProgId);
        }
        finally
        {
            if (documentObject is not null)
            {
                try
                {
                    dynamic document = documentObject;
                    document.Close(false);
                }
                catch
                {
                }
            }

            if (appObject is not null)
            {
                try
                {
                    dynamic app = appObject;
                    app.Quit();
                }
                catch
                {
                }
            }

            ReleaseComObject(documentObject);
            ReleaseComObject(appObject);
        }
    }

    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var source = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                source.SetResult(action());
            }
            catch (Exception exception)
            {
                source.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "WPS PDF conversion"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return source.Task;
    }

    private static void TrySet(Action setter)
    {
        try
        {
            setter();
        }
        catch
        {
        }
    }

    private static void WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > 0) return;
                }
                catch
                {
                }
            }

            Thread.Sleep(200);
        }

        throw new IOException("等待 WPS 生成 PDF 超时。");
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null) return;

        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
        }
    }
#else
    /// <summary>
    /// 将 DOCX 字节转换为 PDF 字节。仅在 Windows 平台可用。
    /// </summary>
    /// <param name="docxBytes">DOCX 字节内容。</param>
    /// <param name="originalFileName">原始文件名，仅用于生成临时文件名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含 PDF 字节、页数与所用 ProgId 的结果。</returns>
    public Task<PdfConversionResult> ConvertDocxToPdfAsync(
        byte[] docxBytes,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException(
            "WPS PDF 转换仅在 Windows 平台上可用。");
    }
#endif

    private static string SanitizeFileName(string value, string fallback)
    {
        var result = string.Concat(
            value.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

/// <summary>
/// WPS 文字自动化组件的检测状态。
/// </summary>
public sealed class WpsStatus
{
    /// <summary>当前运行时是否为 Windows 平台。</summary>
    public bool IsWindows { get; init; }

    /// <summary>是否检测到 WPS 自动化组件。</summary>
    public bool IsAvailable { get; init; }

    /// <summary>实际生效的 ProgId（若可用）。</summary>
    public string? ProgId { get; init; }

    /// <summary>人类可读的状态描述。</summary>
    public string? Message { get; init; }
}

/// <summary>
/// DOCX 到 PDF 的转换结果。
/// </summary>
public sealed class PdfConversionResult
{
    /// <summary>生成的 PDF 字节。</summary>
    public byte[] PdfBytes { get; init; } = Array.Empty<byte>();

    /// <summary>PDF 总页数。</summary>
    public int PageCount { get; init; }

    /// <summary>实际使用的 ProgId。</summary>
    public string? ProgId { get; init; }
}
