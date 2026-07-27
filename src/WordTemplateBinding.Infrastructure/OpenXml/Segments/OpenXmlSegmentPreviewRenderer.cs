using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Segments;

#pragma warning disable CS1591

public sealed class OpenXmlSegmentPreviewRenderer : IWordSegmentPreviewRenderer
{
    public async Task<Stream> RenderAsync(
        string sourceDocxPath,
        TemplateSegmentRecord segment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"wtb-segment-{Guid.NewGuid():N}.docx");
        try
        {
            File.Copy(sourceDocxPath, tempPath, overwrite: false);
            if (!string.Equals(segment.AnchorType, "VIRTUAL", StringComparison.OrdinalIgnoreCase))
            {
                using WordprocessingDocument document =
                    WordprocessingDocument.Open(tempPath, true);
                Body body = document.MainDocumentPart?.Document.Body
                    ?? throw new InvalidDataException("DOCX 缺少主文档正文。");
                using JsonDocument anchor = JsonDocument.Parse(segment.StartAnchorJson);
                string tag = anchor.RootElement.GetProperty("tag").GetString()
                    ?? throw new InvalidDataException("片段锚点缺少 Tag。");
                SdtBlock target = body.Descendants<SdtBlock>().SingleOrDefault(block =>
                        string.Equals(
                            block.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value,
                            tag,
                            StringComparison.Ordinal))
                    ?? throw new InvalidDataException($"找不到片段锚点：{tag}。");
                OpenXmlElement preserved = target.CloneNode(true);
                SectionProperties? sectionProperties = body
                    .Elements<SectionProperties>()
                    .LastOrDefault()?
                    .CloneNode(true) as SectionProperties;
                body.RemoveAllChildren();
                body.Append(preserved);
                if (sectionProperties is not null &&
                    !preserved.Descendants<SectionProperties>().Any())
                {
                    body.Append(sectionProperties);
                }

                document.MainDocumentPart!.Document.Save();
            }

            byte[] bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            return new MemoryStream(bytes, writable: false);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
        }
    }
}

#pragma warning restore CS1591
