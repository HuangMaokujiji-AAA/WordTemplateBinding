using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace WordTemplateBinding.Infrastructure.OpenXml.Cloning;

/// <summary>
/// 文档级别的 Drawing docPr ID 分配器，确保所有绘图元素拥有唯一 ID。
/// </summary>
public sealed class OpenXmlDrawingIdAllocator
{
    private uint _nextId;
    private readonly HashSet<uint> _usedIds;

    /// <summary>
    /// 初始化 ID 分配器并扫描文档中已有的 ID。
    /// </summary>
    /// <param name="document">Word 文档。</param>
    public OpenXmlDrawingIdAllocator(WordprocessingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _usedIds = new HashSet<uint>();
        _nextId = 1;

        MainDocumentPart? mainPart = document.MainDocumentPart;
        if (mainPart is not null)
        {
            ScanElement(mainPart.Document.Body);
        }
    }

    /// <summary>
    /// 分配一个新的唯一 docPr ID。
    /// </summary>
    /// <returns>返回唯一的 docPr ID。</returns>
    public uint Allocate()
    {
        while (_usedIds.Contains(_nextId))
        {
            _nextId++;
        }

        uint id = _nextId;
        _usedIds.Add(id);
        _nextId++;
        return id;
    }

    /// <summary>
    /// 为克隆元素重映射 docPr ID。
    /// </summary>
    /// <param name="root">克隆的根元素。</param>
    public void RemapDrawingIds(OpenXmlElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline inline
                 in root.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>())
        {
            DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties? docPr =
                inline.Descendants<DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties>()
                    .FirstOrDefault();
            if (docPr is not null)
            {
                docPr.Id = Allocate();
            }
        }

        foreach (DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor anchor
                 in root.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>())
        {
            DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties? docPr =
                anchor.Descendants<DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties>()
                    .FirstOrDefault();
            if (docPr is not null)
            {
                docPr.Id = Allocate();
            }
        }
    }

    /// <summary>
    /// 扫描元素树中已有的 docPr ID。
    /// </summary>
    private void ScanElement(OpenXmlElement? root)
    {
        if (root is null)
        {
            return;
        }

        foreach (DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline inline
                 in root.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>())
        {
            DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties? docPr =
                inline.Descendants<DocumentFormat.OpenXml.Drawing.NonVisualDrawingProperties>()
                    .FirstOrDefault();
            if (docPr?.Id?.HasValue == true)
            {
                _usedIds.Add(docPr.Id.Value);
                if (docPr.Id.Value >= _nextId)
                {
                    _nextId = docPr.Id.Value + 1;
                }
            }
        }
    }
}
