#pragma warning disable CS1591
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordTemplateBinding.Core.Enums;
using WordTemplateBinding.Core.Exceptions;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Options;

namespace WordTemplateBinding.Core.Services;
public sealed record DataConnectionTestResult(
    string Status,
    string Provider,
    string Database,
    string? ServerVersion,
    string Message);

public sealed class DataConnectionService
{
    private readonly IDataConnectionRepository _connections;
    private readonly IProjectRepository _projects;
    private readonly IDatabaseSchemaIntrospector _introspector;
    private readonly IDataSourceConnectionFactory _connectionFactory;
    private readonly ApplicationIdentityOptions _identity;

    public DataConnectionService(
        IDataConnectionRepository connections,
        IProjectRepository projects,
        IDatabaseSchemaIntrospector introspector,
        IDataSourceConnectionFactory connectionFactory,
        ApplicationIdentityOptions identity)
    {
        _connections = connections;
        _projects = projects;
        _introspector = introspector;
        _connectionFactory = connectionFactory;
        _identity = identity;
    }

    public Task<IReadOnlyList<DataConnectionRecord>> ListAsync(
        ulong? projectId,
        CancellationToken cancellationToken) =>
        _connections.ListAsync(projectId, cancellationToken);

    public async Task<DataConnectionRecord> GetAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _connections.GetAsync(id, cancellationToken)
        ?? throw new WorkspaceException(
            "data_connection_not_found",
            $"找不到数据连接：{id}。");

    public async Task<DataConnectionRecord> CreateAsync(
        ulong? projectId,
        string name,
        string type,
        DataConnectionConfig config,
        string credentialRef,
        CancellationToken cancellationToken)
    {
        if (projectId.HasValue &&
            await _projects.GetAsync(projectId.Value, cancellationToken) is null)
        {
            throw new WorkspaceException(
                "project_not_found",
                $"找不到项目：{projectId.Value}。");
        }

        if (!string.Equals(type, "MYSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "unsupported_data_connection_type",
                "本阶段只支持 MYSQL 数据连接。");
        }

        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 255 ||
            string.IsNullOrWhiteSpace(config.Host) ||
            string.IsNullOrWhiteSpace(config.Database) ||
            string.IsNullOrWhiteSpace(credentialRef) ||
            credentialRef.Length > 255 ||
            !credentialRef.StartsWith(
                "config:DataSourceCredentials:",
                StringComparison.OrdinalIgnoreCase) ||
            config.Port == 0)
        {
            throw new WorkspaceException(
                "invalid_data_connection",
                "数据连接名称、主机、端口、数据库和凭据引用均不能为空。");
        }

        return await _connections.CreateAsync(
            new DataConnectionRecord
            {
                Id = 0,
                ProjectId = projectId,
                ConnectionName = name.Trim(),
                ConnectionType = "MYSQL",
                Config = config,
                CredentialRef = credentialRef.Trim(),
                ConnectionStatus = "ACTIVE",
                LastTestedAt = null,
                LastTestResultJson = null,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ParseActor(),
            cancellationToken);
    }

    public async Task<DataConnectionTestResult> TestAsync(
        ulong id,
        CancellationToken cancellationToken)
    {
        DataConnectionRecord connection = await GetAsync(id, cancellationToken);
        try
        {
            await using System.Data.Common.DbConnection opened =
                await _connectionFactory.OpenAsync(connection, cancellationToken);
            string version = opened.ServerVersion;
            DataConnectionTestResult result = new(
                "healthy",
                "MySQL",
                connection.Config.Database,
                version,
                "数据连接成功。");
            await _connections.UpdateTestResultAsync(
                id,
                "ACTIVE",
                JsonSerializer.Serialize(result, JsonOptions),
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            DataConnectionTestResult result = new(
                "unavailable",
                "MySQL",
                connection.Config.Database,
                null,
                "无法连接业务数据库，请检查服务端凭据、网络和 TLS 设置。");
            await _connections.UpdateTestResultAsync(
                id,
                "ERROR",
                JsonSerializer.Serialize(result, JsonOptions),
                CancellationToken.None);
            throw new WorkspaceException(
                "data_connection_unavailable",
                result.Message,
                exception);
        }
    }

    public async Task<IReadOnlyList<string>> ListSchemasAsync(
        ulong id,
        CancellationToken cancellationToken) =>
        await _introspector.ListSchemasAsync(
            await GetAsync(id, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(
        ulong id,
        string? schema,
        CancellationToken cancellationToken) =>
        await _introspector.ListObjectsAsync(
            await GetAsync(id, cancellationToken),
            schema,
            cancellationToken);

    public async Task<IReadOnlyList<DatabaseColumnInfo>> ListColumnsAsync(
        ulong id,
        string schema,
        string objectName,
        CancellationToken cancellationToken) =>
        await _introspector.ListColumnsAsync(
            await GetAsync(id, cancellationToken),
            schema,
            objectName,
            cancellationToken);

    private ulong? ParseActor() =>
        ulong.TryParse(_identity.DefaultActorUserId, out ulong actor) && actor > 0
            ? actor
            : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

#pragma warning restore CS1591

