using Microsoft.AspNetCore.Mvc;
using RATools.Application.EctdStructure;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/ectd-structure")]
public sealed class EctdStructureController(IEctdStructureService ectdStructureService) : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] string region)
    {
        try
        {
            return Ok(ectdStructureService.Get(region));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
