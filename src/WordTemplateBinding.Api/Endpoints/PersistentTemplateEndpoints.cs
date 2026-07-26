#pragma warning disable CS1591
using WordTemplateBinding.Api.Contracts;
using WordTemplateBinding.Api.Infrastructure;
using WordTemplateBinding.Core.Interfaces;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Core.Services;

namespace WordTemplateBinding.Api.Endpoints;

public static class PersistentTemplateEndpoints
{
    public static IEndpointRouteBuilder MapPersistentTemplateEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder templates = endpoints.MapGroup("/api/templates")
            .WithTags("Templates");
        templates.MapGet("/", ListAsync);
        templates.MapPost("/", CreateAsync);
        templates.MapGet("/{templateId:regex(^[0-9]+$)}", GetAsync);
        templates.MapGet("/{templateId:regex(^[0-9]+$)}/versions", ListVersionsAsync);
        templates.MapPost("/{templateId:regex(^[0-9]+$)}/versions", UploadVersionAsync);
        templates.MapGet("/{templateId:regex(^[0-9]+$)}/current", GetCurrentAsync);

        RouteGroupBuilder versions = endpoints.MapGroup("/api/template-versions")
            .WithTags("Template versions");
        versions.MapGet("/{versionId:regex(^[0-9]+$)}", GetVersionAsync);
        versions.MapGet("/{versionId:regex(^[0-9]+$)}/elements", GetElementsAsync);
        versions.MapGet("/{versionId:regex(^[0-9]+$)}/file", DownloadFileAsync);
        versions.MapPost("/{versionId:regex(^[0-9]+$)}/rescan", RescanAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? name,
        string? code,
        string? type,
        string? status,
        int page,
        int pageSize,
        TemplateCatalogService service,
        CancellationToken cancellationToken)
    {
        PagedResult<TemplateRecord> result = await service.ListAsync(
            new TemplateListQuery
            {
                Name = name,
                Code = code,
                Type = type,
                Status = status,
                Page = page <= 0 ? 1 : page,
                PageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
            },
            cancellationToken);
        return Results.Ok(new
        {
            items = result.Items.Select(PersistentApiMapper.Template),
            result.Total,
            result.Page,
            result.PageSize,
        });
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        TemplateCatalogService service,
        CancellationToken cancellationToken)
    {
        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        IFormFile file = form.Files.GetFile("file")
            ?? throw new BadHttpRequestException("file 不能为空。");
        string fileStem = Path.GetFileNameWithoutExtension(file.FileName);
        string code = form["templateCode"].FirstOrDefault()
            ?? $"TPL_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string name = form["templateName"].FirstOrDefault() ?? fileStem;
        TemplateVersionView result;
        await using (Stream input = file.OpenReadStream())
        {
            result = await service.CreateAsync(
                new TemplateCreateRequest
                {
                    TemplateCode = code,
                    TemplateName = name,
                    TemplateType = form["templateType"].FirstOrDefault() ?? "SECTION",
                    CategoryCode = form["categoryCode"].FirstOrDefault(),
                    Description = form["description"].FirstOrDefault(),
                },
                file.FileName,
                file.Length,
                input,
                actorUserId: null,
                cancellationToken);
        }

        return Results.Created(
            $"/api/template-versions/{result.Version.Id}",
            PersistentApiMapper.VersionView(result));
    }

    private static async Task<IResult> GetAsync(
        string templateId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok(PersistentApiMapper.Template(await service.GetTemplateAsync(
            DatabaseIdParser.Required(templateId, nameof(templateId)),
            cancellationToken)));

    private static async Task<IResult> ListVersionsAsync(
        string templateId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok((await service.ListVersionsAsync(
                DatabaseIdParser.Required(templateId, nameof(templateId)),
                cancellationToken))
            .Select(PersistentApiMapper.Version));

    private static async Task<IResult> UploadVersionAsync(
        string templateId,
        IFormFile file,
        TemplateCatalogService service,
        CancellationToken cancellationToken)
    {
        await using Stream input = file.OpenReadStream();
        TemplateVersionView result = await service.UploadVersionAsync(
            DatabaseIdParser.Required(templateId, nameof(templateId)),
            file.FileName,
            file.Length,
            input,
            actorUserId: null,
            cancellationToken);
        return Results.Created(
            $"/api/template-versions/{result.Version.Id}",
            PersistentApiMapper.VersionView(result));
    }

    private static async Task<IResult> GetCurrentAsync(
        string templateId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok(PersistentApiMapper.VersionView(await service.GetCurrentVersionAsync(
            DatabaseIdParser.Required(templateId, nameof(templateId)),
            cancellationToken)));

    private static async Task<IResult> GetVersionAsync(
        string versionId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok(PersistentApiMapper.VersionView(await service.GetVersionAsync(
            DatabaseIdParser.Required(versionId, nameof(versionId)),
            cancellationToken)));

    private static async Task<IResult> GetElementsAsync(
        string versionId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok((await service.GetVersionAsync(
                DatabaseIdParser.Required(versionId, nameof(versionId)),
                cancellationToken))
            .Elements.Select(PersistentApiMapper.Element));

    private static async Task<IResult> RescanAsync(
        string versionId,
        TemplateCatalogService service,
        CancellationToken cancellationToken) =>
        Results.Ok(PersistentApiMapper.VersionView(await service.RescanAsync(
            DatabaseIdParser.Required(versionId, nameof(versionId)),
            cancellationToken)));

    private static async Task<IResult> DownloadFileAsync(
        string versionId,
        TemplateCatalogService templates,
        IFileStorageService files,
        CancellationToken cancellationToken)
    {
        TemplateVersionView version = await templates.GetVersionAsync(
            DatabaseIdParser.Required(versionId, nameof(versionId)),
            cancellationToken);
        return new DatabaseFileResult(
            files,
            version.File.Id,
            version.File.OriginalName,
            version.File.MimeType ??
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    private sealed class DatabaseFileResult : IResult
    {
        private readonly IFileStorageService _files;
        private readonly ulong _fileId;
        private readonly string _fileName;
        private readonly string _contentType;

        internal DatabaseFileResult(
            IFileStorageService files,
            ulong fileId,
            string fileName,
            string contentType)
        {
            _files = files;
            _fileId = fileId;
            _fileName = fileName;
            _contentType = contentType;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = _contentType;
            httpContext.Response.Headers.ContentDisposition =
                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(_fileName)}";
            await _files.CopyToAsync(
                _fileId,
                httpContext.Response.Body,
                httpContext.RequestAborted);
        }
    }
}

#pragma warning restore CS1591
