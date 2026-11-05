using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RATools.Application.DocumentProcessing;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId:guid}/sequences/{sequenceNumber}/pdf-processing")]
public sealed class DocumentProcessingController(DocumentProcessingService service) : ControllerBase
{
    [HttpPost("placements/{placementId:guid}/inspection")]
    [ProducesResponseType(typeof(ProcessingInspection), StatusCodes.Status200OK)]
    public Task<IActionResult> Inspect(Guid applicationId, string sequenceNumber, Guid placementId,
        [FromBody] ProcessingRevisionRequest request, CancellationToken cancellationToken) => RespondAsync(async () =>
            Ok(await service.InspectAsync(applicationId, sequenceNumber, placementId, request.ExpectedRevision, cancellationToken)));

    [HttpPost("placements/{placementId:guid}/plans")]
    [ProducesResponseType(typeof(PdfProcessingPlan), StatusCodes.Status200OK)]
    public Task<IActionResult> Plan(Guid applicationId, string sequenceNumber, Guid placementId,
        [FromBody] CreateProcessingPlanRequest request, CancellationToken cancellationToken) => RespondAsync(async () =>
            Ok(await service.CreatePlanAsync(applicationId, sequenceNumber, placementId, request, cancellationToken)));

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(ProcessingJobView), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Enqueue(Guid applicationId, string sequenceNumber, [FromBody] EnqueueProcessingRequest request,
        CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var result = await service.EnqueueAsync(applicationId, sequenceNumber, request.PlanId, request.PlanDigest, request.IdempotencyKey, cancellationToken);
        var view = await service.GetJobAsync(applicationId, sequenceNumber, result.Job.Id, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { applicationId, sequenceNumber, jobId = result.Job.Id }, view);
    });

    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(ProcessingJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) => RespondAsync(async () =>
        Ok(await service.GetJobAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(typeof(ProcessingJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) => RespondAsync(async () =>
        Ok(await service.CancelAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("jobs/{jobId:guid}/adopt")]
    [ProducesResponseType(typeof(PdfProcessingAdoption), StatusCodes.Status200OK)]
    public Task<IActionResult> Adopt(Guid applicationId, string sequenceNumber, Guid jobId,
        [FromBody] ProcessingRevisionRequest request, CancellationToken cancellationToken) => RespondAsync(async () =>
            Ok(await service.AdoptAsync(applicationId, sequenceNumber, jobId, request.ExpectedRevision, cancellationToken)));

    [HttpGet("versions/{versionId:guid}/content")]
    public Task<IActionResult> Content(Guid applicationId, string sequenceNumber, Guid versionId, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var content = await service.OpenVersionAsync(applicationId, sequenceNumber, versionId, cancellationToken);
        Response.Headers.CacheControl = "private, no-cache";
        return new PhysicalFileResult(content.Path, "application/pdf")
        {
            EnableRangeProcessing = true,
            EntityTag = new EntityTagHeaderValue('"' + content.Sha256 + '"'),
            FileDownloadName = content.FileName
        };
    });

    private async Task<IActionResult> RespondAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (KeyNotFoundException) { return NotFound(new { code = "PROCESSING_NOT_FOUND", message = "The requested PDF processing resource was not found in this workspace." }); }
        catch (PdfProcessingIdempotencyConflictException) { return Conflict(new { code = "PROCESSING_IDEMPOTENCY_CONFLICT", message = "This idempotency key belongs to a different processing request." }); }
        catch (PdfProcessingRejectedException exception) { return Conflict(new { code = "PROCESSING_REJECTED", message = exception.Message }); }
        catch (InvalidDataException) { return Conflict(new { code = "PROCESSING_INPUT_CHANGED", message = "The processing input or stored evidence no longer matches its recorded identity." }); }
        catch (ArgumentException) { return BadRequest(new { code = "PROCESSING_REQUEST_INVALID", message = "PDF processing parameters are invalid; refresh the inspection and review the selected targets." }); }
    }
}

[ApiController]
[Route("api/applications/{applicationId:guid}/sequences/{sequenceNumber}/pdf-renames")]
public sealed class PdfRenameController(PdfRenameService service) : ControllerBase
{
    [HttpPost("plans")]
    [ProducesResponseType(typeof(PdfRenamePreview), StatusCodes.Status200OK)]
    public Task<IActionResult> Plan(Guid applicationId, string sequenceNumber, [FromBody] PdfRenamePreviewRequest request,
        CancellationToken cancellationToken) => RespondAsync(async () => Ok(await service.CreatePreviewAsync(applicationId, sequenceNumber, request, cancellationToken)));

    [HttpGet("plans/{planId:guid}")]
    [ProducesResponseType(typeof(PdfRenamePreview), StatusCodes.Status200OK)]
    public Task<IActionResult> GetPlan(Guid applicationId, string sequenceNumber, Guid planId, CancellationToken cancellationToken) => RespondAsync(async () =>
        Ok(await service.GetPreviewAsync(applicationId, sequenceNumber, planId, cancellationToken)));

    [HttpGet("plans/{planId:guid}/job")]
    [ProducesResponseType(typeof(PdfRenameJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> GetPlanJob(Guid applicationId, string sequenceNumber, Guid planId, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var job = await service.GetPlanJobAsync(applicationId, sequenceNumber, planId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    });

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(PdfRenameJobView), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Enqueue(Guid applicationId, string sequenceNumber, [FromBody] EnqueuePdfRenameRequest request,
        CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var result = await service.EnqueueAsync(applicationId, sequenceNumber, request.PlanId, request.PlanDigest,
            request.IdempotencyKey, cancellationToken);
        var view = await service.GetJobAsync(applicationId, sequenceNumber, result.Job.State.Id, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { applicationId, sequenceNumber, jobId = result.Job.State.Id }, view);
    });

    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(PdfRenameJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> GetJob(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) => RespondAsync(async () =>
        Ok(await service.GetJobAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(typeof(PdfRenameJobView), StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid applicationId, string sequenceNumber, Guid jobId, CancellationToken cancellationToken) => RespondAsync(async () =>
        Ok(await service.CancelAsync(applicationId, sequenceNumber, jobId, cancellationToken)));

    [HttpPost("jobs/{jobId:guid}/adopt")]
    [ProducesResponseType(typeof(PdfRenameAdoption), StatusCodes.Status200OK)]
    public Task<IActionResult> Adopt(Guid applicationId, string sequenceNumber, Guid jobId, [FromBody] ProcessingRevisionRequest request,
        CancellationToken cancellationToken) => RespondAsync(async () => Ok(await service.AdoptAsync(applicationId, sequenceNumber, jobId, request.ExpectedRevision, cancellationToken)));

    [HttpGet("jobs/{jobId:guid}/placements/{placementId:guid}/content")]
    public Task<IActionResult> Content(Guid applicationId, string sequenceNumber, Guid jobId, Guid placementId, CancellationToken cancellationToken) => RespondAsync(async () =>
    {
        var content = await service.OpenContentAsync(applicationId, sequenceNumber, jobId, placementId, cancellationToken);
        Response.Headers.CacheControl = "private, no-cache";
        return new PhysicalFileResult(content.Path, "application/pdf") { EnableRangeProcessing = true,
            EntityTag = new EntityTagHeaderValue('"' + content.Sha256 + '"'), FileDownloadName = content.FileName };
    });

    private async Task<IActionResult> RespondAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (KeyNotFoundException) { return NotFound(new { code = "PDF_RENAME_NOT_FOUND", message = "The requested PDF rename resource was not found." }); }
        catch (PdfProcessingIdempotencyConflictException) { return Conflict(new { code = "PDF_RENAME_IDEMPOTENCY_CONFLICT", message = "The idempotency key belongs to another rename request." }); }
        catch (PdfProcessingRejectedException exception) { return Conflict(new { code = "PDF_RENAME_REJECTED", message = exception.Message }); }
        catch (InvalidDataException) { return Conflict(new { code = "PDF_RENAME_INPUT_CHANGED", message = "The rename input or evidence changed." }); }
        catch (ArgumentException) { return BadRequest(new { code = "PDF_RENAME_REQUEST_INVALID", message = "The PDF rename request is invalid." }); }
    }
}

public sealed record EnqueuePdfRenameRequest(Guid PlanId, string PlanDigest, string IdempotencyKey);

public sealed record ProcessingRevisionRequest(long ExpectedRevision);
public sealed record EnqueueProcessingRequest(Guid PlanId, string PlanDigest, string IdempotencyKey);
