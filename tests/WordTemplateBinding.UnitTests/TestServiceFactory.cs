using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;
using WordTemplateBinding.Infrastructure.OpenXml;

namespace WordTemplateBinding.UnitTests;

/// <summary>
/// 集中创建使用默认配置的扫描器和渲染器测试实例。
/// </summary>
internal static class TestServiceFactory
{
    /// <summary>
    /// 创建默认模板处理配置。
    /// </summary>
    /// <returns>返回测试配置。</returns>
    internal static TemplateProcessingOptions CreateOptions() => new();

    /// <summary>
    /// 创建默认 Word 模板扫描器。
    /// </summary>
    /// <returns>返回扫描器实例。</returns>
    internal static IWordTemplateScanner CreateScanner()
    {
        TemplateProcessingOptions options = CreateOptions();
        return new WordTemplateScanner(
            new IMockDataRecognizer[]
            {
                new DecimalNumberRecognizer(options),
                new IntegerNumberRecognizer(options),
                new ExplicitTextRecognizer(options),
            },
            new LocatorIdGenerator(),
            new DocumentPreviewBuilder(),
            options);
    }

    /// <summary>
    /// 创建默认 Word 报告渲染器。
    /// </summary>
    /// <returns>返回渲染器实例。</returns>
    internal static IWordReportRenderer CreateRenderer() =>
        new WordReportRenderer(new DataValueFormatter(), CreateOptions());

    /// <summary>
    /// 根据 DOCX 字节和扫描结果创建模板领域对象。
    /// </summary>
    /// <param name="bytes">原始 DOCX 字节。</param>
    /// <param name="scanResult">扫描结果。</param>
    /// <returns>返回模板对象。</returns>
    internal static TemplateDocument CreateTemplate(
        byte[] bytes,
        TemplateScanResult scanResult)
    {
        DateTimeOffset now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        return new TemplateDocument(
            Guid.NewGuid(),
            "template.docx",
            bytes,
            scanResult.ContentHash,
            scanResult,
            now,
            now);
    }
}
