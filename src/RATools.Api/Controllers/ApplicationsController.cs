using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Applications;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Applications.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController(
    IApplicationService applicationService,
    IApplicationImportService applicationImportService,
    IApplicationPublishHistoryService publishHistoryService) : ControllerBase
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

    [HttpGet("{id:guid}/publish-history")]
    public async Task<IActionResult> GetPublishHistory(
        Guid id,
        [FromQuery] string? sequenceNumber,
        [FromQuery] string? status,
        [FromQuery] DateTime? createdFromUtc,
        [FromQuery] DateTime? createdToUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var history = await publishHistoryService.GetAsync(
            id,
            new ApplicationPublishHistoryQuery(sequenceNumber, page, pageSize, status, createdFromUtc, createdToUtc),
            cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await applicationService.CreateAsync(
                new CreateApplicationRequest(request.ApplicationNumber, request.EctdTemplateKey, request.SponsorName, request.WorkingDirectoryParentPath),
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (EctdTemplateNotFoundException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportApplicationRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await applicationImportService.ImportAsync(
                new ImportApplicationRequest(request.WorkingDirectoryPath, request.EctdTemplateKey, request.SponsorName),
                cancellationToken);

            return Ok(result);
        }
        catch (ApplicationImportConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (EctdTemplateNotFoundException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] ApplicationDeleteMode deleteMode = ApplicationDeleteMode.DatabaseOnly,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ApplicationDeleteMode), deleteMode))
        {
            return BadRequest(new { message = $"Unsupported deleteMode '{deleteMode}'." });
        }

        try
        {
            var deleted = await applicationService.DeleteAsync(id, deleteMode, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (ApplicationDeleteConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (WorkspacePurgeFailedException exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = exception.Message });
        }
    }

    [HttpDelete("{id:guid}/sequences/{sequenceNumber}")]
    public async Task<IActionResult> DeleteSequence(
        Guid id,
        string sequenceNumber,
        [FromQuery] ApplicationDeleteMode deleteMode = ApplicationDeleteMode.DatabaseOnly,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ApplicationDeleteMode), deleteMode))
        {
            return BadRequest(new { message = $"Unsupported deleteMode '{deleteMode}'." });
        }

        try
        {
            var deleted = await applicationService.DeleteSequenceAsync(id, sequenceNumber, deleteMode, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (SequenceDeleteConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (WorkspacePurgeFailedException exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = exception.Message });
        }
    }
}
