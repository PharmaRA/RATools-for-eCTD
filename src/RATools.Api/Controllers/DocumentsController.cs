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
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var items = await documentService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await documentService.GetAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDocumentRequestBody request, CancellationToken cancellationToken)
    {
        var created = await documentService.CreateAsync(
            new CreateDocumentRequest(request.FileName, request.MediaType, request.FileSize, request.Sha256, request.StoragePath),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequestBody request, CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return ValidationProblem(ModelState);
        }

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
}
