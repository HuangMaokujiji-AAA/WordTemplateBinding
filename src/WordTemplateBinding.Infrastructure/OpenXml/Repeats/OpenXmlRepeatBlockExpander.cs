using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml.Cloning;

namespace WordTemplateBinding.Infrastructure.OpenXml.Repeats;

/// <summary>
/// 根据集合数据展开 SdtBlock 原型内容块。
/// </summary>
public sealed class OpenXmlRepeatBlockExpander
{
    private readonly IDataContextResolver _resolver;
    private readonly OpenXmlRuntimeLocatorBuilder _locatorBuilder;

    /// <summary>
    /// 初始化重复块展开器。
    /// </summary>
    /// <param name="resolver">数据上下文解析器。</param>
    /// <param name="locatorBuilder">运行时定位器构建器。</param>
    public OpenXmlRepeatBlockExpander(
        IDataContextResolver resolver,
        OpenXmlRuntimeLocatorBuilder locatorBuilder)
    {
        _resolver = resolver;
        _locatorBuilder = locatorBuilder;
    }

    /// <summary>
    /// 根据定义和渲染作用域展开重复块。
    /// </summary>
    /// <param name="document">Word 文档。</param>
    /// <param name="mainPart">主文档部件。</param>
    /// <param name="definition">重复块定义。</param>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="drawingIdAllocator">Drawing ID 分配器。</param>
    /// <param name="chartCloner">图表克隆器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回展开结果。</returns>
    public async Task<RepeatExpansionResult> ExpandAsync(
        WordprocessingDocument document,
        MainDocumentPart mainPart,
        RepeatBlockDefinition definition,
        RenderScope scope,
        OpenXmlDrawingIdAllocator drawingIdAllocator,
        OpenXmlChartPartCloner chartCloner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mainPart);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(drawingIdAllocator);
        ArgumentNullException.ThrowIfNull(chartCloner);

        cancellationToken.ThrowIfCancellationRequested();

        if (definition.BlockType != RepeatBlockType.REPEAT_BLOCK)
        {
            throw new ArgumentException(
                $"OpenXmlRepeatBlockExpander 只能处理 REPEAT_BLOCK 类型，收到的是 {definition.BlockType}。",
                nameof(definition));
        }

        string tagValue = $"wtb:repeat:{definition.BlockKey}";

        // 1. 找到匹配的 SdtBlock
        SdtBlock? repeatBlock = FindSdtBlockByTag(mainPart, tagValue);
        if (repeatBlock is null)
        {
            throw new InvalidOperationException(
                $"找不到 Tag 为 \"{tagValue}\" 的 SdtBlock 重复块。");
        }

        // 2. 获取原型子元素
        SdtContentBlock? contentBlock = repeatBlock.Elements<SdtContentBlock>().FirstOrDefault();
        if (contentBlock is null)
        {
            throw new InvalidOperationException(
                $"SdtBlock \"{tagValue}\" 内部没有 SdtContentBlock。");
        }

        List<OpenXmlElement> prototypeElements = contentBlock.Elements().ToList();
        if (prototypeElements.Count == 0)
        {
            // No content to repeat
            repeatBlock.Remove();
            return new RepeatExpansionResult
            {
                InstanceCount = 0,
                InstanceRoots = new Dictionary<string, object>(),
                RuntimeElements = Array.Empty<RuntimeTemplateElement>(),
            };
        }

        // 3. 解析数据集合
        IReadOnlyList<object?>? items = _resolver.ResolveArray(scope, definition.SourcePath);
        if (items is null)
        {
            throw new InvalidOperationException(
                $"数据路径 \"{definition.SourcePath}\" 不是数组，无法展开重复块 \"{definition.BlockKey}\"。");
        }

        // 4. 处理空数组
        if (items.Count == 0)
        {
            return HandleEmptyArray(repeatBlock, definition);
        }

        // 5. 获取父元素以插入克隆内容
        OpenXmlElement? parent = repeatBlock.Parent;
        if (parent is null)
        {
            throw new InvalidOperationException("SdtBlock 没有父元素。");
        }

        List<RuntimeTemplateElement> runtimeElements = new();
        Dictionary<string, object> instanceRoots = new(StringComparer.Ordinal);

        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            object? item = items[i];

            // 解析稳定实例键
            string? itemKey = _resolver.ResolveItemKey(item, definition.ItemKeyPath);
            if (string.IsNullOrEmpty(itemKey))
            {
                throw new InvalidOperationException(
                    $"重复块 \"{definition.BlockKey}\" 第 {i} 项的 {definition.ItemKeyPath} 无法解析为稳定键。");
            }

            string instanceKey = $"{definition.BlockKey}/{itemKey}";

            // 创建子作用域
            RenderScope itemScope = scope.CreateChild(
                definition.ItemAlias,
                item,
                i,
                instanceKey);

            // 6. 处理分页
            if (ShouldInsertPageBreak(definition.PageBreak, i))
            {
                Paragraph pageBreakPara = CreatePageBreakParagraph();
                parent.InsertBefore(pageBreakPara, repeatBlock);
            }

            // 7. 深克隆每个原型元素
            List<OpenXmlElement> clonedElements = new(prototypeElements.Count);
            foreach (OpenXmlElement prototype in prototypeElements)
            {
                OpenXmlElement clone = prototype.CloneNode(true);

                // 重映射 Drawing docPr ID
                drawingIdAllocator.RemapDrawingIds(clone);

                // 克隆图表部件
                ChartCloneResult chartResult = await chartCloner.CloneForElementAsync(
                    mainPart, mainPart, clone, instanceKey, cancellationToken);

                clonedElements.Add(chartResult.Root);
            }

            // 8. 构建运行时定位器
            foreach (OpenXmlElement clone in clonedElements)
            {
                IReadOnlyList<RuntimeTemplateElement> elements =
                    _locatorBuilder.BuildForElement(
                        clone,
                        definition.BlockKey,
                        instanceKey,
                        definition.BlockKey,
                        definition.SourcePath);
                runtimeElements.AddRange(elements);
            }

            // 9. 插入克隆内容到原型之前
            foreach (OpenXmlElement clone in clonedElements)
            {
                parent.InsertBefore(clone, repeatBlock);
            }

            instanceRoots[instanceKey] = clonedElements;
        }

        // 10. 移除原型 SdtBlock
        repeatBlock.Remove();

        return new RepeatExpansionResult
        {
            InstanceCount = items.Count,
            InstanceRoots = instanceRoots,
            RuntimeElements = runtimeElements,
        };
    }

    /// <summary>
    /// 处理空数组的情况。
    /// </summary>
    private static RepeatExpansionResult HandleEmptyArray(
        SdtBlock repeatBlock,
        RepeatBlockDefinition definition)
    {
        return definition.EmptyBehavior switch
        {
            EmptyBehavior.REMOVE_PROTOTYPE =>
                HandleRemovePrototype(repeatBlock),
            EmptyBehavior.ERROR =>
                throw new InvalidOperationException(
                    $"重复块 \"{definition.BlockKey}\" 的数据集合为空，策略为 ERROR。"),
            _ => HandleRemovePrototype(repeatBlock),
        };
    }

    /// <summary>
    /// 移除原型 SdtBlock。
    /// </summary>
    private static RepeatExpansionResult HandleRemovePrototype(SdtBlock repeatBlock)
    {
        repeatBlock.Remove();
        return new RepeatExpansionResult
        {
            InstanceCount = 0,
            InstanceRoots = new Dictionary<string, object>(),
            RuntimeElements = Array.Empty<RuntimeTemplateElement>(),
        };
    }

    /// <summary>
    /// 根据 Tag 值查找 SdtBlock。
    /// </summary>
    private static SdtBlock? FindSdtBlockByTag(MainDocumentPart mainPart, string tagValue)
    {
        SdtBlock? result = FindSdtBlockInElement(mainPart.Document.Body, tagValue);
        if (result is not null)
        {
            return result;
        }

        foreach (HeaderPart headerPart in mainPart.HeaderParts)
        {
            result = FindSdtBlockInElement(headerPart.Header, tagValue);
            if (result is not null)
            {
                return result;
            }
        }

        foreach (FooterPart footerPart in mainPart.FooterParts)
        {
            result = FindSdtBlockInElement(footerPart.Footer, tagValue);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 在元素树中查找匹配 Tag 的 SdtBlock。
    /// </summary>
    private static SdtBlock? FindSdtBlockInElement(OpenXmlElement? root, string tagValue)
    {
        if (root is null)
        {
            return null;
        }

        foreach (SdtBlock sdtBlock in root.Descendants<SdtBlock>())
        {
            SdtProperties? properties = sdtBlock.Elements<SdtProperties>().FirstOrDefault();
            if (properties is null)
            {
                continue;
            }

            Tag? tag = properties.Elements<Tag>().FirstOrDefault();
            if (tag is not null &&
                string.Equals(tag.Val?.Value, tagValue, StringComparison.Ordinal))
            {
                return sdtBlock;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断是否需要在当前迭代前插入分页符。
    /// </summary>
    private static bool ShouldInsertPageBreak(PageBreakStrategy strategy, int index)
    {
        return strategy switch
        {
            PageBreakStrategy.BEFORE_EACH => true,
            PageBreakStrategy.BEFORE_EACH_EXCEPT_FIRST => index > 0,
            PageBreakStrategy.AFTER_EACH => false, // Handled differently
            PageBreakStrategy.AFTER_EACH_EXCEPT_LAST => false, // Handled differently
            _ => false,
        };
    }

    /// <summary>
    /// 创建分页符段落。
    /// </summary>
    private static Paragraph CreatePageBreakParagraph()
    {
        return new Paragraph(
            new Run(
                new Break { Type = BreakValues.Page }));
    }
}
