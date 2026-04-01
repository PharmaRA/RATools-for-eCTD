using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/validation")]
public sealed class ValidationController(ISequenceValidationService validationService) : ControllerBase
{
    [HttpPost("sequence")]
    public async Task<IActionResult> ValidateSequence([FromBody] ValidateSequenceRequestBody request, CancellationToken cancellationToken)
    {
        var report = await validationService.ValidateAsync(
            new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);

        return Ok(report);
    }
}
