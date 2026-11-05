using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RATools.Application.Standards;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/standards-snapshots")]
public sealed class StandardsSnapshotsController(StandardsSnapshotService service) : ControllerBase
{
    [HttpPost("/api/ectd-templates/{templateKey}/standards-snapshot")]
    [ProducesResponseType(typeof(StandardsSnapshotView), StatusCodes.Status200OK)]
    public Task<IActionResult> Capture(string templateKey, CancellationToken cancellationToken) => Respond(async () =>
        Ok(service.View(await service.CaptureAsync(templateKey, cancellationToken: cancellationToken))));

    [HttpGet("{snapshotId}")]
    [ProducesResponseType(typeof(StandardsSnapshotView), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(string snapshotId, CancellationToken cancellationToken) => Respond(async () =>
        Ok(service.View(await service.GetAsync(snapshotId, cancellationToken))));

    [HttpGet("{snapshotId}/resources/{resourceKey}/content")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public Task<IActionResult> Content(string snapshotId, string resourceKey, CancellationToken cancellationToken) => Respond(async () =>
    {
        var content = await service.OpenResourceAsync(snapshotId, resourceKey, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return new FileContentResult(content.Bytes, content.MediaType)
        {
            EntityTag = new EntityTagHeaderValue('"' + content.Sha256 + '"'),
            EnableRangeProcessing = true,
            FileDownloadName = content.Key
        };
    });

    private static async Task<IActionResult> Respond(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (KeyNotFoundException exception) { return new NotFoundObjectResult(new { message = exception.Message }); }
        catch (StandardsProfileNotFoundException exception) { return new NotFoundObjectResult(new { message = exception.Message }); }
        catch (StandardsAssetMissingException) { return new ConflictObjectResult(new { code = "STANDARDS_RESOURCE_UNAVAILABLE", message = "The standards resources could not be verified." }); }
        catch (InvalidDataException) { return new ConflictObjectResult(new { code = "STANDARDS_SNAPSHOT_INVALID", message = "The stored standards snapshot failed integrity verification." }); }
        catch (ArgumentException exception) { return new BadRequestObjectResult(new { message = exception.Message }); }
    }
}
