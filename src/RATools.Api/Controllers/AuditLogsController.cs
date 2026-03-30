using Microsoft.AspNetCore.Mvc;
using RATools.Api.Contracts;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Requests;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var items = await auditLogService.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAuditLogRequestBody request, CancellationToken cancellationToken)
    {
        var created = await auditLogService.CreateAsync(
            new CreateAuditLogRequest(request.EntityType, request.EntityId, request.Action, request.Actor, request.Details),
            cancellationToken);

        return Ok(created);
    }
}
