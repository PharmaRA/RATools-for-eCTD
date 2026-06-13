using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Validation;
using RATools.Application.Validation.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/validation")]
public sealed class ValidationController(
    ISequenceValidationService validationService,
    IPublishReadinessService publishReadinessService) : ControllerBase
{
    [HttpPost("sequence")]
    public async Task<IActionResult> ValidateSequence([FromBody] ValidateSequenceRequestBody request, CancellationToken cancellationToken)
    {
        var report = await validationService.ValidateAsync(
            new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);

        return Ok(report);
    }

    [HttpPost("publish-readiness")]
    public async Task<IActionResult> GetPublishReadiness([FromBody] PublishReadinessRequestBody request, CancellationToken cancellationToken)
    {
        var report = await publishReadinessService.GetAsync(
            new ValidateSequenceRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);

        return Ok(report);
    }
}
