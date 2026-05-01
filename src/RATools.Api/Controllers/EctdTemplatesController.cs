using Microsoft.AspNetCore.Mvc;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.EctdTemplates;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/ectd-templates")]
public sealed class EctdTemplatesController : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var items = EctdTemplateRegistry.All
            .Select(template => new EctdTemplateDto(
                template.Key,
                template.DisplayName,
                template.Region,
                template.StandardName,
                template.StandardVersion))
            .ToArray();

        return Ok(items);
    }
}
