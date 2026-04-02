using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/publish-jobs")]
public sealed class PublishJobsController(IPublishJobService publishJobService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var items = await publishJobService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await publishJobService.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> GetExecutionReport(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var report = await publishJobService.GetExecutionReportAsync(id, cancellationToken);
            return report is null ? NotFound() : Ok(report);
        }
        catch (PublishJobNotReadyException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (PublishJobReportUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status410Gone, new { message = exception.Message });
        }
    }

    [HttpGet("{id:guid}/artifacts")]
    public async Task<IActionResult> GetArtifacts(Guid id, CancellationToken cancellationToken)
    {
        var artifacts = await publishJobService.GetArtifactsAsync(id, cancellationToken);
        return artifacts is null ? NotFound() : Ok(artifacts);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePublishJobRequestBody request, CancellationToken cancellationToken)
    {
        var created = await publishJobService.CreateAsync(
            new CreatePublishJobRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] CreatePublishJobRequestBody request, CancellationToken cancellationToken)
    {
        var report = await publishJobService.ExecuteAsync(
            new CreatePublishJobRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);

        return Ok(report);
    }
}
