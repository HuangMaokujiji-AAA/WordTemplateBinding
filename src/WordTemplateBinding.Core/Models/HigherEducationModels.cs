#pragma warning disable CS1591
namespace WordTemplateBinding.Core.Models;

public sealed record HigherEducationSchool(
    string CollectionYear,
    string SchoolCode,
    string SchoolName);

public sealed record HigherEducationReportData
{
    public required string CollectionYear { get; init; }

    public required string SchoolCode { get; init; }

    public required string SchoolName { get; init; }

    public required IReadOnlyDictionary<string, object?> Content { get; init; }

    public required ulong RowCount { get; init; }
}

public sealed record HigherEducationDataSourceResult(
    DataSourceRecord Source,
    DataSnapshotRecord Snapshot);
#pragma warning restore CS1591
