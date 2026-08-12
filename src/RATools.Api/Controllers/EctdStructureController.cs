using Microsoft.AspNetCore.Mvc;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.EctdStructure;
using RATools.Application.EctdStructure.Dtos;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/ectd-structure")]
public sealed class EctdStructureController(IEctdStructureService ectdStructureService, IApplicationRepository applicationRepository) : ControllerBase
{
    [HttpGet("/api/applications/{applicationId:guid}/ectd-structure")]
    [ProducesResponseType(typeof(EctdStructureDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByApplication(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        try
        {
            return Ok(ectdStructureService.GetStructure(application.EctdTemplateKey));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
