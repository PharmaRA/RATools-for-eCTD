using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;

namespace RATools.Api.Controllers;

public abstract class PackageValidationControllerBase : ControllerBase
{
    protected async Task<IActionResult> RespondAsync(Func<Task<IActionResult>> action)
    {
        try { return await action().ConfigureAwait(false); }
        // Streaming endpoints (file download) may fail after headers are sent. Re-throw
        // then instead of appending a JSON error body to a partial binary response.
        catch (PackageSelectionRequiredException exception) when (!Response.HasStarted)
        { return Conflict(new { code = exception.Code, message = exception.Message, candidates = exception.Candidates }); }
        catch (PackageValidationIdempotencyConflictException exception) when (!Response.HasStarted)
        { return Conflict(new { code = "VALIDATION_IDEMPOTENCY_CONFLICT", message = exception.Message }); }
        catch (PackageInputChangedException exception) when (!Response.HasStarted)
        { return Conflict(new { code = exception.Code, message = exception.Message }); }
        catch (PackageInputException exception) when (!Response.HasStarted)
        { return StatusCode(LimitStatus(exception), new { code = exception.Code, message = exception.Message, logicalPath = exception.LogicalPath }); }
        catch (KeyNotFoundException exception) when (!Response.HasStarted) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) when (!Response.HasStarted) { return BadRequest(new { message = exception.Message }); }
        catch (UnauthorizedAccessException) when (!Response.HasStarted) { return StatusCode(403, new { message = "Access to the selected input is denied." }); }
        catch (InvalidDataException exception) when (!Response.HasStarted) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (IOException) when (!Response.HasStarted) { return UnprocessableEntity(new { message = "The selected input or archived evidence is unavailable." }); }
        catch (InvalidOperationException exception) when (!Response.HasStarted) { return BadRequest(new { message = exception.Message }); }
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
