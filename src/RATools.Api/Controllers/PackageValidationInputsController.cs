using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;

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
    public Task<IActionResult> Manifest(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
        await store.GetInputAsync(id, cancellationToken) is { } input ? Content(input.ManifestJson, "application/json") : NotFound());
}

public sealed class PackageInputUploadForm
{
    public Guid ApplicationId { get; set; }
    public IFormFile? File { get; set; }
    public string? SequenceNumber { get; set; }
    public string? RootRelativePath { get; set; }
}
