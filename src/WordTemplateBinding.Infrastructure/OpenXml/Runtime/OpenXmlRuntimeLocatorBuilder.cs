using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 为克隆元素构建运行时定位器。
/// </summary>
public sealed class OpenXmlRuntimeLocatorBuilder
{
    private readonly HashSet<string> _usedLocatorIds = new(StringComparer.Ordinal);
    private int _elementCounter;

    /// <summary>
    /// 为指定的 OpenXml 元素及其子元素构建运行时定位器。
    /// </summary>
    /// <param name="root">克隆的根元素。</param>
    /// <param name="componentNodeKey">组件节点键。</param>
    /// <param name="instanceKey">实例键。</param>
    /// <param name="blockKey">块键。</param>
    /// <param name="dataScopePath">数据作用域路径。</param>
    /// <returns>返回运行时模板元素列表。</returns>
    public IReadOnlyList<RuntimeTemplateElement> BuildForElement(
        OpenXmlElement root,
        string componentNodeKey,
        string instanceKey,
        string blockKey,
        string dataScopePath)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<RuntimeTemplateElement> elements = new();

        // 为每个内容控件构建定位器
        foreach (SdtElement sdt in root.Descendants<SdtElement>())
        {
            SdtProperties? properties = sdt.Elements<SdtProperties>().FirstOrDefault();
            Tag? tag = properties?.Elements<Tag>().FirstOrDefault();
            string? tagValue = tag?.Val?.Value;

            string elementKey = tagValue ?? $"element_{Interlocked.Increment(ref _elementCounter)}";
            string runtimeLocatorId = BuildLocatorId(componentNodeKey, instanceKey, elementKey);

            if (!_usedLocatorIds.Add(runtimeLocatorId))
            {
                throw new InvalidOperationException(
                    $"运行时定位器冲突：{runtimeLocatorId}");
            }

            elements.Add(new RuntimeTemplateElement
            {
                RuntimeLocatorId = runtimeLocatorId,
                SourceElementKey = elementKey,
                ComponentNodeKey = componentNodeKey,
                InstanceKey = instanceKey,
                DataScopePath = dataScopePath,
                Locator = new OpenXmlRuntimeLocator
                {
                    RuntimeLocatorId = runtimeLocatorId,
                    ComponentNodeKey = componentNodeKey,
                    InstanceKey = instanceKey,
                    ElementKey = elementKey,
                    BlockKey = blockKey,
                    DataScopePath = dataScopePath,
                },
            });
        }

        // 为每个段落中的文本运行构建定位器（用于显式占位符）
        foreach (Paragraph paragraph in root.Descendants<Paragraph>())
        {
            foreach (Run run in paragraph.Elements<Run>())
            {
                Text? text = run.Elements<Text>().FirstOrDefault();
                string? textValue = text?.Text;
                if (textValue is not null &&
                    textValue.Contains("{{", StringComparison.Ordinal) &&
                    textValue.Contains("}}", StringComparison.Ordinal))
                {
                    string elementKey = $"text_{Interlocked.Increment(ref _elementCounter)}";
                    string runtimeLocatorId = BuildLocatorId(componentNodeKey, instanceKey, elementKey);

                    if (!_usedLocatorIds.Add(runtimeLocatorId))
                    {
                        throw new InvalidOperationException(
                            $"运行时定位器冲突：{runtimeLocatorId}");
                    }

                    elements.Add(new RuntimeTemplateElement
                    {
                        RuntimeLocatorId = runtimeLocatorId,
                        SourceElementKey = elementKey,
                        ComponentNodeKey = componentNodeKey,
                        InstanceKey = instanceKey,
                        DataScopePath = dataScopePath,
                        Locator = new OpenXmlRuntimeLocator
                        {
                            RuntimeLocatorId = runtimeLocatorId,
                            ComponentNodeKey = componentNodeKey,
                            InstanceKey = instanceKey,
                            ElementKey = elementKey,
                            BlockKey = blockKey,
                            DataScopePath = dataScopePath,
                        },
                    });
                }
            }
        }

        return elements.AsReadOnly();
    }

    /// <summary>
    /// 重置内部状态，为新的渲染任务准备。
    /// </summary>
    public void Reset()
    {
        _usedLocatorIds.Clear();
        _elementCounter = 0;
    }

    /// <summary>
    /// 构建格式化的运行时定位器 ID。
    /// </summary>
    private static string BuildLocatorId(
        string componentNodeKey,
        string instanceKey,
        string elementKey) =>
        $"{componentNodeKey}/{instanceKey}/{elementKey}";
}
