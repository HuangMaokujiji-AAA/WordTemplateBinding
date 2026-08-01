using System.Globalization;
using MySqlConnector;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.Database;

/// <summary>从 report_platform 中的 9 张高校监测表构造统一报告数据对象。</summary>
public sealed class MySqlHigherEducationReportDataProvider
    : IHigherEducationReportDataProvider
{
    private static readonly IReadOnlyList<MetricColumn> Level1MetricColumns =
        new[]
        {
            new MetricColumn("s1EmploymentScore", "就业"),
            new MetricColumn("s2AdmissionScore", "招生"),
            new MetricColumn("s3CultivationScore", "培养"),
        };

    private static readonly IReadOnlyList<MetricColumn> Level2MetricColumns =
        new[]
        {
            new MetricColumn("s11GraduateDestinationScore", "毕业去向"),
            new MetricColumn("s21AdmissionPlanScore", "招生计划"),
            new MetricColumn("s22StudentSourceQualityScore", "生源质量"),
            new MetricColumn("s23MajorRecognitionScore", "专业认可度"),
            new MetricColumn("s31IdeologicalEducationScore", "思政教育"),
            new MetricColumn("s32TeachingInputScore", "教学投入"),
            new MetricColumn("s33FacultyTeamScore", "师资队伍"),
            new MetricColumn("s34MajorDevelopmentScore", "专业建设"),
            new MetricColumn("s35CultivationOutcomeScore", "培养成效"),
        };

    private static readonly IReadOnlyList<MetricColumn> Level3MetricColumns =
        new[]
        {
            new MetricColumn("s111DestinationImplementationRateScore", "去向落实率"),
            new MetricColumn("s112JobRelevanceRateScore", "对口率"),
            new MetricColumn("s211AdmissionPlanCompletionRateScore", "招生计划达成率"),
            new MetricColumn("s221StudentSourceQualityScore", "生源质量"),
            new MetricColumn("s231FirstChoiceAdmissionRateScore", "第一志愿录取率"),
            new MetricColumn("s232NetTransferInRatioScore", "净转入学生比例"),
            new MetricColumn("s311IdeologicalCourseOfferingScore", "思政课程开设"),
            new MetricColumn("s312CurriculumIdeologyDevelopmentScore", "课程思政建设"),
            new MetricColumn("s321ProfessorUndergraduateTeachingRateScore", "教授为本科生授课率"),
            new MetricColumn("s322SeniorTitleCourseTeachingRatioScore", "专业课高职称教师讲授比例"),
            new MetricColumn("s323ThesisSupervisionPerTeacherScore", "师均指导毕业论文"),
            new MetricColumn("s331MajorStudentTeacherRatioScore", "专业生师比"),
            new MetricColumn("s332CounselorStudentTeacherRatioScore", "辅导员生师比"),
            new MetricColumn("s333MasterDegreeTeacherRatioScore", "硕士以上专任教师比例"),
            new MetricColumn("s334AssociateSeniorTeacherRatioScore", "副高以上专任教师比例"),
            new MetricColumn("s335IndustryBackgroundTeacherRatioScore", "行业背景专任教师比例"),
            new MetricColumn("s336HighLevelTeacherRatioScore", "高水平专任教师比例"),
            new MetricColumn("s337MajorLeaderScore", "专业负责人"),
            new MetricColumn("s341FeaturedMajorScore", "优势特色专业"),
            new MetricColumn("s342ProvincialTeachingReformScore", "省级以上教研教改"),
            new MetricColumn("s343TeachingResearchAchievementScore", "教学研究成果"),
            new MetricColumn("s344HighQualityTextbookScore", "高水平教材"),
            new MetricColumn("s351PatentPublicationScore", "专利与发表论文"),
            new MetricColumn("s352ProvincialCompetitionAwardScore", "省部级以上竞赛奖励"),
        };

    private readonly IReportPlatformDatabaseConnectionFactory _connections;

    /// <summary>初始化高校监测报告数据提供器。</summary>
    public MySqlHigherEducationReportDataProvider(
        IReportPlatformDatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListYearsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT collection_year
            FROM he_school_overview
            ORDER BY collection_year DESC;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        List<string> years = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            years.Add(reader.GetString(0));
        }

        return years.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HigherEducationSchool>> ListSchoolsAsync(
        string collectionYear,
        CancellationToken cancellationToken = default)
    {
        ValidateYear(collectionYear);
        const string sql = """
            SELECT collection_year, school_code, school_name
            FROM he_school_overview
            WHERE collection_year = @collectionYear
            ORDER BY school_name, school_code;
            """;
        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@collectionYear", collectionYear);
        List<HigherEducationSchool> schools = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schools.Add(new HigherEducationSchool(
                reader.GetString("collection_year"),
                reader.GetString("school_code"),
                reader.GetString("school_name")));
        }

        return schools.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<HigherEducationReportData> BuildReportAsync(
        string collectionYear,
        string schoolCode,
        CancellationToken cancellationToken = default)
    {
        ValidateYear(collectionYear);
        if (string.IsNullOrWhiteSpace(schoolCode) || schoolCode.Length > 32)
        {
            throw new WorkspaceException("invalid_school_code", "学校代码不能为空且不能超过 32 个字符。");
        }

        await using MySqlConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> schoolRows = await ReadRowsAsync(
            connection,
            """
            SELECT *
            FROM he_school_overview
            WHERE collection_year = @collectionYear AND school_code = @schoolCode
            LIMIT 1;
            """,
            collectionYear,
            schoolCode.Trim(),
            cancellationToken);
        if (schoolRows.Count == 0)
        {
            throw new WorkspaceException(
                "higher_education_school_not_found",
                $"未找到 {collectionYear} 年学校代码 {schoolCode} 的监测数据。");
        }

        IReadOnlyDictionary<string, object?> school = schoolRows[0];
        string schoolName = Convert.ToString(
                school.GetValueOrDefault("schoolName"),
                CultureInfo.InvariantCulture)
            ?? schoolCode;
        IReadOnlyList<IReadOnlyDictionary<string, object?>> undergraduateMajors =
            await ReadRowsAsync(
                connection,
                """
                SELECT *
                FROM he_undergraduate_major_summary
                WHERE collection_year = @collectionYear AND school_code = @schoolCode
                ORDER BY discipline_code;
                """,
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> teachingMetrics =
            await ReadRowsAsync(
                connection,
                """
                SELECT *,
                       CASE
                           WHEN display_order = '02'
                               THEN CONCAT(metric_name, '（应届生就业岗位与专业相关度）')
                           ELSE metric_name
                       END AS display_name,
                       CASE
                           WHEN display_order IN ('04', '08')
                               THEN CAST(CAST(metric_value AS UNSIGNED) AS CHAR)
                           ELSE CONCAT(CAST(metric_value AS DECIMAL(12,2)), '%')
                       END AS display_value
                FROM he_teaching_key_metric
                WHERE collection_year = @collectionYear AND school_code = @schoolCode
                ORDER BY display_order;
                """,
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> featuredMajors =
            await ReadRowsAsync(
                connection,
                """
                SELECT *
                FROM he_featured_major
                WHERE collection_year = @collectionYear AND school_code = @schoolCode
                ORDER BY major_code;
                """,
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> warningMajors =
            await ReadRowsAsync(
                connection,
                """
                SELECT *
                FROM he_warning_major
                WHERE collection_year = @collectionYear AND school_code = @schoolCode
                ORDER BY major_code;
                """,
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> majorCollegeRows =
            await ReadRowsAsync(
                connection,
                """
                SELECT *, COALESCE(monitoring_status, '-') AS monitoring_display
                FROM he_major_college
                WHERE collection_year = @collectionYear AND school_code = @schoolCode
                ORDER BY college_code, major_code;
                """,
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> majorColleges =
            majorCollegeRows.Select(NormalizeMajorCollegeRow).ToList().AsReadOnly();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level1 =
            await ReadMetricRowsAsync(
                connection,
                "he_major_level1_metric",
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level2 =
            await ReadMetricRowsAsync(
                connection,
                "he_major_level2_metric",
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level3 =
            await ReadMetricRowsAsync(
                connection,
                "he_major_level3_metric",
                collectionYear,
                schoolCode,
                cancellationToken);
        IReadOnlyDictionary<string, object?> majorMetrics = BuildMajorMetricChartData(
            schoolCode,
            majorColleges,
            level1,
            level2,
            level3);

        IReadOnlyList<object> collegeGroups = majorColleges
            .GroupBy(row => Convert.ToString(
                row.GetValueOrDefault("collegeCode"),
                CultureInfo.InvariantCulture) ?? string.Empty)
            .Select(group => (object)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["collegeCode"] = group.Key,
                ["collegeName"] = group.First().GetValueOrDefault("collegeName"),
                ["majors"] = group.ToList(),
            })
            .ToList()
            .AsReadOnly();

        Dictionary<string, object?> content = new(StringComparer.Ordinal)
        {
            ["collectionYear"] = collectionYear,
            ["schoolCode"] = schoolCode,
            ["schoolName"] = schoolName,
            ["school"] = school,
            ["undergraduateMajors"] = undergraduateMajors,
            ["teachingMetrics"] = teachingMetrics,
            ["featuredMajors"] = featuredMajors,
            ["warningMajors"] = warningMajors,
            ["majorColleges"] = majorColleges,
            ["collegeGroups"] = collegeGroups,
            ["majorMetrics"] = majorMetrics,
        };
        ulong rowCount = checked((ulong)(
            1 + undergraduateMajors.Count + teachingMetrics.Count +
            featuredMajors.Count + warningMajors.Count + majorColleges.Count +
            level1.Count + level2.Count + level3.Count));
        return new HigherEducationReportData
        {
            CollectionYear = collectionYear,
            SchoolCode = schoolCode,
            SchoolName = schoolName,
            Content = content,
            RowCount = rowCount,
        };
    }

    internal static IReadOnlyDictionary<string, object?> BuildMajorMetricChartData(
        string schoolCode,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> majorColleges,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level1,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level2,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> level3)
    {
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> level1Index =
            IndexMetricRows(level1);
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> level2Index =
            IndexMetricRows(level2);
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> level3Index =
            IndexMetricRows(level3);
        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach (IReadOnlyDictionary<string, object?> major in majorColleges
                     .GroupBy(row => GetString(row, "majorCode"), StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            string majorCode = GetString(major, "majorCode");
            if (string.IsNullOrWhiteSpace(majorCode))
            {
                continue;
            }

            string majorName = GetString(major, "majorName");
            result[majorCode] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["majorName"] = majorName,
                ["collegeName"] = GetString(major, "collegeName"),
                ["level1RadarData"] = BuildRadarRows(
                    schoolCode,
                    majorCode,
                    majorName,
                    level1Index.GetValueOrDefault(majorCode),
                    Level1MetricColumns),
                ["level2RadarData"] = BuildRadarRows(
                    schoolCode,
                    majorCode,
                    majorName,
                    level2Index.GetValueOrDefault(majorCode),
                    Level2MetricColumns),
                ["level3RadarData"] = BuildRadarRows(
                    schoolCode,
                    majorCode,
                    majorName,
                    level3Index.GetValueOrDefault(majorCode),
                    Level3MetricColumns),
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> IndexMetricRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        rows.GroupBy(row => GetString(row, "majorCode"), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)group.ToList().AsReadOnly(),
                StringComparer.Ordinal);

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildRadarRows(
        string schoolCode,
        string majorCode,
        string majorName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows,
        IReadOnlyList<MetricColumn> metricColumns)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> availableRows =
            rows ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        IReadOnlyDictionary<string, object?> school = RequireMetricRow(
            availableRows,
            row => string.Equals(GetString(row, "schoolCode"), schoolCode, StringComparison.Ordinal),
            majorCode,
            "本校");
        IReadOnlyDictionary<string, object?> average = RequireMetricRow(
            availableRows,
            row => IsProvincialMetricRow(row, "01"),
            majorCode,
            "全省平均");
        IReadOnlyDictionary<string, object?> maximum = RequireMetricRow(
            availableRows,
            row => IsProvincialMetricRow(row, "02"),
            majorCode,
            "全省最高");
        IReadOnlyDictionary<string, object?> minimum = RequireMetricRow(
            availableRows,
            row => IsProvincialMetricRow(row, "03"),
            majorCode,
            "全省最低");
        string schoolSeriesName = string.IsNullOrWhiteSpace(majorName)
            ? majorCode
            : majorName;

        return metricColumns.Select(metric =>
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["category"] = metric.Label,
                    ["平均值"] = GetMetricValue(average, metric.Field),
                    ["最大值"] = GetMetricValue(maximum, metric.Field),
                    ["最小值"] = GetMetricValue(minimum, metric.Field),
                    [schoolSeriesName] = GetMetricValue(school, metric.Field),
                })
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyDictionary<string, object?> RequireMetricRow(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate,
        string majorCode,
        string seriesName) =>
        rows.FirstOrDefault(predicate)
        ?? throw new WorkspaceException(
            "higher_education_metric_incomplete",
            $"专业 {majorCode} 缺少{seriesName}指标，无法生成雷达图数据。");

    private static bool IsProvincialMetricRow(
        IReadOnlyDictionary<string, object?> row,
        string displayOrder) =>
        string.Equals(GetString(row, "schoolCode"), "全省同专业", StringComparison.Ordinal) &&
        string.Equals(GetString(row, "displayOrder"), displayOrder, StringComparison.Ordinal);

    private static object? GetMetricValue(
        IReadOnlyDictionary<string, object?> row,
        string field) =>
        row.TryGetValue(field, out object? value) ? value : null;

    private static string GetString(
        IReadOnlyDictionary<string, object?> row,
        string field) =>
        Convert.ToString(row.GetValueOrDefault(field), CultureInfo.InvariantCulture)
        ?? string.Empty;

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadMetricRowsAsync(
        MySqlConnection connection,
        string tableName,
        string collectionYear,
        string schoolCode,
        CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT *
            FROM {tableName}
            WHERE collection_year = @collectionYear
              AND (
                  school_code = @schoolCode
                  OR (
                      school_code = '全省同专业'
                      AND major_code IN (
                          SELECT major_code
                          FROM he_major_college
                          WHERE collection_year = @collectionYear
                            AND school_code = @schoolCode
                      )
                  )
              )
            ORDER BY major_code,
                     CASE WHEN school_code = @schoolCode THEN 0 ELSE 1 END,
                     display_order;
            """;
        return await ReadRowsAsync(
            connection,
            sql,
            collectionYear,
            schoolCode,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        MySqlConnection connection,
        string sql,
        string collectionYear,
        string schoolCode,
        CancellationToken cancellationToken)
    {
        await using MySqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@collectionYear", collectionYear);
        command.Parameters.AddWithValue("@schoolCode", schoolCode);
        List<IReadOnlyDictionary<string, object?>> rows = new();
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Dictionary<string, object?> row = new(StringComparer.Ordinal);
            for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                string column = reader.GetName(ordinal);
                if (string.Equals(column, "id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                row[ToCamelCase(column)] = reader.IsDBNull(ordinal)
                    ? null
                    : reader.GetValue(ordinal);
            }

            rows.Add(row);
        }

        return rows.AsReadOnly();
    }

    private static string ToCamelCase(string value)
    {
        string[] parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return value;
        }

        return parts[0] + string.Concat(parts.Skip(1).Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static IReadOnlyDictionary<string, object?> NormalizeMajorCollegeRow(
        IReadOnlyDictionary<string, object?> source)
    {
        Dictionary<string, object?> row = new(source, StringComparer.Ordinal);
        if (row.TryGetValue("majorEstablishmentYears", out object? value) &&
            value is not null)
        {
            string normalized = string.Join(
                ",",
                Convert.ToString(value, CultureInfo.InvariantCulture)?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                ?? Array.Empty<string>());
            row["majorEstablishmentYears"] = normalized;
        }

        return row;
    }

    private static void ValidateYear(string collectionYear)
    {
        if (collectionYear.Length != 4 || !collectionYear.All(char.IsDigit))
        {
            throw new WorkspaceException("invalid_collection_year", "采集年度必须是 4 位数字。");
        }
    }

    private sealed record MetricColumn(string Field, string Label);
}
