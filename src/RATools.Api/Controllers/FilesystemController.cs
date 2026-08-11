using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Abstractions.Storage;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/filesystem")]
public sealed class FilesystemController(IServerDirectoryBrowser serverDirectoryBrowser) : ControllerBase
{
    [HttpGet("directories")]
    [ProducesResponseType(typeof(DirectoryBrowseResult), StatusCodes.Status200OK)]
    public IActionResult Browse([FromQuery] string? path, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(serverDirectoryBrowser.Browse(path));
        }
        catch (DirectoryNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("resolve-directory")]
    [ProducesResponseType(typeof(DirectoryResolutionResult), StatusCodes.Status200OK)]
    public IActionResult Resolve([FromBody] ResolveDirectoryRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(serverDirectoryBrowser.Resolve(request.Path));
        }
        catch (DirectoryNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
