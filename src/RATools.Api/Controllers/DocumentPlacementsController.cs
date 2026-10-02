using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Documents;
using RATools.Application.Documents.Dtos;
using RATools.Application.Documents.Requests;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/document-placements")]
public sealed class DocumentPlacementsController(IDocumentPlacementService placementService, CtdNodePlacementService nodePlacements) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DocumentPlacementDto[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? applicationId, CancellationToken cancellationToken)
    {
        if (applicationId.HasValue)
        {
            var filtered = await placementService.ListByApplicationAsync(applicationId.Value, cancellationToken);
            return Ok(filtered);
        }

        var items = await placementService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DocumentPlacementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentPlacementRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await placementService.CreateAsync(
                new CreateDocumentPlacementRequest(
                    request.DocumentId,
                    request.ApplicationId,
                    request.SequenceNumber,
                    request.CtdSection,
                    request.Operation,
                    request.Title,
                    request.ExpectedRevision),
                cancellationToken);

            return Ok(created);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] long? expectedRevision, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await placementService.DeleteAsync(id, expectedRevision, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (DocumentPlacementDeleteConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/section")]
    [ProducesResponseType(typeof(DocumentPlacementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSection(Guid id, [FromBody] UpdateDocumentPlacementSectionRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await placementService.UpdateSectionAsync(id, new UpdateDocumentPlacementSectionRequest(
                request.CtdSection, request.ExpectedRevision, request.NodeInstanceId, request.SortOrder ?? 0), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (CtdNodeConstraintException exception)
        {
            return Conflict(new { message = exception.Message, code = exception.Code, nodeInstanceId = exception.NodeInstanceId });
        }
        catch (IOException exception)
        {
            return Conflict(new { message = exception.Message, code = "NodeMoveFileConflict" });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/section/preview")]
    [ProducesResponseType(typeof(NodePlacementMovePreview), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewSection(Guid id, [FromBody] UpdateDocumentPlacementSectionRequestBody request, CancellationToken cancellationToken)
    {
        if (request.NodeInstanceId is not { } nodeId)
            return BadRequest(new { message = "Select a target node instance.", code = "NodeSelectionRequired" });
        try
        {
            return Ok(await nodePlacements.MoveAsync(id, new(nodeId, request.SortOrder ?? 0, request.ExpectedRevision, request.CtdSection),
                previewOnly: true, cancellationToken));
        }
        catch (CtdNodeConstraintException exception)
        {
            return Conflict(new { message = exception.Message, code = exception.Code, nodeInstanceId = exception.NodeInstanceId });
        }
        catch (IOException exception)
        {
            return Conflict(new { message = exception.Message, code = "NodeMoveFileConflict" });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message, code = "NodeMoveConflict" });
        }
    }

    [HttpPut("{id:guid}/metadata")]
    [ProducesResponseType(typeof(DocumentPlacementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMetadata(Guid id, [FromBody] UpdateDocumentPlacementMetadataRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await placementService.UpdateMetadataAsync(id, new UpdateDocumentPlacementMetadataRequest(request.Title, request.Operation, request.FileNamePrefix, request.LifecycleTargetPlacementId, request.ExpectedRevision), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
