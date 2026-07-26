using System.Data;
using System.Text.Json;
using MySqlConnector;

namespace WordTemplateBinding.Infrastructure.Database;

internal static class MySqlDataAccess
{
    internal static MySqlParameter AddParameter(
        this MySqlCommand command,
        string name,
        object? value,
        MySqlDbType? type = null)
    {
        MySqlParameter parameter = command.Parameters.Add(name, type ?? InferType(value));
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    internal static ulong GetUInt64(this MySqlDataReader reader, string name) =>
        reader.GetUInt64(reader.GetOrdinal(name));

    internal static uint GetUInt32(this MySqlDataReader reader, string name) =>
        reader.GetUInt32(reader.GetOrdinal(name));

    internal static int GetInt32(this MySqlDataReader reader, string name) =>
        reader.GetInt32(reader.GetOrdinal(name));

    internal static string GetString(this MySqlDataReader reader, string name) =>
        reader.GetString(reader.GetOrdinal(name));

    internal static string? GetNullableString(this MySqlDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    internal static DateTimeOffset GetDateTimeOffset(this MySqlDataReader reader, string name)
    {
        DateTime value = reader.GetDateTime(reader.GetOrdinal(name));
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    internal static DateTimeOffset? GetNullableDateTimeOffset(
        this MySqlDataReader reader,
        string name)
    {
        int ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        DateTime value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    internal static string SerializeJson<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static MySqlDbType InferType(object? value) => value switch
    {
        byte[] => MySqlDbType.MediumBlob,
        bool => MySqlDbType.Bool,
        DateTime or DateTimeOffset => MySqlDbType.DateTime,
        int or uint => MySqlDbType.Int32,
        long or ulong => MySqlDbType.UInt64,
        decimal => MySqlDbType.Decimal,
        _ => MySqlDbType.VarChar,
    };
}
