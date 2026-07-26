#pragma warning disable CS1591
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.DataSchema;

/// <summary>
/// 将内置 JSON Fixture 初始化为项目级测试数据源。
/// 数据源、快照和字段均持久化到 MySQL，与正式数据源走相同绑定流程。
/// </summary>
public sealed class JsonDevelopmentDataSourceInitializer : IDevelopmentDataSourceInitializer
{
    private const string SourceCode = "DEV_JSON_REPORT_DATA";
    private const string FixtureResourceName = "report-development-fixture-v1.json";
    private const string FixtureVersion = "1.0";

    private readonly IDataSourceRepository _sources;
    private readonly IDataSnapshotRepository _snapshots;
    private readonly IDataFieldRepository _fields;
    private readonly IProjectRepository _projects;
    private readonly ICurrentUserContext _user;

    public JsonDevelopmentDataSourceInitializer(
        IDataSourceRepository sources,
        IDataSnapshotRepository snapshots,
        IDataFieldRepository fields,
        IProjectRepository projects,
        ICurrentUserContext user)
    {
        _sources = sources;
        _snapshots = snapshots;
        _fields = fields;
        _projects = projects;
        _user = user;
    }

    public async Task<DevelopmentDataSourceInitializationResult> EnsureInitializedAsync(
        ulong projectId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        _ = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");

        string fixtureJson = LoadFixture();

        // Find existing dev data source by code
        ulong dataSourceId;
        bool created = false;
        DataSourceRecord? existing = await FindDevDataSourceAsync(projectId, cancellationToken);

        if (existing is not null)
        {
            dataSourceId = existing.Id;
            if (!forceRefresh)
            {
                DataSnapshotRecord? latestReady = await _snapshots.GetLatestReadyAsync(
                    dataSourceId, cancellationToken);
                if (latestReady is not null)
                {
                    int existingFieldCount = (await _fields.ListAsync(
                        latestReady.Id, null, 5000, cancellationToken)).Count;
                    return new DevelopmentDataSourceInitializationResult
                    {
                        ProjectId = projectId,
                        DataSourceId = dataSourceId,
                        SnapshotId = latestReady.Id,
                        FieldCount = existingFieldCount,
                        Created = false,
                        Refreshed = false,
                    };
                }
            }
        }
        else
        {
            dataSourceId = await CreateDevDataSourceAsync(projectId, cancellationToken);
            created = true;
        }

        // Create snapshot and parse fields
        DataSnapshotRecord snapshot = await _snapshots.StartAsync(
            dataSourceId, _user.UserId, cancellationToken);

        try
        {
            List<DataFieldRecord> fields = ParseFields(snapshot.Id, fixtureJson);
            await _fields.ReplaceAsync(snapshot.Id, fields, cancellationToken);

            string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(fixtureJson)))
                .ToLowerInvariant();

            string snapshotSchema = JsonSerializer.Serialize(
                new
                {
                    provider = "BUILTIN_JSON_FIXTURE",
                    fixtureName = FixtureResourceName,
                    fixtureVersion = FixtureVersion,
                },
                JsonOptions);

            await _snapshots.CompleteAsync(
                snapshot.Id, fixtureJson, snapshotSchema, hash, 1, cancellationToken);

            string sourceSchema = JsonSerializer.Serialize(
                new
                {
                    provider = "BUILTIN_JSON_FIXTURE",
                    fixtureName = FixtureResourceName,
                    fixtureVersion = FixtureVersion,
                    sourceWatermark = $"fixture:{FixtureResourceName}:{FixtureVersion}",
                    fieldCount = fields.Count,
                },
                JsonOptions);

            await _sources.UpdateSchemaAsync(dataSourceId, sourceSchema, cancellationToken);

            return new DevelopmentDataSourceInitializationResult
            {
                ProjectId = projectId,
                DataSourceId = dataSourceId,
                SnapshotId = snapshot.Id,
                FieldCount = fields.Count,
                Created = created,
                Refreshed = true,
            };
        }
        catch (OperationCanceledException)
        {
            await _snapshots.FailAsync(snapshot.Id, "初始化已取消。", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await _snapshots.FailAsync(snapshot.Id, SafeMessage(ex), CancellationToken.None);
            throw new WorkspaceException(
                "development_data_source_initialization_failed",
                $"初始化测试数据源失败：{SafeMessage(ex)}",
                ex);
        }
    }

    private async Task<DataSourceRecord?> FindDevDataSourceAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DataSourceRecord> all = await _sources.ListAsync(
            projectId, cancellationToken);
        return all.FirstOrDefault(s =>
            string.Equals(s.SourceCode, SourceCode, StringComparison.Ordinal));
    }

    private async Task<ulong> CreateDevDataSourceAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        DataSourceRecord record = await _sources.CreateAsync(
            new DataSourceRecord
            {
                Id = 0,
                ProjectId = projectId,
                ConnectionId = 0, // JSON sources have no connection
                SourceCode = SourceCode,
                SourceName = "开发测试 JSON 数据",
                SourceType = "JSON",
                SourceStatus = "ACTIVE",
                SchemaName = string.Empty,
                ObjectType = string.Empty,
                ObjectName = string.Empty,
                SchemaJson = JsonSerializer.Serialize(
                    new
                    {
                        provider = "BUILTIN_JSON_FIXTURE",
                        fixtureName = FixtureResourceName,
                        fixtureVersion = FixtureVersion,
                    },
                    JsonOptions),
                CreatedAt = DateTimeOffset.UtcNow,
            },
            _user.UserId,
            cancellationToken);
        return record.Id;
    }

    private static string LoadFixture()
    {
        // Search for fixture file in common locations
        string[] searchPaths =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataSchema", "Fixtures", FixtureResourceName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FixtureResourceName),
            // When running from project directory (dotnet run), BaseDirectory is the bin output
            // Try relative path from bin to source
            Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "src", "WordTemplateBinding.Infrastructure",
                "DataSchema", "Fixtures", FixtureResourceName)),
        };

        foreach (string path in searchPaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }

        // Fallback: search recursively
        string? found = Directory.GetFiles(
            AppDomain.CurrentDomain.BaseDirectory,
            FixtureResourceName,
            SearchOption.AllDirectories).FirstOrDefault();

        if (found is not null)
        {
            return File.ReadAllText(found, Encoding.UTF8);
        }

        // Embedded fallback matching InMemoryDataValueProvider values
        return EmbeddedFixture;
    }

    private static List<DataFieldRecord> ParseFields(ulong snapshotId, string json)
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 32 });
        List<DataFieldRecord> fields = new();
        int order = 0;
        ParseObject(snapshotId, string.Empty, document.RootElement, fields, ref order);
        return fields;
    }

    private static void ParseObject(
        ulong snapshotId,
        string prefix,
        JsonElement element,
        List<DataFieldRecord> fields,
        ref int order)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string path = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";
            ParseValue(snapshotId, path, property.Name, property.Value, fields, ref order);
        }
    }

    private static void ParseValue(
        ulong snapshotId,
        string path,
        string name,
        JsonElement element,
        List<DataFieldRecord> fields,
        ref int order,
        bool isArrayItem = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (!isArrayItem)
                {
                    fields.Add(MakeField(snapshotId, path, name, DataValueType.Object,
                        false, false, null, ref order));
                }
                ParseObject(snapshotId, path, element, fields, ref order);
                break;

            case JsonValueKind.Array:
                fields.Add(MakeField(snapshotId, path, name, DataValueType.Array,
                    true, true, SafeSerialize(element), ref order));
                if (element.GetArrayLength() > 0)
                {
                    JsonElement first = element[0];
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        string itemPath = $"{path}[]";
                        foreach (JsonProperty prop in first.EnumerateObject())
                        {
                            string childPath = $"{itemPath}.{prop.Name}";
                            ParseValue(snapshotId, childPath, prop.Name, prop.Value,
                                fields, ref order, isArrayItem: true);
                        }
                    }
                }
                break;

            case JsonValueKind.String:
            {
                DataValueType dt = IsDateValue(element.GetString())
                    ? DataValueType.Date : DataValueType.String;
                fields.Add(MakeField(snapshotId, path, name, dt,
                    false, true, SafeSerialize(element), ref order));
                break;
            }

            case JsonValueKind.Number:
            {
                DataValueType nt = element.TryGetInt64(out _)
                    ? DataValueType.Integer : DataValueType.Decimal;
                fields.Add(MakeField(snapshotId, path, name, nt,
                    false, true, SafeSerialize(element), ref order));
                break;
            }

            case JsonValueKind.True:
            case JsonValueKind.False:
                fields.Add(MakeField(snapshotId, path, name, DataValueType.Boolean,
                    false, true, SafeSerialize(element), ref order));
                break;

            case JsonValueKind.Null:
                fields.Add(MakeField(snapshotId, path, name, DataValueType.String,
                    false, true, "null", ref order));
                break;
        }
    }

    private static DataFieldRecord MakeField(
        ulong snapshotId, string path, string name, DataValueType type,
        bool isArray, bool isBindable, string? sample, ref int order) =>
        new()
        {
            Id = 0,
            SnapshotId = snapshotId,
            FieldPath = path,
            FieldName = name,
            Comment = null,
            DataType = type,
            IsArray = isArray,
            IsNullable = true,
            IsBindable = isBindable,
            SampleValueJson = sample,
            DisplayOrder = order++,
        };

    private static bool IsDateValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 10)
            return false;
        return value.Length >= 10 && value[4] == '-' && value[7] == '-' &&
               DateTime.TryParse(value, out _);
    }

    private static string? SafeSerialize(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : JsonSerializer.Serialize(element, JsonOptions);

    private static string SafeMessage(Exception ex) => ex switch
    {
        WordTemplateBindingException business => business.Message,
        _ => "数据源初始化失败。",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// 内置 Fixture 兜底数据，与 InMemoryDataValueProvider 值保持一致。
    /// </summary>
    private const string EmbeddedFixture = """
    {
      "Report": {
        "Str1": "四年级学生科学",
        "Str2": "监测总览",
        "Str3": "报告总结",
        "Title": "本年度学生成绩统计报告",
        "GeneratedAt": "2026-07-17T00:00:00Z",
        "IsFinal": true,
        "ReportYear": 2026,
        "SchoolName": "测试学校"
      },
      "StudentStatistics": {
        "AverageScore": 543,
        "StudentCount": 209,
        "PassRate": 96.8,
        "ProvinceAverageScore": 506
      },
      "ChartData": {
        "ScienceScores": [
          { "Category": "四年级", "你县": 552, "全省": 506 },
          { "Category": "八年级", "你县": 518, "全省": 493 }
        ],
        "QuarterlySales": [
          { "quarter": "第一季度", "onlineSales": 1280, "offlineSales": 3420, "totalSales": 4700 },
          { "quarter": "第二季度", "onlineSales": 1560, "offlineSales": 3890, "totalSales": 5450 },
          { "quarter": "第三季度", "onlineSales": 1420, "offlineSales": 4010, "totalSales": 5430 },
          { "quarter": "第四季度", "onlineSales": 1890, "offlineSales": 4520, "totalSales": 6410 }
        ],
        "RegionGDP": [
          { "region": "华东地区", "gdp": 48650, "growthRate": 5.8 },
          { "region": "华南地区", "gdp": 34120, "growthRate": 6.2 },
          { "region": "华北地区", "gdp": 29870, "growthRate": 4.5 },
          { "region": "西南地区", "gdp": 18540, "growthRate": 7.1 },
          { "region": "西北地区", "gdp": 9620, "growthRate": 6.8 },
          { "region": "东北地区", "gdp": 13780, "growthRate": 3.2 }
        ],
        "ProductRatings": [
          { "dimension": "产品质量", "satisfaction": 92.3, "recommendation": 88.5 },
          { "dimension": "售后服务", "satisfaction": 85.7, "recommendation": 82.1 },
          { "dimension": "物流速度", "satisfaction": 78.4, "recommendation": 75.3 },
          { "dimension": "价格合理", "satisfaction": 71.2, "recommendation": 68.9 },
          { "dimension": "界面体验", "satisfaction": 89.1, "recommendation": 86.4 }
        ],
        "MonthlyTemperature": [
          { "month": "1月", "maxTemp": 5.2, "minTemp": -3.8, "avgTemp": 0.7 },
          { "month": "2月", "maxTemp": 8.1, "minTemp": -1.2, "avgTemp": 3.4 },
          { "month": "3月", "maxTemp": 14.6, "minTemp": 3.5, "avgTemp": 9.0 },
          { "month": "4月", "maxTemp": 21.3, "minTemp": 9.8, "avgTemp": 15.5 },
          { "month": "5月", "maxTemp": 26.8, "minTemp": 15.2, "avgTemp": 21.0 },
          { "month": "6月", "maxTemp": 30.5, "minTemp": 20.1, "avgTemp": 25.3 },
          { "month": "7月", "maxTemp": 33.2, "minTemp": 23.6, "avgTemp": 28.4 },
          { "month": "8月", "maxTemp": 32.1, "minTemp": 22.8, "avgTemp": 27.5 },
          { "month": "9月", "maxTemp": 27.4, "minTemp": 17.5, "avgTemp": 22.4 },
          { "month": "10月", "maxTemp": 21.8, "minTemp": 11.2, "avgTemp": 16.5 },
          { "month": "11月", "maxTemp": 13.5, "minTemp": 4.6, "avgTemp": 9.0 },
          { "month": "12月", "maxTemp": 7.8, "minTemp": -1.5, "avgTemp": 3.1 }
        ]
      },
      "DepartmentStatistics": {}
    }
    """;
}

#pragma warning restore CS1591
