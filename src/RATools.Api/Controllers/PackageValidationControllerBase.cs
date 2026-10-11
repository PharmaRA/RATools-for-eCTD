using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;

namespace RATools.Api.Controllers;

public abstract class PackageValidationControllerBase : ControllerBase
{
    protected async Task<IActionResult> RespondAsync(Func<Task<IActionResult>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (PackageSelectionRequiredException exception) { return Conflict(new { code = exception.Code, message = exception.Message, candidates = exception.Candidates }); }
        catch (PackageValidationIdempotencyConflictException exception) { return Conflict(new { code = "VALIDATION_IDEMPOTENCY_CONFLICT", message = exception.Message }); }
        catch (PackageInputChangedException exception) { return Conflict(new { code = exception.Code, message = exception.Message }); }
        catch (PackageInputException exception)
        { return StatusCode(exception.RuleId == "INPUT-LIMITS" ? 413 : 422, new { code = exception.Code, message = exception.Message, logicalPath = exception.LogicalPath }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { message = "Access to the selected input is denied." }); }
        catch (InvalidDataException exception) { return UnprocessableEntity(new { message = exception.Message }); }
        catch (IOException) { return UnprocessableEntity(new { message = "The selected input or archived evidence is unavailable." }); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
