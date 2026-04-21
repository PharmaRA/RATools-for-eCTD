using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/document-placements")]
public sealed class DocumentPlacementsController(IDocumentPlacementService placementService) : ControllerBase
{
    [HttpGet]
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
                    request.Title),
                cancellationToken);

            return Ok(created);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await placementService.DeleteAsync(id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (DocumentPlacementDeleteConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/section")]
    public async Task<IActionResult> UpdateSection(Guid id, [FromBody] UpdateDocumentPlacementSectionRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await placementService.UpdateSectionAsync(id, new UpdateDocumentPlacementSectionRequest(request.CtdSection), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/metadata")]
    public async Task<IActionResult> UpdateMetadata(Guid id, [FromBody] UpdateDocumentPlacementMetadataRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await placementService.UpdateMetadataAsync(id, new UpdateDocumentPlacementMetadataRequest(request.Title, request.FileNamePrefix), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
