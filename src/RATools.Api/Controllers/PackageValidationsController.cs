using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/package-validations")]
public sealed class PackageValidationsController(PackageValidationService service, IPackageValidationStore store) : PackageValidationControllerBase
{
    [HttpGet("profiles")]
    [ProducesResponseType(typeof(ValidationProfileScope[]), StatusCodes.Status200OK)]
    public IActionResult Profiles() => Ok(PackageValidationCatalog.Current.Profiles.Values);

    [HttpPost]
    [ProducesResponseType(typeof(PackageRunSummary), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Create([FromBody] CreatePackageValidationRequest request, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var result = await service.EnqueueAsync(request, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = result.Run.Id }, PackageValidationPaging.Summarize(result.Run));
    });

    [HttpGet]
    [ProducesResponseType(typeof(PackageValidationPage<PackageRunSummary>), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] Guid? applicationId, [FromQuery] int offset = 0, [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) => RespondAsync(async () => Ok(await store.ListRunsAsync(applicationId, offset, limit, cancellationToken)));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PackageRunSummary), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
        await store.GetRunAsync(id, cancellationToken) is { } run ? Ok(PackageValidationPaging.Summarize(run)) : NotFound());

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(PackageRunSummary), StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
        await store.CancelAsync(id, DateTime.UtcNow, cancellationToken) is { } run ? Ok(PackageValidationPaging.Summarize(run)) : NotFound());

    [HttpGet("{id:guid}/report")]
    [ProducesResponseType(typeof(ValidationReport), StatusCodes.Status200OK)]
    public Task<IActionResult> Report(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var run = await store.GetRunAsync(id, cancellationToken);
        if (run is null) return NotFound();
        if (!run.IsTerminal) return Conflict(new { message = "Validation is still running.", run.Stage });
        var report = await store.GetReportJsonAsync(id, cancellationToken);
        return report is null ? StatusCode(410, new { message = "This run did not produce a completed report.", run.FailureCode }) : Content(report, "application/json");
    });

    [HttpGet("{id:guid}/findings")]
    [ProducesResponseType(typeof(PackageValidationPage<ValidationFinding>), StatusCodes.Status200OK)]
    public Task<IActionResult> Findings(Guid id, [FromQuery] PackageFindingQuery query, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var run = await store.GetRunAsync(id, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status != ValidationRunStatus.Completed) return Conflict(new { message = "No completed findings are available for this run.", run.Stage });
        return Ok(await store.GetFindingsAsync(id, query, cancellationToken));
    });

    [HttpGet("{id:guid}/evidence")]
    [ProducesResponseType(typeof(StoredPackageValidationRequest), StatusCodes.Status200OK)]
    public Task<IActionResult> Evidence(Guid id, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var run = await store.GetRunAsync(id, cancellationToken);
        if (run is null) return NotFound();
        run.VerifyRequestIntegrity();
        return Content(run.RequestJson, "application/json");
    });
}
