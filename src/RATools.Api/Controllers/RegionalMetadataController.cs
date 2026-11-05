using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.RegionalMetadata;
using RATools.Application.Workspaces;
using RATools.Domain.RegionalMetadata;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/applications/{id:guid}")]
public sealed class RegionalMetadataController(RegionalMetadataService service) : ControllerBase
{
    private const string Sequence = "sequences/{sequenceNumber}/publishing-metadata/regional";
    [HttpGet("regional-metadata/schema")]
    [ProducesResponseType(typeof(RegionalMetadataSchemaDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Schema(Guid id, [FromQuery] string? standardsSnapshotId, CancellationToken cancellationToken) =>
        Run(async () => Ok(await service.GetSchemaAsync(id, standardsSnapshotId, cancellationToken)));

    [HttpGet("regional-metadata/defaults")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Defaults(Guid id, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var head = RegionalMetadataRevisionDto.From(await service.GetScopedHeadAsync(id, null, cancellationToken));
            return head is null ? NotFound(new { code = "RegionalMetadataNotFound", message = "This application has no regional defaults head." }) : Ok(head);
        });

    [HttpPut("regional-metadata/defaults")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> SaveDefaults(Guid id, [FromBody] RegionalDefaultsRequestBody request, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (request.ExpectedDefaultsRevision is null) return StatusCode(428, new { code = "DefaultsRevisionRequired", message = "The expected defaults revision is required." });
            if (request.ExpectedDefaultsRevision is < 0 or >= 9007199254740991L) return BadRequest(new { code = "DefaultsRevisionInvalid", message = "The expected defaults revision must be a nonnegative safe integer." });
            Require(request.Defaults); return Ok(RegionalMetadataRevisionDto.From(await service.SaveApplicationAsync(id, request.ExpectedDefaultsRevision.Value, request.StandardsSnapshotId, request.Defaults, cancellationToken)));
        });

    [HttpPost("regional-metadata/defaults/preflight")]
    [ProducesResponseType(typeof(RegionalMetadataPreflightDto), StatusCodes.Status200OK)]
    public Task<IActionResult> DefaultsPreflight(Guid id, [FromBody] RegionalDefaultsRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => { Require(request.Defaults); return Ok(await service.PreflightDefaultsAsync(id, request.StandardsSnapshotId, request.Defaults, cancellationToken)); });

    [HttpGet("regional-metadata/revisions/{revisionId:guid}")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Revision(Guid id, Guid revisionId, [FromQuery] string? sequenceNumber, CancellationToken cancellationToken) =>
        Run(async () => Ok(RegionalMetadataRevisionDto.From(await service.GetScopedRevisionAsync(id, sequenceNumber, revisionId, cancellationToken))));

    [HttpPost(Sequence + "/preflight")]
    [ProducesResponseType(typeof(RegionalMetadataPreflightDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Preflight(Guid id, string sequenceNumber, [FromBody] RegionalPreflightRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => { Require(request.Content); return Ok(await service.PreflightAsync(id, sequenceNumber, new(request.StandardsSnapshotId, request.Content, request.NodeContexts, request.DocumentContexts), cancellationToken)); });

    [HttpPost(Sequence + "/inheritance/preview")]
    [ProducesResponseType(typeof(RegionalMetadataPreviewDto), StatusCodes.Status200OK)]
    public Task<IActionResult> InheritancePreview(Guid id, string sequenceNumber, [FromBody] RegionalInheritanceRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => Ok(await service.PreviewInheritanceAsync(id, sequenceNumber, request.ExpectedRevision, request.DefaultsRevisionId, cancellationToken)));

    [HttpPost(Sequence + "/inheritance")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Inherit(Guid id, string sequenceNumber, [FromBody] RegionalInheritanceRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => Ok(RegionalMetadataRevisionDto.From(await service.InheritAsync(id, sequenceNumber, request.ExpectedRevision, request.DefaultsRevisionId, cancellationToken))));

    [HttpPost(Sequence + "/node-context-copy/preview")]
    [ProducesResponseType(typeof(RegionalMetadataPreviewDto), StatusCodes.Status200OK)]
    public Task<IActionResult> CopyPreview(Guid id, string sequenceNumber, [FromBody] RegionalNodeContextCopyRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => { Require(request.PlacementIds); return Ok(await service.PreviewNodeContextCopyAsync(id, sequenceNumber, request.ExpectedRevision, request.SourceRevisionId, request.NodeInstanceId, request.PlacementIds, cancellationToken)); });

    [HttpPost(Sequence + "/node-context-copy")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Copy(Guid id, string sequenceNumber, [FromBody] RegionalNodeContextCopyRequestBody request, CancellationToken cancellationToken) =>
        Run(async () => { Require(request.PlacementIds); return Ok(RegionalMetadataRevisionDto.From(await service.CopyNodeContextAsync(id, sequenceNumber, request.ExpectedRevision, request.SourceRevisionId, request.NodeInstanceId, request.PlacementIds, cancellationToken))); });

    [HttpPost(Sequence + "/legacy-upgrade")]
    [ProducesResponseType(typeof(RegionalMetadataRevisionDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Upgrade(Guid id, string sequenceNumber, [FromBody] RegionalLegacyUpgradeRequestBody request, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (request.ExpectedRevision is null) throw new WorkspaceRevisionRequiredException();
            if (request.ExpectedRevision is < 0 or >= 9007199254740991L) throw new WorkspaceRevisionInvalidException();
            return Ok(RegionalMetadataRevisionDto.From(await service.UpgradeLegacyAsync(id, sequenceNumber, request.ExpectedRevision, cancellationToken)));
        });

    private static void Require(object? content) { if (content is null) throw new ArgumentException("Regional content cannot be null."); }
    private async Task<IActionResult> Run(Func<Task<IActionResult>> operation)
    {
        try { return await operation(); }
        catch (KeyNotFoundException error) { return NotFound(new { code = "RegionalMetadataNotFound", message = error.Message }); }
        catch (RegionalMetadataScopeException error) { return BadRequest(new { code = "RegionalMetadataScope", message = error.Message }); }
        catch (RegionalMetadataConflictException error) { return Conflict(new { code = "RegionalMetadataConflict", message = error.Message }); }
        catch (ArgumentException error) { return BadRequest(new { code = "RegionalMetadataInvalid", message = error.Message }); }
    }
}
