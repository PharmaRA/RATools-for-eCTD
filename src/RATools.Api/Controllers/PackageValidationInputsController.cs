using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using Microsoft.Net.Http.Headers;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/package-validation-inputs")]
public sealed class PackageValidationInputsController(PackageValidationInputRegistry registry, IPackageValidationStore store)
    : PackageValidationControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PackageInputDto), StatusCodes.Status201Created)]
    public Task<IActionResult> Register([FromBody] RegisterPackageInputRequest request, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var input = await registry.RegisterAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = input.Id }, input);
    });

    [HttpPost("upload")]
    [TypeFilter(typeof(RATools.Api.Filters.RetainedPackageUploadFilter))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PackageInputDto), StatusCodes.Status201Created)]
    public Task<IActionResult> Upload([FromForm] PackageInputUploadForm request, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        if (request.File is null || request.File.Length == 0) return BadRequest(new { message = "Select a nonempty ZIP file." });
        await using var stream = request.File.OpenReadStream();
        var input = await registry.UploadAsync(request.ApplicationId, stream, request.SequenceNumber, request.RootRelativePath, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = input.Id }, input);
    });

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PackageInputDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
        await registry.GetAsync(id, cancellationToken) is { } input ? Ok(input) : NotFound());

    [HttpGet("{id:guid}/manifest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Manifest(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
        await store.GetInputAsync(id, cancellationToken) is { } input ? Content(input.ManifestJson, "application/json") : NotFound());

    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(Guid id, [FromQuery] string logicalPath, [FromQuery] bool surrounding = false,
        CancellationToken cancellationToken = default) => RespondAsync(() =>
        registry.ReadFileAsync<IActionResult>(id, logicalPath, async (file, stream) =>
        {
            Response.ContentType = "application/octet-stream";
            Response.ContentLength = file.Length;
            Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = Path.GetFileName(file.LogicalPath) }.ToString();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers.CacheControl = "no-store";
            await stream.CopyToAsync(Response.Body, cancellationToken);
            return new EmptyResult();
        }, surrounding, cancellationToken));

    [HttpGet("{id:guid}/text")]
    [ProducesResponseType(typeof(PackageTextPreview), StatusCodes.Status200OK)]
    public Task<IActionResult> Text(Guid id, [FromQuery] string logicalPath, [FromQuery] int line = 1,
        [FromQuery] bool surrounding = false, CancellationToken cancellationToken = default) => RespondAsync(() =>
        registry.ReadFileAsync<IActionResult>(id, logicalPath, async (file, stream) =>
        {
            if (line < 1) throw new ArgumentException("Use a positive line number.");
            if (Path.GetExtension(logicalPath).ToLowerInvariant() is not (".xml" or ".xsl" or ".txt" or ".dtd" or ".mod"))
                throw new ArgumentException("This file is not a supported text preview; download the original bytes instead.");
            // Show escaped text, never parse/transform/execute delivered XML or stylesheets.
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[262145];
            var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            var truncated = count > 262144;
            var lines = new string(buffer, 0, Math.Min(count, 262144)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var start = Math.Max(0, Math.Min(line - 1, lines.Length - 1) - 10);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Ok(new PackageTextPreview(file.LogicalPath, file.Sha256, start + 1,
                lines.Skip(start).Take(40).ToArray(), truncated || start + 40 < lines.Length));
        }, surrounding, cancellationToken));
}

public sealed record PackageTextPreview(string LogicalPath, string Sha256, int StartLine, IReadOnlyList<string> Lines, bool Truncated);

public sealed class PackageInputUploadForm
{
    public Guid ApplicationId { get; set; }
    public IFormFile? File { get; set; }
    public string? SequenceNumber { get; set; }
    public string? RootRelativePath { get; set; }
}
