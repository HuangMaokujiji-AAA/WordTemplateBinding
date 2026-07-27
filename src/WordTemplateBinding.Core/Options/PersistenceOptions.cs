#pragma warning disable CS1591
using System.ComponentModel.DataAnnotations;

namespace WordTemplateBinding.Core.Options;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    [RegularExpression("^(MySql|InMemory)$", ErrorMessage = "Persistence:Mode 必须为 MySql 或 InMemory。")]
    public string Mode { get; set; } = "MySql";
}

public sealed class DatabaseFileStorageOptions
{
    public const string SectionName = "DatabaseFileStorage";

    [Range(64 * 1024, 16 * 1024 * 1024)]
    public int ChunkSizeBytes { get; set; } = 4 * 1024 * 1024;

    [Required]
    [MaxLength(128)]
    public string BucketName { get; set; } = "word-template-binding";

    [Range(5, 1440)]
    public int UploadSessionMinutes { get; set; } = 60;
}

public sealed class DataSourceOptions
{
    public const string SectionName = "DataSources";

    [Range(1, 20)]
    public int SampleRowLimit { get; set; } = 1;

    [Range(32, 4096)]
    public int SampleValueMaxLength { get; set; } = 512;

    [Range(1024, 10 * 1024 * 1024)]
    public int SampleMaxBytes { get; set; } = 1024 * 1024;

    [Range(1, 120)]
    public int CommandTimeoutSeconds { get; set; } = 15;
}

public sealed class ApplicationIdentityOptions
{
    public const string SectionName = "ApplicationIdentity";
    public string? DefaultActorUserId { get; set; }
}

public sealed class BindingSuggestionOptions
{
    public const string SectionName = "BindingSuggestions";
    public Dictionary<string, string[]> Aliases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

#pragma warning restore CS1591
