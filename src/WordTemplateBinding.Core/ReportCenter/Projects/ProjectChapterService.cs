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
public sealed class ProjectChapterService
{
    private readonly IProjectRepository _projects;
    private readonly IChapterRepository _chapters;
    private readonly ICurrentUserContext _user;
    private readonly IAuditLogWriter? _audit;

    public ProjectChapterService(
        IProjectRepository projects,
        IChapterRepository chapters,
        ICurrentUserContext user,
        IAuditLogWriter? audit = null)
    {
        _projects = projects;
        _chapters = chapters;
        _user = user;
        _audit = audit;
    }

    // ── Projects ──

    public async Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(
        CancellationToken cancellationToken)
    {
        PagedResult<ProjectRecord> result = await _projects.ListAsync(
            null, null, 1, int.MaxValue, cancellationToken);
        return result.Items;
    }

    public Task<PagedResult<ProjectRecord>> QueryProjectsAsync(
        string? query,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        _projects.ListAsync(query, status, page, pageSize, cancellationToken);

    public async Task<ProjectRecord> CreateProjectAsync(
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        ValidateCode(code, "项目编码");
        ValidateName(name, "项目名称");
        ProjectRecord project = await _projects.CreateAsync(
            code.Trim(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            _user.UserId,
            cancellationToken);
        await TryAuditAsync("CREATE_PROJECT", "rp_project", project.Id, null, project, cancellationToken);
        return project;
    }

    public async Task<ProjectRecord> GetProjectAsync(
        ulong projectId,
        CancellationToken cancellationToken) =>
        await _projects.GetAsync(projectId, cancellationToken)
        ?? throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");

    public async Task<ProjectRecord> UpdateProjectAsync(
        ulong projectId,
        string name,
        string? description,
        string? status,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ProjectRecord before = await GetProjectAsync(projectId, cancellationToken);
        ValidateName(name, "项目名称");
        if (status is not null)
        {
            ValidateProjectStatus(status);
        }

        if (!await _projects.UpdateAsync(projectId, name.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                status, expectedRowVersion, cancellationToken))
        {
            throw new WorkspaceException(
                "project_concurrency_conflict",
                "项目已被其他操作修改，请刷新后重试。");
        }

        ProjectRecord after = await GetProjectAsync(projectId, cancellationToken);
        await TryAuditAsync("UPDATE_PROJECT", "rp_project", projectId, before, after, cancellationToken);
        return after;
    }

    public async Task<ProjectRecord> ArchiveProjectAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ProjectRecord before = await GetProjectAsync(projectId, cancellationToken);
        if (!await _projects.ArchiveAsync(projectId, expectedRowVersion, cancellationToken))
        {
            throw new WorkspaceException(
                "project_concurrency_conflict",
                "项目已被其他操作修改，请刷新后重试。");
        }

        ProjectRecord after = await _projects.GetAsync(projectId, cancellationToken)
            ?? before;
        await TryAuditAsync("ARCHIVE_PROJECT", "rp_project", projectId, before, after, cancellationToken);
        return after;
    }

    public async Task<ProjectRecord> RestoreProjectAsync(
        ulong projectId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ProjectRecord before = await GetProjectAsync(projectId, cancellationToken);
        if (!await _projects.RestoreAsync(projectId, expectedRowVersion, cancellationToken))
        {
            throw new WorkspaceException(
                "project_concurrency_conflict",
                "项目已被其他操作修改，请刷新后重试。");
        }

        ProjectRecord after = await _projects.GetAsync(projectId, cancellationToken)
            ?? before;
        await TryAuditAsync("RESTORE_PROJECT", "rp_project", projectId, before, after, cancellationToken);
        return after;
    }

    // ── Chapters ──

    public async Task<IReadOnlyList<ChapterRecord>> ListChaptersAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        return await _chapters.ListAsync(projectId, cancellationToken);
    }

    public async Task<ChapterRecord> CreateChapterAsync(
        ulong projectId,
        string code,
        string title,
        ulong? parentId,
        decimal sortKey,
        CancellationToken cancellationToken)
    {
        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        ValidateCode(code, "章节编码");
        ValidateName(title, "章节标题");

        if (parentId.HasValue)
        {
            ChapterRecord parent = await _chapters.GetAsync(parentId.Value, cancellationToken)
                ?? throw new WorkspaceException("chapter_parent_invalid", "找不到父章节。");
            if (parent.ProjectId != projectId)
            {
                throw new WorkspaceException(
                    "chapter_parent_invalid",
                    "父章节不属于当前项目。");
            }
        }

        ChapterRecord chapter = await _chapters.CreateAsync(
            projectId,
            code.Trim(),
            title.Trim(),
            parentId,
            sortKey,
            _user.UserId,
            cancellationToken);
        await TryAuditAsync("CREATE_CHAPTER", "rp_chapter", chapter.Id, null, chapter, cancellationToken);
        return chapter;
    }

    public async Task<ChapterRecord> GetChapterAsync(
        ulong chapterId,
        CancellationToken cancellationToken) =>
        await _chapters.GetAsync(chapterId, cancellationToken)
        ?? throw new WorkspaceException("chapter_not_found", $"找不到章节：{chapterId}。");

    public async Task<ChapterRecord> UpdateChapterAsync(
        ulong chapterId,
        string code,
        string title,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ChapterRecord before = await GetChapterAsync(chapterId, cancellationToken);
        ValidateCode(code, "章节编码");
        ValidateName(title, "章节标题");

        if (!await _chapters.UpdateAsync(chapterId, code.Trim(), title.Trim(),
                expectedRowVersion, cancellationToken))
        {
            throw new WorkspaceException(
                "chapter_concurrency_conflict",
                "章节已被其他操作修改，请刷新后重试。");
        }

        ChapterRecord after = await GetChapterAsync(chapterId, cancellationToken);
        await TryAuditAsync("UPDATE_CHAPTER", "rp_chapter", chapterId, before, after, cancellationToken);
        return after;
    }

    public async Task DeleteChapterAsync(
        ulong chapterId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ChapterRecord before = await GetChapterAsync(chapterId, cancellationToken);

        if (await _chapters.HasChildrenAsync(chapterId, cancellationToken))
        {
            throw new WorkspaceException(
                "chapter_has_children",
                "该章节包含子章节，请先删除子章节。");
        }

        if (!await _chapters.DeleteAsync(chapterId, expectedRowVersion, cancellationToken))
        {
            throw new WorkspaceException(
                "chapter_concurrency_conflict",
                "章节已被其他操作修改，请刷新后重试。");
        }

        await TryAuditAsync("DELETE_CHAPTER", "rp_chapter", chapterId, before, null, cancellationToken);
    }

    public async Task ReorderChaptersAsync(
        ulong projectId,
        IReadOnlyList<(ulong ChapterId, ulong? ParentId, decimal SortKey)> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (await _projects.GetAsync(projectId, cancellationToken) is null)
        {
            throw new WorkspaceException("project_not_found", $"找不到项目：{projectId}。");
        }

        // Validate all chapter IDs belong to the project
        foreach (var (chapterId, parentId, _) in items)
        {
            ChapterRecord chapter = await _chapters.GetAsync(chapterId, cancellationToken)
                ?? throw new WorkspaceException("chapter_not_found", $"找不到章节：{chapterId}。");
            if (chapter.ProjectId != projectId)
            {
                throw new WorkspaceException(
                    "chapter_not_found",
                    $"章节 {chapterId} 不属于当前项目。");
            }

            if (parentId.HasValue)
            {
                ChapterRecord parent = await _chapters.GetAsync(parentId.Value, cancellationToken)
                    ?? throw new WorkspaceException("chapter_parent_invalid", "找不到父章节。");
                if (parent.ProjectId != projectId)
                {
                    throw new WorkspaceException(
                        "chapter_parent_invalid",
                        "父章节不属于当前项目。");
                }
            }
        }

        if (!await _chapters.ReorderAsync(projectId, items, cancellationToken))
        {
            throw new WorkspaceException(
                "chapter_reorder_failed",
                "章节排序失败。");
        }

        await TryAuditAsync("REORDER_CHAPTER", "rp_chapter", projectId, null,
            ToJson(new { itemCount = items.Count }), cancellationToken);
    }

    public Task<int> CountChaptersAsync(
        ulong projectId,
        CancellationToken cancellationToken) =>
        _chapters.CountAsync(projectId, cancellationToken);

    // ── Helpers ──

    private static void ValidateProjectStatus(string status)
    {
        string[] valid = { "DRAFT", "CONFIGURING", "READY", "GENERATING", "COMPLETED", "ARCHIVED" };
        if (!valid.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new WorkspaceException(
                "invalid_project_status",
                $"无效的项目状态：{status}。允许：{string.Join(", ", valid)}。");
        }
    }

    private static void ValidateCode(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            !value.All(character =>
                char.IsLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new WorkspaceException(
                "invalid_workspace_request",
                $"{label}只能包含字母、数字、下划线和连字符，且长度不能超过 64。");
        }
    }

    private static void ValidateName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
        {
            throw new WorkspaceException(
                "invalid_workspace_request",
                $"{label}不能为空且长度不能超过 255。");
        }
    }

    private async Task TryAuditAsync(
        string action,
        string targetType,
        ulong targetId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        try
        {
            await _audit.WriteAsync(
                action,
                targetType,
                targetId,
                _user.UserId,
                ToJson(before),
                ToJson(after),
                null,
                cancellationToken);
        }
        catch
        {
            // Audit failure must not break business operations.
        }
    }

    private static string? ToJson(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

#pragma warning restore CS1591

