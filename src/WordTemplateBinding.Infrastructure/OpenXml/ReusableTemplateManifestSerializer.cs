using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 读取和写入本系统固定命名空间的 DOCX 自定义 XML 绑定清单。
/// </summary>
internal static class ReusableTemplateManifestSerializer
{
    internal const string NamespaceUri = "urn:word-template-binding:bindings:v1";
    private const int CurrentVersion = 1;
    private static readonly XNamespace ManifestNamespace = NamespaceUri;

    /// <summary>
    /// 读取全部本系统清单；缺失或单个损坏的清单不会阻断普通 DOCX 扫描。
    /// </summary>
    /// <param name="mainPart">DOCX 主文档部件。</param>
    /// <returns>返回可恢复图表项与警告。</returns>
    internal static ReusableTemplateManifest Read(MainDocumentPart mainPart)
    {
        List<ReusableTemplateChartBinding> chartBindings = new();
        List<string> warnings = new();
        foreach (CustomXmlPart customPart in mainPart.CustomXmlParts)
        {
            string xml;
            using (Stream stream = customPart.GetStream(FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new(stream, Encoding.UTF8, true, leaveOpen: false))
            {
                xml = reader.ReadToEnd();
            }

            try
            {
                XDocument document = ParseXml(xml);
                XElement? root = document.Root;
                if (root?.Name != ManifestNamespace + "bindings")
                {
                    continue;
                }

                if (!int.TryParse(root.Attribute("version")?.Value, out int version) ||
                    version != CurrentVersion)
                {
                    warnings.Add("发现不受支持的复用模板绑定清单版本，已忽略该清单。");
                    continue;
                }

                foreach (XElement chart in root.Elements(ManifestNamespace + "chart"))
                {
                    string? dataPath = chart.Attribute("dataPath")?.Value;
                    string? partKey = chart.Attribute("chartPart")?.Value;
                    string? relationshipId = chart.Attribute("relationshipId")?.Value;
                    string? targetKind = chart.Attribute("targetKind")?.Value;
                    if (string.IsNullOrEmpty(dataPath) ||
                        string.IsNullOrEmpty(partKey) ||
                        string.IsNullOrEmpty(relationshipId) ||
                        !string.Equals(targetKind, "Chart", StringComparison.Ordinal) ||
                        !int.TryParse(chart.Attribute("documentOrder")?.Value, out int documentOrder) ||
                        documentOrder < 0)
                    {
                        warnings.Add("复用模板绑定清单中存在不完整的图表条目，已忽略该条目。");
                        continue;
                    }

                    chartBindings.Add(new ReusableTemplateChartBinding
                    {
                        DataPath = dataPath,
                        PartKey = partKey,
                        RelationshipId = relationshipId,
                        DocumentOrder = documentOrder,
                    });
                }
            }
            catch (XmlException)
            {
                if (xml.Contains(NamespaceUri, StringComparison.Ordinal))
                {
                    warnings.Add("复用模板绑定清单 XML 已损坏，文本占位符仍会正常恢复。");
                }
            }
        }

        return new ReusableTemplateManifest
        {
            Version = CurrentVersion,
            ChartBindings = chartBindings.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
        };
    }

    /// <summary>
    /// 仅替换属于本系统命名空间的有效清单，并保留其他 CustomXmlPart。
    /// </summary>
    /// <param name="mainPart">DOCX 主文档部件。</param>
    /// <param name="bindings">需要写入的绑定关系。</param>
    /// <param name="mockItems">按 LocatorId 索引的文本扫描项。</param>
    /// <param name="chartItems">按 LocatorId 索引的图表扫描项。</param>
    internal static void Write(
        MainDocumentPart mainPart,
        IReadOnlyCollection<TemplateBinding> bindings,
        IReadOnlyDictionary<string, MockDataItem> mockItems,
        IReadOnlyDictionary<string, ChartTemplateItem> chartItems)
    {
        foreach (CustomXmlPart existing in mainPart.CustomXmlParts.ToList())
        {
            if (IsOwnedManifest(existing))
            {
                mainPart.DeletePart(existing);
            }
        }

        XElement root = new(
            ManifestNamespace + "bindings",
            new XAttribute(XNamespace.Xmlns + "wtb", NamespaceUri),
            new XAttribute("version", CurrentVersion));
        foreach (TemplateBinding binding in bindings
                     .OrderBy(item => item.TargetKind)
                     .ThenBy(item => item.LocatorId, StringComparer.Ordinal))
        {
            if (binding.TargetKind == BindingTargetKind.Text &&
                mockItems.ContainsKey(binding.LocatorId))
            {
                root.Add(new XElement(
                    ManifestNamespace + "text",
                    new XAttribute("dataPath", binding.DataPath),
                    new XAttribute("targetKind", "Text")));
            }
            else if (binding.TargetKind == BindingTargetKind.Chart &&
                     chartItems.TryGetValue(binding.LocatorId, out ChartTemplateItem? chart))
            {
                root.Add(new XElement(
                    ManifestNamespace + "chart",
                    new XAttribute("dataPath", binding.DataPath),
                    new XAttribute("targetKind", "Chart"),
                    new XAttribute("chartPart", chart.Locator.PartKey),
                    new XAttribute("relationshipId", chart.Locator.RelationshipId),
                    new XAttribute("documentOrder", chart.Locator.DocumentOrder)));
            }
        }

        CustomXmlPart manifestPart = mainPart.AddCustomXmlPart("application/xml");
        using Stream output = manifestPart.GetStream(FileMode.Create, FileAccess.Write);
        using XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            CloseOutput = false,
        });
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
    }

    private static bool IsOwnedManifest(CustomXmlPart part)
    {
        string xml;
        using (Stream stream = part.GetStream(FileMode.Open, FileAccess.Read))
        using (StreamReader reader = new(stream, Encoding.UTF8, true))
        {
            xml = reader.ReadToEnd();
        }

        try
        {
            XDocument document = ParseXml(xml);
            return document.Root?.Name == ManifestNamespace + "bindings";
        }
        catch (XmlException)
        {
            return xml.Contains(NamespaceUri, StringComparison.Ordinal);
        }
    }

    private static XDocument ParseXml(string xml)
    {
        using StringReader textReader = new(xml);
        using XmlReader reader = XmlReader.Create(textReader, CreateReaderSettings());
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };
}
