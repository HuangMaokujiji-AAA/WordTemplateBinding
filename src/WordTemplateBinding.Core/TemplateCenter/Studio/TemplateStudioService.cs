#pragma warning disable CS1591
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

public sealed record TemplateStudioSummary(
    int SegmentCount,
    int ElementCount,
    int ValidElementCount,
    int WarningElementCount,
    int UnsupportedElementCount,
    int ChartCount,
    int TableCount,
    int BoundElementCount,
    int RequiredMissingCount);

public sealed record TemplateStudioWorkspace(
    TemplateVersionView VersionView,
    IReadOnlyList<TemplateSegmentListItem> Segments,
    TemplateSegmentOutline Outline,
    TemplateStudioSummary Summary);

public sealed class TemplateStudioService
{
    private readonly TemplateCatalogService _catalog;
    private readonly TemplateSegmentService _segments;

    public TemplateStudioService(
        TemplateCatalogService catalog,
        TemplateSegmentService segments)
    {
        _catalog = catalog;
        _segments = segments;
    }

    public async Task<TemplateStudioWorkspace> GetAsync(
        ulong templateId,
        ulong? templateVersionId,
        ulong? bindingSetId,
        CancellationToken cancellationToken)
    {
        TemplateVersionView version = templateVersionId.HasValue
            ? await _catalog.GetVersionAsync(
                templateVersionId.Value,
                cancellationToken)
            : await _catalog.GetCurrentVersionAsync(
                templateId,
                cancellationToken);
        if (version.Template.Id != templateId)
        {
            throw new TemplatePersistenceException(
                "template_version_mismatch",
                "指定模板版本不属于当前模板。");
        }

        IReadOnlyList<TemplateSegmentListItem> segments =
            await _segments.ListAsync(
                version.Version.Id,
                bindingSetId,
                cancellationToken);
        // Segment listing may repair stale segment affiliations after a rescan.
        // Return the refreshed element records in the same aggregate response.
        version = await _catalog.GetVersionAsync(
            version.Version.Id,
            cancellationToken);
        TemplateSegmentOutline outline = await _segments.GetOutlineAsync(
            version.Version.Id,
            cancellationToken);
        IReadOnlyList<TemplateElementRecord> elements = version.Elements;
        TemplateStudioSummary summary = new(
            segments.Count,
            elements.Count,
            elements.Count(element => string.Equals(
                element.ParseStatus,
                "VALID",
                StringComparison.OrdinalIgnoreCase)),
            elements.Count(element => string.Equals(
                element.ParseStatus,
                "WARNING",
                StringComparison.OrdinalIgnoreCase)),
            elements.Count(element =>
                !string.Equals(
                    element.ParseStatus,
                    "VALID",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    element.ParseStatus,
                    "WARNING",
                    StringComparison.OrdinalIgnoreCase)),
            elements.Count(element => string.Equals(
                element.ElementType,
                "CHART",
                StringComparison.OrdinalIgnoreCase)),
            elements.Count(element => string.Equals(
                element.ElementType,
                "TABLE",
                StringComparison.OrdinalIgnoreCase)),
            segments.Sum(segment => segment.BoundCount),
            segments.Sum(segment => segment.RequiredMissingCount));
        return new TemplateStudioWorkspace(
            version,
            segments,
            outline,
            summary);
    }
}

#pragma warning restore CS1591
