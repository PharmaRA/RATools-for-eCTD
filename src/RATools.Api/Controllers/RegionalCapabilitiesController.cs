using Microsoft.AspNetCore.Mvc;
using RATools.Application.PackageValidation;
using RATools.Application.Regional;
using RATools.Application.Standards;

namespace RATools.Api.Controllers;

// Read-only capability matrix. It reports what the shipped profile and catalog facts
// actually support; application/procedure scoping remains an explicit placeholder until
// P6 sources real regional criteria.
[ApiController]
[Route("api/regional-capabilities")]
public sealed class RegionalCapabilitiesController(IStandardsProfileProvider standards) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(RegionalCapability[]), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(RegionalCapabilityMatrix.Build(standards, PackageValidationCatalog.Current));
}
