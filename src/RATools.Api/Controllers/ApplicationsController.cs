using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController(IApplicationService applicationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var items = await applicationService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await applicationService.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequestBody request, CancellationToken cancellationToken)
    {
        var created = await applicationService.CreateAsync(
            new CreateApplicationRequest(request.ApplicationNumber, request.Region, request.SponsorName),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/sequences")]
    public async Task<IActionResult> CreateSequence(Guid id, [FromBody] CreateSequenceRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await applicationService.CreateSequenceAsync(
                id,
                new CreateSequenceRequest(request.SequenceNumber, request.SubmissionType, request.Description),
                cancellationToken);

            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
