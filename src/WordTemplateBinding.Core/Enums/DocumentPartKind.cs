namespace WordTemplateBinding.Core.Enums;

/// <summary>
/// 表示可扫描和定位的 Word 文档部件类型。
/// </summary>
public enum DocumentPartKind
{
    /// <summary>
    /// 主文档正文。
    /// </summary>
    MainDocument = 1,

    /// <summary>
    /// 页眉。
    /// </summary>
    Header = 2,

    /// <summary>
    /// 页脚。
    /// </summary>
    Footer = 3,

    /// <summary>
    /// 脚注。
    /// </summary>
    Footnote = 4,

    /// <summary>
    /// 尾注。
    /// </summary>
    Endnote = 5,

    /// <summary>
    /// 文本框。
    /// </summary>
    TextBox = 6,
}
