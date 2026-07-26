#pragma warning disable CS1591
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Infrastructure.DataSchema;

public sealed class ConfigurationDataConnectionCredentialResolver
    : IDataConnectionCredentialResolver
{
    private readonly IConfiguration _configuration;

    public ConfigurationDataConnectionCredentialResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public DataConnectionCredential Resolve(string credentialRef)
    {
        const string prefix = "config:DataSourceCredentials:";
        if (string.IsNullOrWhiteSpace(credentialRef) ||
            !credentialRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                "数据连接凭据引用无效；仅支持服务端 config: 配置引用。");
        }

        string key = credentialRef[prefix.Length..];
        if (string.IsNullOrWhiteSpace(key) ||
            !key.All(character =>
                char.IsLetterOrDigit(character) ||
                character is '_' or '-'))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                "数据连接凭据引用键无效。");
        }

        IConfigurationSection section = _configuration.GetSection(
            $"DataSourceCredentials:{key}");
        string username = section["Username"] ?? string.Empty;
        string password = section["Password"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                "数据连接凭据尚未在服务端配置。");
        }

        return new DataConnectionCredential(username, password);
    }
}

public sealed class MySqlDataSourceConnectionFactory : IDataSourceConnectionFactory
{
    private readonly IDataConnectionCredentialResolver _credentials;
    private readonly DataSourceOptions _options;

    public MySqlDataSourceConnectionFactory(
        IDataConnectionCredentialResolver credentials,
        DataSourceOptions options)
    {
        _credentials = credentials;
        _options = options;
    }

    public async Task<DbConnection> OpenAsync(
        DataConnectionRecord connection,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(connection.ConnectionType, "MYSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "unsupported_data_connection_type",
                $"当前只支持 MYSQL 数据连接，不支持 {connection.ConnectionType}。");
        }

        if (string.IsNullOrWhiteSpace(connection.CredentialRef))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                "数据连接没有配置服务端凭据引用。");
        }

        DataConnectionCredential credential =
            _credentials.Resolve(connection.CredentialRef);
        if (!Enum.TryParse(
                connection.Config.SslMode,
                ignoreCase: true,
                out MySqlSslMode sslMode))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                "数据连接 TLS 配置无效。");
        }

        MySqlConnectionStringBuilder builder = new()
        {
            Server = connection.Config.Host,
            Port = connection.Config.Port,
            Database = connection.Config.Database,
            UserID = credential.Username,
            Password = credential.Password,
            SslMode = sslMode,
            Pooling = true,
            PersistSecurityInfo = false,
            AllowLoadLocalInfile = false,
            DefaultCommandTimeout = checked((uint)_options.CommandTimeoutSeconds),
            ConnectionTimeout = checked((uint)_options.CommandTimeoutSeconds),
        };
        MySqlConnection result = new(builder.ConnectionString);
        try
        {
            await result.OpenAsync(cancellationToken);
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }
}

public sealed class MySqlSchemaIntrospector : IDatabaseSchemaIntrospector
{
    private readonly IDataSourceConnectionFactory _connections;
    private readonly DataSourceOptions _options;

    public MySqlSchemaIntrospector(
        IDataSourceConnectionFactory connections,
        DataSourceOptions options)
    {
        _connections = connections;
        _options = options;
    }

    public async Task<IReadOnlyList<string>> ListSchemasAsync(
        DataConnectionRecord connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT SCHEMA_NAME
            FROM INFORMATION_SCHEMA.SCHEMATA
            WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
            ORDER BY SCHEMA_NAME;
            """;
        await using DbConnection dbConnection =
            await _connections.OpenAsync(connection, cancellationToken);
        await using DbCommand command = dbConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        List<string> schemas = new();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas.AsReadOnly();
    }

    public async Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(
        DataConnectionRecord connection,
        string? schema,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            ORDER BY TABLE_TYPE, TABLE_NAME;
            """;
        string selectedSchema = string.IsNullOrWhiteSpace(schema)
            ? connection.Config.Database
            : schema.Trim();
        await EnsureSchemaAsync(connection, selectedSchema, cancellationToken);
        await using DbConnection dbConnection =
            await _connections.OpenAsync(connection, cancellationToken);
        await using DbCommand command = dbConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        AddParameter(command, "@schema", selectedSchema);
        List<DatabaseObjectInfo> objects = new();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(new DatabaseObjectInfo(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "VIEW", StringComparison.OrdinalIgnoreCase)
                    ? "VIEW"
                    : "TABLE"));
        }

        return objects.AsReadOnly();
    }

    public async Task<IReadOnlyList<DatabaseColumnInfo>> ListColumnsAsync(
        DataConnectionRecord connection,
        string schema,
        string objectName,
        CancellationToken cancellationToken)
    {
        await EnsureObjectAsync(connection, schema, objectName, cancellationToken);
        const string sql = """
            SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
                   c.COLUMN_TYPE, c.IS_NULLABLE, c.COLUMN_KEY, c.COLUMN_COMMENT,
                   c.ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS AS c
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @objectName
            ORDER BY c.ORDINAL_POSITION;
            """;
        await using DbConnection dbConnection =
            await _connections.OpenAsync(connection, cancellationToken);
        await using DbCommand command = dbConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        AddParameter(command, "@schema", schema);
        AddParameter(command, "@objectName", objectName);
        List<DatabaseColumnInfo> columns = new();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string dataType = reader.GetString(3);
            string columnType = reader.GetString(4);
            (DataValueType normalized, bool bindable) = MapType(dataType, columnType);
            columns.Add(new DatabaseColumnInfo
            {
                Schema = reader.GetString(0),
                ObjectName = reader.GetString(1),
                ColumnName = reader.GetString(2),
                DatabaseType = columnType,
                IsNullable = string.Equals(
                    reader.GetString(5),
                    "YES",
                    StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey = string.Equals(
                    reader.GetString(6),
                    "PRI",
                    StringComparison.OrdinalIgnoreCase),
                Comment = reader.IsDBNull(7) ? null : reader.GetString(7),
                DataType = normalized,
                IsBindable = bindable,
                Ordinal = reader.GetInt32(8),
            });
        }

        return columns.AsReadOnly();
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadSampleAsync(
        DataConnectionRecord connection,
        string schema,
        string objectName,
        IReadOnlyList<DatabaseColumnInfo> columns,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DatabaseColumnInfo> verified = await ListColumnsAsync(
            connection,
            schema,
            objectName,
            cancellationToken);
        HashSet<string> requestedNames = columns
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<DatabaseColumnInfo> readable = verified
            .Where(column =>
                column.IsBindable &&
                requestedNames.Contains(column.ColumnName))
            .OrderBy(column => column.Ordinal)
            .ToList()
            .AsReadOnly();
        if (readable.Count == 0)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        string selectList = string.Join(
            ", ",
            readable.Select(column => QuoteIdentifier(column.ColumnName)));
        string sql = $"SELECT {selectList} FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(objectName)} LIMIT @limit;";
        await using DbConnection dbConnection =
            await _connections.OpenAsync(connection, cancellationToken);
        await using DbCommand command = dbConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        AddParameter(command, "@limit", Math.Clamp(limit, 1, _options.SampleRowLimit));
        List<IReadOnlyDictionary<string, object?>> rows = new();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Dictionary<string, object?> row = new(StringComparer.Ordinal);
            for (int index = 0; index < readable.Count; index++)
            {
                object? value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                if (value is string text && text.Length > _options.SampleValueMaxLength)
                {
                    value = text[.._options.SampleValueMaxLength];
                }

                row[readable[index].ColumnName] = value;
            }

            rows.Add(row);
        }

        return rows.AsReadOnly();
    }

    private async Task EnsureSchemaAsync(
        DataConnectionRecord connection,
        string schema,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> schemas = await ListSchemasAsync(connection, cancellationToken);
        if (!schemas.Contains(schema, StringComparer.Ordinal))
        {
            throw new WorkspaceException(
                "data_connection_unavailable",
                $"数据库 Schema {schema} 不存在或不可访问。");
        }
    }

    private async Task EnsureObjectAsync(
        DataConnectionRecord connection,
        string schema,
        string objectName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DatabaseObjectInfo> objects = await ListObjectsAsync(
            connection,
            schema,
            cancellationToken);
        if (!objects.Any(item =>
                string.Equals(item.ObjectName, objectName, StringComparison.Ordinal)))
        {
            throw new WorkspaceException(
                "data_source_not_found",
                $"数据库对象 {schema}.{objectName} 不存在或不可访问。");
        }
    }

    private static (DataValueType Type, bool Bindable) MapType(
        string dataType,
        string columnType)
    {
        string normalized = dataType.ToUpperInvariant();
        if (normalized == "TINYINT" &&
            columnType.StartsWith("tinyint(1)", StringComparison.OrdinalIgnoreCase))
        {
            return (DataValueType.Boolean, true);
        }

        return normalized switch
        {
            "CHAR" or "VARCHAR" or "TINYTEXT" or "TEXT" or "MEDIUMTEXT" or
                "LONGTEXT" or "ENUM" or "SET" => (DataValueType.String, true),
            "TINYINT" or "SMALLINT" or "MEDIUMINT" or "INT" or "INTEGER" or
                "BIGINT" => (DataValueType.Integer, true),
            "DECIMAL" or "NUMERIC" or "FLOAT" or "DOUBLE" or "REAL" =>
                (DataValueType.Decimal, true),
            "BIT" or "BOOL" or "BOOLEAN" => (DataValueType.Boolean, true),
            "DATE" or "DATETIME" or "TIMESTAMP" or "TIME" or "YEAR" =>
                (DataValueType.Date, true),
            "JSON" => (DataValueType.Object, false),
            "BINARY" or "VARBINARY" or "TINYBLOB" or "BLOB" or "MEDIUMBLOB" or
                "LONGBLOB" => (DataValueType.Binary, false),
            _ => (DataValueType.Object, false),
        };
    }

    private static string QuoteIdentifier(string value) =>
        $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

#pragma warning restore CS1591
