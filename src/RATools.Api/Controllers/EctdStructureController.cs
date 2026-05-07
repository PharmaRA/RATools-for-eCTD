using Microsoft.AspNetCore.Mvc;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.EctdStructure;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/ectd-structure")]
public sealed class EctdStructureController(IEctdStructureService ectdStructureService, IApplicationRepository applicationRepository) : ControllerBase
{
    [HttpGet("/api/applications/{applicationId:guid}/ectd-structure")]
    public async Task<IActionResult> GetByApplication(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        try
        {
            return Ok(ectdStructureService.Get(application.EctdTemplateKey));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
