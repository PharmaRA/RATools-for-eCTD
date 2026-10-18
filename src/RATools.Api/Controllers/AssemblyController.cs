using Microsoft.AspNetCore.Mvc;
using RATools.Application.Assembly;
using RATools.Application.Documents;

namespace RATools.Api.Controllers;

// Bulk assembly is preview-first: the manifest is planned against an explicit workspace
// revision, invalid rows are reported instead of skipped, and execution requires the
// exact plan digest so a stale preview can never be applied.
[ApiController]
[Route("api")]
public sealed class AssemblyController(AssemblyPlanner planner, AssemblyExecutor executor) : ControllerBase
{
    [HttpPost("applications/{applicationId:guid}/sequences/{sequenceNumber}/assembly-plans")]
    [ProducesResponseType(typeof(AssemblyPlan), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plan(Guid applicationId, string sequenceNumber,
        [FromBody] AssemblyPlanBody body, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await planner.PlanAsync(applicationId, sequenceNumber, body.ExpectedRevision, body.Manifest,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("assembly-plans/execute")]
    [ProducesResponseType(typeof(AssemblyExecutionResult), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Execute([FromBody] AssemblyExecuteBody body, CancellationToken cancellationToken)
    {
        try
        {
            var result = await executor.ExecuteAsync(body.ApplicationId, body.SequenceNumber, body.ExpectedRevision,
                body.PlanDigest, body.Manifest, cancellationToken);
            return Accepted(result);
        }
        catch (AssemblyPlanStaleException exception)
        {
            return Conflict(new { code = "ASSEMBLY_PLAN_STALE", message = exception.Message });
        }
        catch (AssemblyPlanInvalidException exception)
        {
            return Conflict(new { code = "ASSEMBLY_PLAN_INVALID", message = exception.Message });
        }
        catch (AssemblyApplicationNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (DocumentStorageBoundaryException exception)
        {
            return Conflict(new { code = "ASSEMBLY_DOCUMENT_OUT_OF_SCOPE", message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}

public sealed record AssemblyPlanBody(long ExpectedRevision, string Manifest);

public sealed record AssemblyExecuteBody(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    string PlanDigest, string Manifest);
