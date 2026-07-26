using System.Security.Cryptography;
using System.Text;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml;

/// <summary>
/// 按内容控件 Tag、显式占位符上下文和 Locator 的顺序生成模板元素稳定键。
/// </summary>
public sealed class TemplateElementIdentityResolver : ITemplateElementIdentityResolver
{
    /// <inheritdoc />
    public TemplateElementIdentity Resolve(
        string locatorId,
        TextLocator locator,
        string? placeholderCandidatePath,
        string? contentControlTag)
    {
        if (contentControlTag?.StartsWith(
                "rtb-marker:",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return new TemplateElementIdentity(
                $"marker:{contentControlTag["rtb-marker:".Length..]}",
                "ContentControlTag",
                contentControlTag);
        }

        if (!string.IsNullOrWhiteSpace(placeholderCandidatePath))
        {
            string identityInput = string.Join(
                "\u001f",
                placeholderCandidatePath,
                locator.PartKind,
                locator.PartKey,
                locator.ContextHash,
                locator.OccurrenceIndex);
            string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(identityInput)))
                .ToLowerInvariant();
            return new TemplateElementIdentity(
                $"placeholder:{hash}",
                "PlaceholderContext",
                null);
        }

        return new TemplateElementIdentity($"text:{locatorId}", "LocatorId", null);
    }
}
