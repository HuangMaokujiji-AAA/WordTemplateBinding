using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Repeats;

/// <summary>
/// 根据集合数据展开 SdtRow 原型表格行。
/// </summary>
public sealed class OpenXmlRepeatRowExpander
{
    private readonly IDataContextResolver _resolver;
    private readonly OpenXmlRuntimeLocatorBuilder _locatorBuilder;

    /// <summary>
    /// 初始化重复行展开器。
    /// </summary>
    /// <param name="resolver">数据上下文解析器。</param>
    /// <param name="locatorBuilder">运行时定位器构建器。</param>
    public OpenXmlRepeatRowExpander(
        IDataContextResolver resolver,
        OpenXmlRuntimeLocatorBuilder locatorBuilder)
    {
        _resolver = resolver;
        _locatorBuilder = locatorBuilder;
    }

    /// <summary>
    /// 根据定义和渲染作用域展开重复行。
    /// </summary>
    /// <param name="document">Word 文档。</param>
    /// <param name="mainPart">主文档部件。</param>
    /// <param name="definition">重复块定义。</param>
    /// <param name="scope">当前渲染作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>返回展开结果。</returns>
    public Task<RepeatExpansionResult> ExpandAsync(
        WordprocessingDocument document,
        MainDocumentPart mainPart,
        RepeatBlockDefinition definition,
        RenderScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mainPart);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scope);

        cancellationToken.ThrowIfCancellationRequested();

        if (definition.BlockType != RepeatBlockType.REPEAT_ROW)
        {
            throw new ArgumentException(
                $"OpenXmlRepeatRowExpander 只能处理 REPEAT_ROW 类型，收到的是 {definition.BlockType}。",
                nameof(definition));
        }

        string tagValue = $"wtb:repeat:{definition.BlockKey}";

        // 1. 找到匹配的 SdtRow
        SdtRow? repeatRow = FindSdtRowByTag(mainPart, tagValue);
        if (repeatRow is null)
        {
            throw new InvalidOperationException(
                $"找不到 Tag 为 \"{tagValue}\" 的 SdtRow 重复行。");
        }

        // 2. 找到原型 TableRow
        TableRow? prototype = repeatRow.Descendants<TableRow>().FirstOrDefault();
        if (prototype is null)
        {
            throw new InvalidOperationException(
                $"SdtRow \"{tagValue}\" 内部没有找到 TableRow 原型。");
        }

        // 3. 解析数据集合
        IReadOnlyList<object?>? items = _resolver.ResolveArray(scope, definition.SourcePath);
        if (items is null)
        {
            throw new InvalidOperationException(
                $"数据路径 \"{definition.SourcePath}\" 不是数组，无法展开重复行 \"{definition.BlockKey}\"。");
        }

        // 4. 处理空数组
        if (items.Count == 0)
        {
            return Task.FromResult(HandleEmptyArray(repeatRow, definition));
        }

        // 5. 为每个项目克隆行
        List<RuntimeTemplateElement> runtimeElements = new();
        OpenXmlElement? parent = repeatRow.Parent;
        if (parent is null)
        {
            throw new InvalidOperationException("SdtRow 没有父元素，无法插入克隆行。");
        }

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

            // 深克隆原型行
            TableRow clone = (TableRow)prototype.CloneNode(true);

            // 重映射行内 ID 和书签
            RemapCloneIds(clone, instanceKey, i);

            // 构建运行时定位器
            IReadOnlyList<RuntimeTemplateElement> elements =
                _locatorBuilder.BuildForElement(
                    clone,
                    definition.BlockKey,
                    instanceKey,
                    definition.BlockKey,
                    definition.SourcePath);

            runtimeElements.AddRange(elements);

            // 插入克隆行到 SdtRow 之前
            parent.InsertBefore(clone, repeatRow);
        }

        // 6. 移除原型 SdtRow
        repeatRow.Remove();

        Dictionary<string, object> instanceRoots = new(StringComparer.Ordinal);
        // We don't track individual roots for rows since they're inserted at table level

        return Task.FromResult(new RepeatExpansionResult
        {
            InstanceCount = items.Count,
            InstanceRoots = instanceRoots,
            RuntimeElements = runtimeElements,
        });
    }

    /// <summary>
    /// 处理空数组的情况。
    /// </summary>
    private static RepeatExpansionResult HandleEmptyArray(
        SdtRow repeatRow,
        RepeatBlockDefinition definition)
    {
        return definition.EmptyBehavior switch
        {
            EmptyBehavior.REMOVE_PROTOTYPE =>
                HandleRemovePrototype(repeatRow),
            EmptyBehavior.ERROR =>
                throw new InvalidOperationException(
                    $"重复块 \"{definition.BlockKey}\" 的数据集合为空，策略为 ERROR。"),
            EmptyBehavior.KEEP_PROTOTYPE =>
                new RepeatExpansionResult
                {
                    InstanceCount = 0,
                    InstanceRoots = new Dictionary<string, object>(),
                    RuntimeElements = Array.Empty<RuntimeTemplateElement>(),
                },
            EmptyBehavior.INSERT_EMPTY_ROW =>
                HandleRemovePrototype(repeatRow), // For now, just remove; insert empty is future work
            _ => HandleRemovePrototype(repeatRow),
        };
    }

    /// <summary>
    /// 移除原型 SdtRow。
    /// </summary>
    private static RepeatExpansionResult HandleRemovePrototype(SdtRow repeatRow)
    {
        repeatRow.Remove();
        return new RepeatExpansionResult
        {
            InstanceCount = 0,
            InstanceRoots = new Dictionary<string, object>(),
            RuntimeElements = Array.Empty<RuntimeTemplateElement>(),
        };
    }

    /// <summary>
    /// 根据 Tag 值查找 SdtRow。
    /// </summary>
    private static SdtRow? FindSdtRowByTag(MainDocumentPart mainPart, string tagValue)
    {
        // Search in document body
        SdtRow? result = FindSdtRowInElement(mainPart.Document.Body, tagValue);
        if (result is not null)
        {
            return result;
        }

        // Search in headers
        foreach (HeaderPart headerPart in mainPart.HeaderParts)
        {
            result = FindSdtRowInElement(headerPart.Header, tagValue);
            if (result is not null)
            {
                return result;
            }
        }

        // Search in footers
        foreach (FooterPart footerPart in mainPart.FooterParts)
        {
            result = FindSdtRowInElement(footerPart.Footer, tagValue);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 在给定 OpenXmlElement 树中查找匹配 Tag 的 SdtRow。
    /// </summary>
    private static SdtRow? FindSdtRowInElement(OpenXmlElement? root, string tagValue)
    {
        if (root is null)
        {
            return null;
        }

        foreach (SdtRow sdtRow in root.Descendants<SdtRow>())
        {
            SdtProperties? properties = sdtRow.Elements<SdtProperties>().FirstOrDefault();
            if (properties is null)
            {
                continue;
            }

            Tag? tag = properties.Elements<Tag>().FirstOrDefault();
            if (tag is not null &&
                string.Equals(tag.Val?.Value, tagValue, StringComparison.Ordinal))
            {
                return sdtRow;
            }
        }

        return null;
    }

    /// <summary>
    /// 重映射克隆行中的 ID 以避免冲突。
    /// 处理书签 ID、书签名称和 Drawing docPr ID。
    /// </summary>
    private static void RemapCloneIds(TableRow clone, string instanceKey, int index)
    {
        // 重映射 BookmarkStart/BookmarkEnd ID 和名称
        foreach (BookmarkStart bookmarkStart in clone.Descendants<BookmarkStart>())
        {
            if (bookmarkStart.Id?.HasValue == true)
            {
                // Add a large offset based on instance key hash to avoid collisions
                int hashOffset = Math.Abs(GetStableHashCode(instanceKey) % 100000) + (index * 1000);
                if (bookmarkStart.Name?.HasValue == true)
                {
                    string originalName = bookmarkStart.Name.Value!;
                    string newName = SanitizeBookmarkName($"{originalName}_{instanceKey}");
                    bookmarkStart.Name = newName;
                }

                bookmarkStart.Id = $"{(bookmarkStart.Id.Value + hashOffset)}";
            }
        }

        foreach (BookmarkEnd bookmarkEnd in clone.Descendants<BookmarkEnd>())
        {
            if (bookmarkEnd.Id?.HasValue == true)
            {
                int hashOffset = Math.Abs(GetStableHashCode(instanceKey) % 100000) + (index * 1000);
                bookmarkEnd.Id = $"{(bookmarkEnd.Id.Value + hashOffset)}";
            }
        }

        // 重映射 Drawing docPr ID
        foreach (DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline inline
                 in clone.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>())
        {
            DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties? docPr =
                inline.Descendants<DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties>()
                    .FirstOrDefault();
            if (docPr?.Id?.HasValue == true)
            {
                uint newId = (uint)(docPr.Id.Value + Math.Abs(GetStableHashCode(instanceKey) % 1000000) + (uint)(index * 10000));
                docPr.Id = newId;
            }
        }
    }

    /// <summary>
    /// 净化书签名称中的非法字符。
    /// </summary>
    private static string SanitizeBookmarkName(string name)
    {
        // Bookmark names must start with a letter or underscore, contain only
        // letters, digits, underscores, hyphens, or periods, and be <= 40 chars
        System.Text.StringBuilder sb = new(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        string result = sb.ToString();
        if (result.Length > 40)
        {
            result = result[..40];
        }

        if (result.Length > 0 && !char.IsLetter(result[0]) && result[0] != '_')
        {
            result = "_" + result;
        }

        return result;
    }

    /// <summary>
    /// 计算字符串的稳定哈希码。
    /// </summary>
    private static int GetStableHashCode(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value)
            {
                hash = hash * 31 + c;
            }

            return hash;
        }
    }
}
