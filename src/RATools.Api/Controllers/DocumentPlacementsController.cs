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
}
