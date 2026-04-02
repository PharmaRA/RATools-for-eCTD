using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/backbone")]
public sealed class BackboneController(IBackboneService backboneService) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateBackboneRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var generated = await backboneService.GenerateAsync(
                new GenerateBackboneRequest(request.ApplicationId, request.SequenceNumber, "publish-report.json"),
                cancellationToken);

            return Ok(generated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
