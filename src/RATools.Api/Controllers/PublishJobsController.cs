using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
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
        catch (PublishJobReportCorruptedException exception)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { message = exception.Message });
        }
    }

    [HttpGet("{id:guid}/artifacts")]
    public async Task<IActionResult> GetArtifacts(Guid id, CancellationToken cancellationToken)
    {
        var artifacts = await publishJobService.GetArtifactsAsync(id, cancellationToken);
        return artifacts is null ? NotFound() : Ok(artifacts);
    }

    [HttpGet("{id:guid}/artifacts/{name}/download")]
    public async Task<IActionResult> DownloadArtifact(Guid id, string name, CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await publishJobService.GetArtifactDownloadAsync(id, name, cancellationToken);
            if (artifact is null)
            {
                return NotFound();
            }

            return PhysicalFile(artifact.Path, artifact.ContentType, artifact.FileName);
        }
        catch (PublishJobNotReadyException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (PublishJobReportUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status410Gone, new { message = exception.Message });
        }
        catch (PublishArtifactNotSupportedException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost]
    [Obsolete("Use POST /api/publish-jobs/execute to create and enqueue a publish job.")]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult Create()
    {
        return StatusCode(
            StatusCodes.Status410Gone,
            new { message = "POST /api/publish-jobs is deprecated; use POST /api/publish-jobs/execute." });
    }

    [HttpPost("execute")]
    [ProducesResponseType(typeof(PublishJobDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Execute([FromBody] CreatePublishJobRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var job = await publishJobService.EnqueueExecutionAsync(
                new CreatePublishJobRequest(request.ApplicationId, request.SequenceNumber),
                cancellationToken);

            // 发布在后台作业中执行；客户端通过 GetById / report / artifacts 端点轮询状态。
            return AcceptedAtAction(nameof(GetById), new { id = job.Id }, job);
        }
        catch (PublishJobAlreadyInProgressException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
