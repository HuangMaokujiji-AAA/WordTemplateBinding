using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// 读取 ChartPart 的外部关系信息（嵌入工作簿路径、关系部件路径等）。
/// </summary>
internal static class OpenXmlChartRelationshipReader
{
    internal static ChartRelationshipInfo Read(ChartPart chartPart)
    {
        string? embeddedWorkbookPath = null;
        string? externalDataRelationshipId = null;

        try
        {
            // Check parts the chart references (Parts may not exist in all SDK versions)
            var parts = chartPart.Parts;
            if (parts is not null)
            {
                foreach (IdPartPair partPair in parts)
                {
                    if (partPair.OpenXmlPart is EmbeddedPackagePart embeddedPart)
                    {
                        embeddedWorkbookPath = embeddedPart.Uri.OriginalString;
                    }
                }
            }

            // Try to get external data relationship from the chart XML
            // OpenXml SDK ChartPart doesn't expose ExternalDataParts directly,
            // so we look for ExternalData references in the chart space
            if (chartPart.ChartSpace is not null)
            {
                foreach (var element in chartPart.ChartSpace.Descendants())
                {
                    if (element.LocalName == "externalData")
                    {
                        OpenXmlAttribute attr = element.GetAttribute("r:id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                        if (!string.IsNullOrEmpty(attr.Value))
                        {
                            externalDataRelationshipId = attr.Value;
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Best-effort; relationship reading is optional
        }

        string chartRelationshipPartPath = GetRelationshipPartPath(chartPart);

        return new ChartRelationshipInfo(
            chartRelationshipPartPath,
            externalDataRelationshipId,
            embeddedWorkbookPath,
            embeddedWorkbookPath is not null);
    }

    private static string GetRelationshipPartPath(ChartPart chartPart)
    {
        string original = chartPart.Uri.OriginalString;
        string dir = original.Contains('/')
            ? original[..original.LastIndexOf('/')]
            : string.Empty;
        string name = original.Contains('/')
            ? original[(original.LastIndexOf('/') + 1)..]
            : original;
        return $"{dir}/_rels/{name}.rels";
    }
}

internal sealed record ChartRelationshipInfo(
    string ChartRelationshipPartPath,
    string? ExternalDataRelationshipId,
    string? EmbeddedWorkbookPath,
    bool EmbeddedWorkbookDetected);
