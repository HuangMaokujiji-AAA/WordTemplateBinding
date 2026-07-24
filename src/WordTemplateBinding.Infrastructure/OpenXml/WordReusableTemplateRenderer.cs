using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 在原始 DOCX 副本中写入字段路径占位符和图表绑定清单。
/// </summary>
public sealed class WordReusableTemplateRenderer : IWordReusableTemplateRenderer
{
    private readonly OpenXmlTextReplacementService _textReplacementService;

    /// <summary>
    /// 初始化复用模板渲染器。
    /// </summary>
    /// <param name="options">模板定位配置。</param>
    public WordReusableTemplateRenderer(TemplateProcessingOptions options)
    {
        _textReplacementService = new OpenXmlTextReplacementService(options);
    }

    /// <inheritdoc />
    public async Task<RenderedTemplate> RenderAsync(
        TemplateDocument template,
        IReadOnlyCollection<TemplateBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ValidateBindings(template, bindings);
            byte[] originalBytes = template.GetOriginalBytesCopy();
            using MemoryStream stream = new(originalBytes.Length + 4096);
            await stream.WriteAsync(originalBytes, cancellationToken);
            stream.Position = 0;

            using (WordprocessingDocument document = WordprocessingDocument.Open(stream, true))
            {
                MainDocumentPart mainPart = document.MainDocumentPart
                    ?? throw new ReusableTemplateRenderingException("模板缺少主文档部件。");
                Dictionary<string, MockDataItem> mockItems = template.ScanResult.MockItems
                    .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);
                Dictionary<string, ChartTemplateItem> chartItems = template.ScanResult.Charts
                    .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);

                ValidateChartLocators(mainPart, bindings, chartItems);
                IReadOnlyList<OpenXmlTextReplacement> replacements = bindings
                    .Where(binding => binding.TargetKind == BindingTargetKind.Text)
                    .Select(binding =>
                    {
                        MockDataItem item = mockItems[binding.LocatorId];
                        return new OpenXmlTextReplacement(
                            binding.LocatorId,
                            item.Locator,
                            BuildPlaceholder(binding.DataPath),
                            RemoveYellowHighlight: true);
                    })
                    .ToList()
                    .AsReadOnly();
                _textReplacementService.ReplaceAll(mainPart, replacements, cancellationToken);
                ReusableTemplateManifestSerializer.Write(
                    mainPart,
                    bindings,
                    mockItems,
                    chartItems);
            }

            return new RenderedTemplate(
                stream.ToArray(),
                BuildDownloadFileName(template.OriginalFileName));
        }
        catch (ReusableTemplateRenderingException)
        {
            throw;
        }
        catch (WordTemplateBindingException exception)
        {
            throw new ReusableTemplateRenderingException(
                $"无法导出复用模板：{exception.Message}",
                exception);
        }
        catch (OpenXmlPackageException exception)
        {
            throw new ReusableTemplateRenderingException(
                "导出复用模板时 OpenXML 包处理失败。",
                exception);
        }
        catch (FileFormatException exception)
        {
            throw new ReusableTemplateRenderingException(
                "导出复用模板时发现模板格式无效。",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new ReusableTemplateRenderingException(
                "导出复用模板时压缩包数据无效。",
                exception);
        }
        catch (IOException exception)
        {
            throw new ReusableTemplateRenderingException(
                "导出复用模板时发生流读写错误。",
                exception);
        }
    }

    private static void ValidateBindings(
        TemplateDocument template,
        IReadOnlyCollection<TemplateBinding> bindings)
    {
        Dictionary<string, MockDataItem> mockItems = template.ScanResult.MockItems
            .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);
        Dictionary<string, ChartTemplateItem> chartItems = template.ScanResult.Charts
            .ToDictionary(item => item.LocatorId, StringComparer.Ordinal);
        HashSet<string> locators = new(StringComparer.Ordinal);

        foreach (TemplateBinding binding in bindings)
        {
            if (binding.TemplateId != template.Id || !locators.Add(binding.LocatorId))
            {
                throw new ReusableTemplateRenderingException("复用模板绑定集合包含重复或不属于当前模板的定位。");
            }

            ValidateDataPath(binding.DataPath);
            bool validTarget = binding.TargetKind switch
            {
                BindingTargetKind.Text => mockItems.ContainsKey(binding.LocatorId),
                BindingTargetKind.Chart => chartItems.ContainsKey(binding.LocatorId),
                _ => false,
            };
            if (!validTarget)
            {
                throw new ReusableTemplateRenderingException(
                    $"绑定定位 {binding.LocatorId} 已失效，复用模板未导出。");
            }
        }
    }

    private static void ValidateChartLocators(
        MainDocumentPart mainPart,
        IEnumerable<TemplateBinding> bindings,
        IReadOnlyDictionary<string, ChartTemplateItem> chartItems)
    {
        foreach (TemplateBinding binding in bindings.Where(
                     item => item.TargetKind == BindingTargetKind.Chart))
        {
            ChartTemplateItem chart = chartItems[binding.LocatorId];
            ChartPart? partByUri = mainPart.ChartParts.FirstOrDefault(part =>
                string.Equals(part.Uri.OriginalString, chart.Locator.PartKey, StringComparison.Ordinal));
            OpenXmlPart? partByRelationship = mainPart.Parts
                .FirstOrDefault(part => string.Equals(
                    part.RelationshipId,
                    chart.Locator.RelationshipId,
                    StringComparison.Ordinal))
                .OpenXmlPart;
            if (partByUri is null || !ReferenceEquals(partByUri, partByRelationship))
            {
                throw new ReusableTemplateRenderingException(
                    $"图表绑定定位 {binding.LocatorId} 已失效，复用模板未导出。");
            }
        }
    }

    private static string BuildPlaceholder(string dataPath)
    {
        ValidateDataPath(dataPath);
        return "{{" + dataPath + "}}";
    }

    private static void ValidateDataPath(string dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath) ||
            !string.Equals(dataPath, dataPath.Trim(), StringComparison.Ordinal) ||
            dataPath.Contains("{{", StringComparison.Ordinal) ||
            dataPath.Contains("}}", StringComparison.Ordinal) ||
            dataPath.Any(character => character is '\r' or '\n' || char.IsControl(character)))
        {
            throw new ReusableTemplateRenderingException(
                $"字段路径“{dataPath}”不能生成合法的复用模板占位符。");
        }
    }

    private static string BuildDownloadFileName(string originalFileName)
    {
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(originalFileName));
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        invalidCharacters.UnionWith("<>:\"/\\|?*");
        string sanitized = new(stem
            .Where(character => !invalidCharacters.Contains(character) && !char.IsControl(character))
            .ToArray());
        sanitized = sanitized.Trim();
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..100];
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "template";
        }

        return sanitized.EndsWith("-template", StringComparison.OrdinalIgnoreCase)
            ? $"{sanitized}.docx"
            : $"{sanitized}-template.docx";
    }
}
