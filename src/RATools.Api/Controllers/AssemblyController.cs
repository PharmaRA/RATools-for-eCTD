using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RATools.Application.Assembly;
using RATools.Application.Documents;

namespace RATools.Api.Controllers;

// Bulk assembly is preview-first: the manifest is planned against an explicit workspace
// revision, invalid rows are reported instead of skipped, and execution requires the
// exact plan digest so a stale preview can never be applied.
[ApiController]
[Route("api/applications/{applicationId:guid}/sequences/{sequenceNumber}")]
public sealed class AssemblyController(AssemblyService service) : ControllerBase
{
    [HttpPost("assembly-sources")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(537919488)]
    [RequestFormLimits(MultipartBodyLengthLimit = 537919488)]
    [ProducesResponseType(typeof(AssemblySourceUpload), StatusCodes.Status200OK)]
    public Task<IActionResult> Source(Guid applicationId, string sequenceNumber, [FromForm, BindRequired] long expectedRevision,
        IFormFile file, CancellationToken cancellationToken) => Respond(async () =>
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose a nonempty source file." });
        await using var stream = file.OpenReadStream();
        return Ok(await service.UploadSourceAsync(applicationId, sequenceNumber, expectedRevision, file.FileName, stream, cancellationToken));
    });

    [HttpGet("assembly-sources")]
    [ProducesResponseType(typeof(AssemblySourcePage), StatusCodes.Status200OK)]
    public Task<IActionResult> Sources(Guid applicationId, string sequenceNumber, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Respond(async () => Ok(await service.SourcePageAsync(applicationId, sequenceNumber, offset, limit, cancellationToken)));

    [HttpPost("assembly-plans")]
    [ProducesResponseType(typeof(AssemblyPreview), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plan(Guid applicationId, string sequenceNumber,
        [FromBody] AssemblyPreviewRequest body, CancellationToken cancellationToken)
    {
        return await Respond(async () => Ok(await service.PreviewAsync(applicationId, sequenceNumber, body, cancellationToken)));
    }

    [HttpGet("assembly-plans/{planId:guid}")]
    [ProducesResponseType(typeof(AssemblyPreview), StatusCodes.Status200OK)]
    public Task<IActionResult> GetPlan(Guid applicationId, string sequenceNumber, Guid planId, CancellationToken cancellationToken) =>
        Respond(async () => Ok(await service.PlanAsync(applicationId, sequenceNumber, planId, cancellationToken)));

    [HttpGet("assembly-plans/{planId:guid}/rows")]
    [ProducesResponseType(typeof(AssemblyPreviewPage), StatusCodes.Status200OK)]
    public Task<IActionResult> Rows(Guid applicationId, string sequenceNumber, Guid planId, [FromQuery] int offset = 0, [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Respond(async () => Ok(await service.PlanRowsAsync(applicationId, sequenceNumber, planId, offset, limit, cancellationToken)));

    [HttpPost("assembly-plans/{planId:guid}/selection")]
    [ProducesResponseType(typeof(AssemblyPreview), StatusCodes.Status200OK)]
    public Task<IActionResult> Select(Guid applicationId, string sequenceNumber, Guid planId, [FromBody] AssemblySelectionRequest body, CancellationToken cancellationToken) =>
        Respond(async () => Ok(await service.SelectAsync(applicationId, sequenceNumber, planId, body, cancellationToken)));

    [HttpGet("assembly-plans/{planId:guid}/execution")]
    [ProducesResponseType(typeof(AssemblyExecutionJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> PlanJob(Guid applicationId, string sequenceNumber, Guid planId, CancellationToken cancellationToken) => Respond(async () =>
        await service.PlanJobAsync(applicationId, sequenceNumber, planId, cancellationToken) is { } job ? Ok(job) : NotFound());

    [HttpPost("assembly-executions")]
    [ProducesResponseType(typeof(AssemblyExecutionJobView), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Enqueue(Guid applicationId, string sequenceNumber, [FromBody] AssemblyEnqueueRequest body, CancellationToken cancellationToken) => Respond(async () =>
    {
        var result = await service.EnqueueAsync(applicationId, sequenceNumber, body, cancellationToken);
        return Accepted(await service.JobAsync(applicationId, sequenceNumber, result.Job.State.Id, cancellationToken));
    });

    [HttpGet("assembly-executions/{jobId:guid}")]
    [ProducesResponseType(typeof(AssemblyExecutionJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> Job(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) =>
        Respond(async () => Ok(await service.JobAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("assembly-executions/{jobId:guid}/cancel")]
    [ProducesResponseType(typeof(AssemblyExecutionJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) =>
        Respond(async () => Ok(await service.CancelAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("/api/assembly-plans/execute")]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult Execute() => StatusCode(StatusCodes.Status410Gone, new { code = "ASSEMBLY_DURABLE_EXECUTION_REQUIRED",
        message = "Create a stored assembly preview, confirm its rows and enqueue its plan identity/digest with an idempotency key." });

    private static async Task<IActionResult> Respond(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AssemblyPlanStaleException exception)
        {
            return new ConflictObjectResult(new { code = "ASSEMBLY_PLAN_STALE", message = exception.Message });
        }
        catch (AssemblyPlanInvalidException exception)
        {
            return new ConflictObjectResult(new { code = "ASSEMBLY_PLAN_INVALID", message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { message = exception.Message });
        }
        catch (DocumentStorageBoundaryException exception)
        {
            return new ConflictObjectResult(new { code = "ASSEMBLY_DOCUMENT_OUT_OF_SCOPE", message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(new { message = exception.Message });
        }
        catch (InvalidDataException) { return new ConflictObjectResult(new { code = "ASSEMBLY_EVIDENCE_CHANGED", message = "Source or evidence verification failed; regenerate the preview." }); }
    }
}

public sealed record AssemblyPlanBody(long ExpectedRevision, string Manifest);

public sealed record AssemblyExecuteBody(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    string PlanDigest, string Manifest);
