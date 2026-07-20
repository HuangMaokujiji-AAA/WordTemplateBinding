using DocumentFormat.OpenXml.Wordprocessing;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 表示一个 Word 段落拼接后的完整文本及其文本节点偏移映射。
/// </summary>
public sealed class ParagraphTextMap
{
    /// <summary>
    /// 初始化段落文本映射。
    /// </summary>
    /// <param name="fullText">段落内全部 Text 节点拼接后的文本。</param>
    /// <param name="segments">Text 节点与全局偏移的映射。</param>
    public ParagraphTextMap(string fullText, IReadOnlyList<TextSegment> segments)
    {
        FullText = fullText;
        Segments = segments;
    }

    /// <summary>
    /// 获取段落拼接文本。
    /// </summary>
    public string FullText { get; }

    /// <summary>
    /// 获取按文档顺序排列的文本节点段。
    /// </summary>
    public IReadOnlyList<TextSegment> Segments { get; }
}

/// <summary>
/// 表示单个 OpenXML Text 节点在段落拼接文本中的范围。
/// </summary>
public sealed class TextSegment
{
    /// <summary>
    /// 初始化文本节点段。
    /// </summary>
    /// <param name="nodeIndex">节点在段落中的顺序索引。</param>
    /// <param name="startOffset">节点文本在拼接文本中的起始偏移。</param>
    /// <param name="length">节点文本长度。</param>
    /// <param name="textNode">对应的 OpenXML Text 节点。</param>
    public TextSegment(int nodeIndex, int startOffset, int length, Text textNode)
    {
        NodeIndex = nodeIndex;
        StartOffset = startOffset;
        Length = length;
        TextNode = textNode;
    }

    /// <summary>
    /// 获取节点在段落中的顺序索引。
    /// </summary>
    public int NodeIndex { get; }

    /// <summary>
    /// 获取节点文本在段落拼接文本中的起始偏移。
    /// </summary>
    public int StartOffset { get; }

    /// <summary>
    /// 获取节点文本长度。
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// 获取对应的 OpenXML Text 节点。
    /// </summary>
    public Text TextNode { get; }

    /// <summary>
    /// 获取节点文本在段落拼接文本中的结束偏移。
    /// </summary>
    public int EndOffset => StartOffset + Length;
}

/// <summary>
/// 负责从 Word 段落建立可复用的文本节点偏移映射。
/// </summary>
public static class ParagraphTextMapBuilder
{
    /// <summary>
    /// 拼接段落中的 Text 节点，并记录每个节点的全局偏移。
    /// </summary>
    /// <param name="paragraph">需要映射的 Word 段落。</param>
    /// <returns>返回段落文本映射。</returns>
    public static ParagraphTextMap Build(Paragraph paragraph)
    {
        List<TextSegment> segments = new();
        System.Text.StringBuilder builder = new();
        int nodeIndex = 0;

        // Word 会把一个视觉上连续的数字拆到多个 Run/Text 中，因此扫描和替换都必须基于拼接文本。
        foreach (Text textNode in paragraph.Descendants<Text>())
        {
            string value = textNode.Text ?? string.Empty;
            segments.Add(new TextSegment(nodeIndex, builder.Length, value.Length, textNode));
            builder.Append(value);
            nodeIndex++;
        }

        return new ParagraphTextMap(builder.ToString(), segments.AsReadOnly());
    }
}
