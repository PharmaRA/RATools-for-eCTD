using Microsoft.AspNetCore.Mvc;
using RATools.Application.Ctd;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId:guid}/sequences/{sequenceNumber}/nodes")]
public sealed class CtdNodesController(CtdNodeService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid applicationId, string sequenceNumber, CancellationToken ct)
    {
        var tree = await service.GetAsync(applicationId, sequenceNumber, ct);
        return tree is null ? NotFound() : Ok(tree);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid applicationId, string sequenceNumber, CreateCtdNodeRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(applicationId, sequenceNumber, request, ct));

    [HttpPut("{nodeId:guid}")]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid applicationId, string sequenceNumber, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(applicationId, sequenceNumber, nodeId, request, ct));

    [HttpDelete("{nodeId:guid}")]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid applicationId, string sequenceNumber, Guid nodeId, [FromQuery] long? expectedRevision, CancellationToken ct) =>
        Ok(await service.DeleteAsync(applicationId, sequenceNumber, nodeId, expectedRevision, ct));

    [HttpPost("inherit")]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Inherit(Guid applicationId, string sequenceNumber, InheritCtdNodesRequest request, CancellationToken ct) =>
        Ok(await service.InheritAsync(applicationId, sequenceNumber, request, ct));

    [HttpPost("{nodeId:guid}/clone/preview")]
    [ProducesResponseType(typeof(CtdNodeClonePreview), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewClone(Guid applicationId, string sequenceNumber, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct) =>
        Ok(await service.PreviewCloneAsync(applicationId, sequenceNumber, nodeId, request, ct));

    [HttpPost("{nodeId:guid}/clone")]
    [ProducesResponseType(typeof(CtdNodeTreeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clone(Guid applicationId, string sequenceNumber, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct) =>
        Ok(await service.CloneAsync(applicationId, sequenceNumber, nodeId, request, ct));
}
