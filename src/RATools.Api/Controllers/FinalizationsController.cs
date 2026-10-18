using Microsoft.AspNetCore.Mvc;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Domain.Finalization;

namespace RATools.Api.Controllers;

// Finalization is an explicit operator action that records the validated identity of a
// sequence. It never derives "submitted" from a publish job, and a recorded snapshot is
// immutable: corrections are new delivery/receipt rows.
[ApiController]
[Route("api")]
public sealed class FinalizationsController(FinalizationService service, IFinalizationStore store) : ControllerBase
{
    [HttpPost("applications/{applicationId:guid}/sequences/{sequenceNumber}/finalizations")]
    [ProducesResponseType(typeof(FinalizationResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> Finalize(Guid applicationId, string sequenceNumber,
        [FromBody] FinalizeSequenceBody body, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.FinalizeAsync(new FinalizeSequenceRequest(applicationId, sequenceNumber,
                body.ExpectedRevision, body.PackageValidationRunId, body.ManifestDigest, body.StandardsProfileSnapshotId,
                body.IchEctdVersion, body.RegionalModule1Version, body.HistoryBaselineDigest, body.IdempotencyKey),
                cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(Get), new { id = result.Sequence.Id }, result)
                : Ok(result);
        }
        catch (WorkspaceRevisionConflictException exception)
        {
            return Conflict(new { code = "WorkspaceRevisionConflict", message = exception.Message,
                expectedRevision = exception.ExpectedRevision, currentRevision = exception.CurrentRevision });
        }
        catch (FinalizationIdempotencyConflictException exception)
        {
            return Conflict(new { code = "FINALIZATION_IDEMPOTENCY_CONFLICT", message = exception.Message });
        }
        catch (FinalizationSnapshotExistsException exception)
        {
            return Conflict(new { code = "FINALIZATION_SNAPSHOT_EXISTS", message = exception.Message });
        }
        catch (FinalizationNotAuthorizedException exception)
        {
            return Conflict(new { code = "FINALIZATION_NOT_AUTHORIZED", message = exception.Message });
        }
        catch (FinalizationNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("finalizations/{id:guid}")]
    [ProducesResponseType(typeof(FinalizedSequence), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken) is { } sequence ? Ok(sequence) : NotFound();

    [HttpGet("applications/{applicationId:guid}/sequences/{sequenceNumber}/finalized-versions")]
    [ProducesResponseType(typeof(IReadOnlyList<FinalizedSequence>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken) =>
        Ok(await store.ListBySequenceAsync(applicationId, sequenceNumber.Trim(), cancellationToken));

    [HttpGet("finalized-sequences/{id:guid}/deliveries")]
    [ProducesResponseType(typeof(IReadOnlyList<DeliveryRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deliveries(Guid id, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(id, cancellationToken) is null)
            return NotFound(new { message = $"Finalized sequence {id} was not found." });
        return Ok(await store.ListDeliveriesAsync(id, cancellationToken));
    }

    [HttpPost("finalized-sequences/{id:guid}/deliveries")]
    [ProducesResponseType(typeof(DeliveryRecord), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterDelivery(Guid id, [FromBody] RegisterDeliveryBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await service.RegisterDeliveryAsync(id, body.PackageSha256, body.Channel,
                body.ExternalReference, body.Note, body.RecordedBy, cancellationToken);
            return CreatedAtAction(nameof(Deliveries), new { id }, record);
        }
        catch (FinalizationNotAuthorizedException exception)
        {
            return Conflict(new { code = "DELIVERY_NOT_AUTHORIZED", message = exception.Message });
        }
        catch (FinalizationNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("finalized-deliveries/{deliveryId:guid}/receipts")]
    [ProducesResponseType(typeof(IReadOnlyList<DeliveryReceipt>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipts(Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!await store.DeliveryExistsAsync(deliveryId, cancellationToken))
            return NotFound(new { message = $"Delivery record {deliveryId} was not found." });
        return Ok(await store.ListReceiptsAsync(deliveryId, cancellationToken));
    }

    [HttpPost("finalized-deliveries/{deliveryId:guid}/receipts")]
    [ProducesResponseType(typeof(DeliveryReceipt), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterReceipt(Guid deliveryId, [FromBody] RegisterReceiptBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await service.RegisterReceiptAsync(deliveryId, body.Kind, body.Outcome, body.RawText,
                body.ExternalReference, body.RecordedBy, cancellationToken);
            return CreatedAtAction(nameof(Receipts), new { deliveryId }, receipt);
        }
        catch (FinalizationNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}

public sealed record FinalizeSequenceBody(long ExpectedRevision, Guid PackageValidationRunId, string ManifestDigest,
    string StandardsProfileSnapshotId, string IchEctdVersion, string? RegionalModule1Version,
    string HistoryBaselineDigest, string IdempotencyKey);

public sealed record RegisterDeliveryBody(string PackageSha256, string Channel, string? ExternalReference,
    string? Note, string RecordedBy);

public sealed record RegisterReceiptBody(DeliveryReceiptKind Kind, string Outcome, string RawText,
    string? ExternalReference, string RecordedBy);
