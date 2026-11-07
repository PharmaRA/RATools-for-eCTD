using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/backbone")]
public sealed class BackboneController(IBackboneService backboneService) : ControllerBase
{
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GeneratedBackboneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateBackboneRequestBody request, CancellationToken cancellationToken)
    {
        try
        {
            var publishJobId = Guid.NewGuid();
            var generated = await backboneService.GenerateAsync(
                new GenerateBackboneRequest(
                    request.ApplicationId,
                    request.SequenceNumber,
                    publishJobId,
                    $"publish-report-{request.SequenceNumber}-{publishJobId:N}.json",
                    $"{request.SequenceNumber}-{publishJobId:N}.zip"),
                cancellationToken);

            return Ok(generated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (EuRegionalXmlWriterException exception)
        {
            return BadRequest(new { code = "EU_REGIONAL_METADATA_MISSING", fieldPath = exception.FieldPath ?? "regionalMetadata.eu", message = exception.Message });
        }
        catch (UsRegionalXmlMetadataException exception)
        {
            return BadRequest(new { code = "US_REGIONAL_METADATA_MISSING", fieldPath = exception.FieldName, message = exception.Message });
        }
    }
}
