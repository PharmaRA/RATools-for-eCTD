using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Api.Controllers;

public abstract class PackageValidationControllerBase : ControllerBase
{
    protected async Task<IActionResult> RespondAsync(Func<Task<IActionResult>> action)
    {
        try { return await action().ConfigureAwait(false); }
        // Streaming endpoints (file download) may fail after headers are sent. Re-throw
        // then instead of appending a JSON error body to a partial binary response.
        catch (PackageSelectionRequiredException exception) when (PrepareErrorResponse())
        { return Conflict(new { code = exception.Code, message = exception.Message, candidates = exception.Candidates }); }
        catch (PackageValidationIdempotencyConflictException exception) when (PrepareErrorResponse())
        { return Conflict(new { code = "VALIDATION_IDEMPOTENCY_CONFLICT", message = exception.Message }); }
        catch (ExternalComparisonEvidenceConflictException exception) when (PrepareErrorResponse())
        { return Conflict(new { code = "EXTERNAL_EVIDENCE_CONFLICT", message = exception.Message }); }
        catch (StandardsSelectionException exception) when (PrepareErrorResponse())
        { return BadRequest(new { code = "STANDARDS_SELECTION_BLOCKED", message = exception.Message, reasons = exception.Reasons }); }
        catch (PackageInputChangedException exception) when (PrepareErrorResponse())
        { return Conflict(new { code = exception.Code, message = exception.Message }); }
        catch (PackageInputException exception) when (PrepareErrorResponse())
        { return StatusCode(LimitStatus(exception), new { code = exception.Code, message = exception.Message, logicalPath = exception.LogicalPath }); }
        catch (KeyNotFoundException exception) when (PrepareErrorResponse()) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) when (PrepareErrorResponse()) { return BadRequest(new { message = exception.Message }); }
        catch (UnauthorizedAccessException) when (PrepareErrorResponse()) { return StatusCode(403, new { message = "Access to the selected input is denied." }); }
        catch (InvalidDataException exception) when (PrepareErrorResponse()) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (IOException) when (PrepareErrorResponse()) { return UnprocessableEntity(new { message = "The selected input or archived evidence is unavailable." }); }
        catch (InvalidOperationException exception) when (PrepareErrorResponse()) { return BadRequest(new { message = exception.Message }); }
    }

    private bool PrepareErrorResponse()
    {
        if (Response.HasStarted) return false;
        // A failed first read may leave binary headers even though no bytes were sent.
        Response.ContentType = null;
        Response.ContentLength = null;
        Response.Headers.Remove("Content-Disposition");
        return true;
    }

    // Size/count limits are payload-too-large; elapsed deadlines are timeouts, not
    // size failures. ZIP metadata limits are bounded payload sizes as well.
    private static int LimitStatus(PackageInputException exception) => exception.Code switch
    {
        "INPUT_READ_TIMEOUT" or "DELIVERY_INSPECTION_TIMEOUT" => StatusCodes.Status408RequestTimeout,
        _ when exception.RuleId is "INPUT-LIMITS" or "ZIP-LIMITS" => StatusCodes.Status413PayloadTooLarge,
        _ => StatusCodes.Status422UnprocessableEntity
    };
}
