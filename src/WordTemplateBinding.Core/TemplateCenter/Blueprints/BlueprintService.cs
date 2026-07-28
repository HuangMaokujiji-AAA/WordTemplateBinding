using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Core.Services;

/// <summary>
/// 负责蓝图和蓝图版本的CRUD、校验、发布和复制操作。
/// </summary>
public sealed class BlueprintService
{
    private readonly IReportBlueprintRepository _blueprintRepo;
    private readonly IBlueprintVersionRepository _versionRepo;
    private readonly IBlueprintNodeRepository _nodeRepo;
    private readonly ITemplateVersionRepository _templateVersionRepo;
    private readonly ITemplateRepository _templateRepo;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// 初始化蓝图服务。
    /// </summary>
    public BlueprintService(
        IReportBlueprintRepository blueprintRepo,
        IBlueprintVersionRepository versionRepo,
        IBlueprintNodeRepository nodeRepo,
        ITemplateVersionRepository templateVersionRepo,
        ITemplateRepository templateRepo)
    {
        _blueprintRepo = blueprintRepo;
        _versionRepo = versionRepo;
        _nodeRepo = nodeRepo;
        _templateVersionRepo = templateVersionRepo;
        _templateRepo = templateRepo;
    }

    /// <summary>
    /// 创建新蓝图。
    /// </summary>
    public async Task<BlueprintRecord> CreateAsync(
        string blueprintCode,
        string blueprintName,
        string? description,
        ulong? actorUserId,
        CancellationToken cancellationToken = default) =>
        await _blueprintRepo.CreateAsync(
            blueprintCode, blueprintName, description, actorUserId, cancellationToken);

    /// <summary>
    /// 获取蓝图。
    /// </summary>
    public Task<BlueprintRecord?> GetAsync(
        ulong id,
        CancellationToken cancellationToken = default) =>
        _blueprintRepo.GetAsync(id, cancellationToken);

    /// <summary>
    /// 列出蓝图。
    /// </summary>
    public Task<PagedResult<BlueprintRecord>> ListAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        _blueprintRepo.ListAsync(query, page, pageSize, cancellationToken);

    /// <summary>
    /// 创建新蓝图版本草稿。
    /// </summary>
    public async Task<BlueprintVersionRecord> CreateDraftAsync(
        ulong blueprintId,
        ulong masterTemplateVersionId,
        ulong? actorUserId,
        CancellationToken cancellationToken = default)
    {
        // Validate master template version exists
        TemplateVersionRecord? masterVersion =
            await _templateVersionRepo.GetAsync(masterTemplateVersionId, cancellationToken);
        if (masterVersion is null)
        {
            throw new InvalidOperationException(
                $"主模板版本 {masterTemplateVersionId} 不存在。");
        }

        return await _versionRepo.CreateDraftAsync(
            blueprintId, masterTemplateVersionId, null, actorUserId, cancellationToken);
    }

    /// <summary>
    /// 获取蓝图版本的节点列表。
    /// </summary>
    public Task<IReadOnlyList<BlueprintNodeRecord>> GetNodesAsync(
        ulong versionId,
        CancellationToken cancellationToken = default) =>
        _nodeRepo.ListAsync(versionId, cancellationToken);

    /// <summary>
    /// 更新蓝图版本的节点树。
    /// </summary>
    public async Task UpdateNodesAsync(
        ulong versionId,
        IReadOnlyList<BlueprintNodeRecord> nodes,
        CancellationToken cancellationToken = default)
    {
        // Validate version is DRAFT
        BlueprintVersionRecord? version = await _versionRepo.GetAsync(versionId, cancellationToken);
        if (version is null)
        {
            throw new InvalidOperationException($"蓝图版本 {versionId} 不存在。");
        }

        if (!string.Equals(version.VersionStatus, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"蓝图版本 {versionId} 状态为 {version.VersionStatus}，只有 DRAFT 状态可以编辑节点。");
        }

        // Validate all template version references
        foreach (BlueprintNodeRecord node in nodes)
        {
            if (node.TemplateVersionId.HasValue)
            {
                TemplateVersionRecord? tv =
                    await _templateVersionRepo.GetAsync(
                        node.TemplateVersionId.Value, cancellationToken);
                if (tv is null)
                {
                    throw new InvalidOperationException(
                        $"节点 \"{node.NodeKey}\" 引用的模板版本 {node.TemplateVersionId} 不存在。");
                }
            }
        }

        await _nodeRepo.ReplaceAsync(versionId, nodes, cancellationToken);
    }

    /// <summary>
    /// 验证蓝图版本。
    /// </summary>
    public async Task<BlueprintValidationResult> ValidateAsync(
        ulong versionId,
        CancellationToken cancellationToken = default)
    {
        BlueprintVersionRecord? version = await _versionRepo.GetAsync(versionId, cancellationToken);
        if (version is null)
        {
            return new BlueprintValidationResult
            {
                IsValid = false,
                Errors = new[] { $"蓝图版本 {versionId} 不存在。" },
            };
        }

        IReadOnlyList<BlueprintNodeRecord> nodes =
            await _nodeRepo.ListAsync(versionId, cancellationToken);

        List<string> errors = new();
        HashSet<string> nodeKeys = new(StringComparer.Ordinal);

        foreach (BlueprintNodeRecord node in nodes)
        {
            // Check duplicate node keys
            if (!nodeKeys.Add(node.NodeKey))
            {
                errors.Add($"节点键 \"{node.NodeKey}\" 重复。");
            }

            // Validate node type
            if (node.NodeType is not ("STATIC_COMPONENT" or "REPEAT_COMPONENT"
                or "CONDITIONAL_COMPONENT" or "GROUP" or "SLOT_REFERENCE"))
            {
                errors.Add($"节点 \"{node.NodeKey}\" 的类型 \"{node.NodeType}\" 无效。");
            }

            // Validate repeat component has source path and item alias
            if (node.NodeType == "REPEAT_COMPONENT")
            {
                if (string.IsNullOrWhiteSpace(node.DataScopePath))
                {
                    errors.Add($"REPEAT_COMPONENT 节点 \"{node.NodeKey}\" 缺少 dataScopePath。");
                }

                if (string.IsNullOrWhiteSpace(node.ItemAlias))
                {
                    errors.Add($"REPEAT_COMPONENT 节点 \"{node.NodeKey}\" 缺少 itemAlias。");
                }

                if (string.IsNullOrWhiteSpace(node.ItemKeyPath))
                {
                    errors.Add($"REPEAT_COMPONENT 节点 \"{node.NodeKey}\" 缺少 itemKeyPath。");
                }
            }
        }

        return new BlueprintValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            NodeCount = nodes.Count,
        };
    }

    /// <summary>
    /// 发布蓝图版本。发布后版本不可修改。
    /// </summary>
    public async Task<BlueprintVersionRecord> PublishAsync(
        ulong versionId,
        ulong? actorUserId,
        CancellationToken cancellationToken = default)
    {
        // Validate first
        BlueprintValidationResult validation = await ValidateAsync(versionId, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"蓝图版本 {versionId} 校验失败，无法发布: {string.Join("; ", validation.Errors)}");
        }

        // Compute dependency hash
        BlueprintVersionRecord? version = await _versionRepo.GetAsync(versionId, cancellationToken);
        IReadOnlyList<BlueprintNodeRecord> nodes =
            await _nodeRepo.ListAsync(versionId, cancellationToken);

        string dependencyHash = ComputeDependencyHash(
            version!.MasterTemplateVersionId, nodes);

        await _versionRepo.PublishAsync(versionId, dependencyHash, actorUserId, cancellationToken);

        return (await _versionRepo.GetAsync(versionId, cancellationToken))!;
    }

    /// <summary>
    /// 计算蓝图版本的依赖哈希。
    /// </summary>
    public static string ComputeDependencyHash(
        ulong masterTemplateVersionId,
        IReadOnlyList<BlueprintNodeRecord> nodes)
    {
        using MemoryStream ms = new();
        using StreamWriter writer = new(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(masterTemplateVersionId);
        writer.Write('|');

        IOrderedEnumerable<BlueprintNodeRecord> sorted =
            nodes.OrderBy(n => n.SortKey).ThenBy(n => n.NodeKey, StringComparer.Ordinal);

        foreach (BlueprintNodeRecord node in sorted)
        {
            writer.Write(node.NodeKey);
            writer.Write('|');
            writer.Write(node.NodeType);
            writer.Write('|');
            writer.Write(node.TemplateVersionId?.ToString() ?? "");
            writer.Write('|');
            writer.Write(node.TargetSlotKey ?? "");
            writer.Write('|');
            writer.Write(node.DataScopePath ?? "");
            writer.Write('|');
            writer.Write(node.ItemAlias ?? "");
            writer.Write('|');
            writer.Write(node.ItemKeyPath ?? "");
            writer.Write('|');
            writer.Write(node.ConditionConfigJson ?? "");
            writer.Write('|');
            writer.Write(node.AssemblyConfigJson ?? "");
            writer.Write('|');
        }

        writer.Flush();
        byte[] hash = SHA256.HashData(ms.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// 表示蓝图版本验证结果。
/// </summary>
public sealed record BlueprintValidationResult
{
    /// <summary>
    /// 获取是否通过验证。
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 获取验证错误列表。
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取节点数量。
    /// </summary>
    public int NodeCount { get; init; }
}
