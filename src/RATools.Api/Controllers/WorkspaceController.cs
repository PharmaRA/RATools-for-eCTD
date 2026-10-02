using Microsoft.AspNetCore.Mvc;
using RATools.Application.Workspaces;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId:guid}/sequences/{sequenceNumber}/workspace")]
public sealed class WorkspaceController(WorkspaceSnapshotService snapshots) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(WorkspaceSnapshotDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.GetAsync(applicationId, sequenceNumber, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }
}
