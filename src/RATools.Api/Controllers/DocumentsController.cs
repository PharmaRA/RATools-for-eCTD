using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(IDocumentService documentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? applicationId, [FromQuery] string? sequenceNumber, CancellationToken cancellationToken)
    {
        if (applicationId.HasValue)
        {
            var scopedItems = await documentService.ListByApplicationAsync(applicationId.Value, sequenceNumber, cancellationToken);
            return Ok(scopedItems);
        }

        var items = await documentService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await documentService.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequestBody request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var created = await documentService.UploadAsync(
                new UploadDocumentRequest
                {
                    FileName = request.File.FileName,
                    MediaType = string.IsNullOrWhiteSpace(request.File.ContentType)
                        ? "application/octet-stream"
                        : request.File.ContentType,
                    Content = stream
                },
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DocumentFileValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/api/applications/{applicationId:guid}/sequences/{sequenceNumber}/documents/upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadToSequence(Guid applicationId, string sequenceNumber, [FromForm] UploadSequenceDocumentRequestBody request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var created = await documentService.UploadToSequenceAsync(
                applicationId,
                sequenceNumber,
                new UploadSequenceDocumentRequest
                {
                    FileName = request.File.FileName,
                    MediaType = string.IsNullOrWhiteSpace(request.File.ContentType)
                        ? "application/octet-stream"
                        : request.File.ContentType,
                    CtdSection = request.CtdSection,
                    Content = stream
                },
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DocumentSequenceUploadTargetNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (DocumentSequenceUploadConfigurationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (DocumentFileValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await documentService.DeleteAsync(id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (DocumentDeleteConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
