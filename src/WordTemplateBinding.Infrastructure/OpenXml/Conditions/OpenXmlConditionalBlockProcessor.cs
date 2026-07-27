using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Conditions;

/// <summary>
/// 处理条件块（wtb:condition:{blockKey}），根据数据条件显示或删除内容。
/// 处理顺序必须位于 Repeat 展开之后。
/// </summary>
public sealed class OpenXmlConditionalBlockProcessor
{
    private readonly IConditionEvaluator _evaluator;
    private readonly IDataContextResolver _resolver;

    /// <summary>
    /// 初始化条件块处理器。
    /// </summary>
    /// <param name="evaluator">条件求值器。</param>
    /// <param name="resolver">数据上下文解析器。</param>
    public OpenXmlConditionalBlockProcessor(
        IConditionEvaluator evaluator,
        IDataContextResolver resolver)
    {
        _evaluator = evaluator;
        _resolver = resolver;
    }

    /// <summary>
    /// 处理文档中所有条件块。
    /// </summary>
    /// <param name="mainPart">主文档部件。</param>
    /// <param name="definitions">条件块定义列表。</param>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回处理结果摘要。</returns>
    public Task<ConditionProcessingResult> ProcessAsync(
        MainDocumentPart mainPart,
        IReadOnlyList<ConditionBlockDefinition> definitions,
        RenderScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mainPart);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(scope);

        int processed = 0;
        int kept = 0;
        int removed = 0;
        List<string> errors = new();

        foreach (ConditionBlockDefinition definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string tagValue = $"wtb:condition:{definition.BlockKey}";
            List<SdtElement> matchingElements = FindSdtElementsByTag(mainPart, tagValue);

            if (matchingElements.Count == 0)
            {
                continue;
            }

            // Evaluate the condition in the current scope
            ConditionEvaluationResult evalResult = _evaluator.Evaluate(
                definition, scope, _resolver);

            foreach (SdtElement sdtElement in matchingElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (evalResult.Success && evalResult.Result)
                {
                    // Condition is true — unwrap the Sdt, keep inner content
                    UnwrapSdtElement(sdtElement);
                    kept++;
                }
                else if (evalResult.Success && !evalResult.Result)
                {
                    // Condition is false — handle per falseBehavior
                    ProcessFalseCondition(sdtElement, definition, errors);
                    removed++;
                }
                else
                {
                    // Evaluation failed — handle per onError
                    ProcessEvaluationError(sdtElement, definition, evalResult, errors);
                    if (evalResult.Result)
                    {
                        kept++;
                    }
                    else
                    {
                        removed++;
                    }
                }

                processed++;
            }
        }

        return Task.FromResult(new ConditionProcessingResult
        {
            ProcessedCount = processed,
            KeptCount = kept,
            RemovedCount = removed,
            Errors = errors,
        });
    }

    /// <summary>
    /// 展开 SdtElement，保留内部内容并移除内容控件外壳。
    /// </summary>
    private static void UnwrapSdtElement(SdtElement sdtElement)
    {
        OpenXmlElement? parent = sdtElement.Parent;
        if (parent is null)
        {
            return;
        }

        // Get all content inside the SdtElement
        List<OpenXmlElement> innerContent = new();
        foreach (OpenXmlElement child in sdtElement.Elements())
        {
            if (child is SdtProperties)
            {
                continue; // Skip properties
            }

            innerContent.Add(child.CloneNode(true));
        }

        // Insert inner content before the SdtElement
        foreach (OpenXmlElement content in innerContent)
        {
            parent.InsertBefore(content, sdtElement);
        }

        // Remove the SdtElement shell
        sdtElement.Remove();
    }

    /// <summary>
    /// 处理条件为 false 时的行为。
    /// </summary>
    private static void ProcessFalseCondition(
        SdtElement sdtElement,
        ConditionBlockDefinition definition,
        List<string> errors)
    {
        // falseBehavior determines what happens when condition is false.
        // STRICT and FALSE_ON_ERROR both mean "remove" for false conditions.
        // KEEP_TEMPLATE means unwrap and keep content even when condition is false.
        switch (definition.FalseBehavior)
        {
            case ConditionFailureStrategy.KEEP_TEMPLATE:
                // Keep the content by unwrapping the SdtElement
                UnwrapSdtElement(sdtElement);
                break;

            case ConditionFailureStrategy.STRICT:
            case ConditionFailureStrategy.FALSE_ON_ERROR:
            default:
                // Remove the content block
                sdtElement.Remove();
                break;
        }
    }

    /// <summary>
    /// 处理条件求值失败的情况。
    /// </summary>
    private static void ProcessEvaluationError(
        SdtElement sdtElement,
        ConditionBlockDefinition definition,
        ConditionEvaluationResult result,
        List<string> errors)
    {
        string errorMessage = result.ErrorMessage ?? "条件求值失败。";
        errors.Add($"条件块 \"{definition.BlockKey}\": {errorMessage}");

        switch (definition.OnError)
        {
            case ConditionFailureStrategy.STRICT:
                throw new InvalidOperationException(
                    $"条件块 \"{definition.BlockKey}\" 求值失败（STRICT 策略）：{errorMessage}");

            case ConditionFailureStrategy.FALSE_ON_ERROR:
                sdtElement.Remove();
                break;

            case ConditionFailureStrategy.KEEP_TEMPLATE:
                UnwrapSdtElement(sdtElement);
                break;

            default:
                sdtElement.Remove();
                break;
        }
    }

    /// <summary>
    /// 查找所有匹配指定 Tag 的 SdtElement。
    /// </summary>
    private static List<SdtElement> FindSdtElementsByTag(
        MainDocumentPart mainPart, string tagValue)
    {
        List<SdtElement> results = new();

        // Search in body
        FindSdtElementsInElement(mainPart.Document.Body, tagValue, results);

        // Search in headers
        foreach (HeaderPart headerPart in mainPart.HeaderParts)
        {
            FindSdtElementsInElement(headerPart.Header, tagValue, results);
        }

        // Search in footers
        foreach (FooterPart footerPart in mainPart.FooterParts)
        {
            FindSdtElementsInElement(footerPart.Footer, tagValue, results);
        }

        return results;
    }

    /// <summary>
    /// 在元素树中查找匹配 Tag 的 SdtElement。
    /// </summary>
    private static void FindSdtElementsInElement(
        OpenXmlElement? root, string tagValue, List<SdtElement> results)
    {
        if (root is null)
        {
            return;
        }

        foreach (SdtElement sdtElement in root.Descendants<SdtElement>())
        {
            SdtProperties? properties = sdtElement.Elements<SdtProperties>().FirstOrDefault();
            if (properties is null)
            {
                continue;
            }

            Tag? tag = properties.Elements<Tag>().FirstOrDefault();
            if (tag is not null &&
                string.Equals(tag.Val?.Value, tagValue, StringComparison.Ordinal))
            {
                results.Add(sdtElement);
            }
        }
    }
}

/// <summary>
/// 表示条件块处理结果摘要。
/// </summary>
public sealed record ConditionProcessingResult
{
    /// <summary>
    /// 获取处理的条件块总数。
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// 获取保留的内容块数量。
    /// </summary>
    public int KeptCount { get; init; }

    /// <summary>
    /// 获取删除的内容块数量。
    /// </summary>
    public int RemovedCount { get; init; }

    /// <summary>
    /// 获取处理期间的错误消息。
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
