using Microsoft.AspNetCore.Mvc;
using RATools.Application.Auditing;
using RATools.Application.Auditing.Dtos;

namespace RATools.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AuditLogPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? action,
        [FromQuery] DateTime? createdFromUtc,
        [FromQuery] DateTime? createdToUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // 上限防资源耗尽：审计表只增，pageSize 无界时单请求可拉全表。
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);
        var result = await auditLogService.QueryAsync(
            new AuditLogQuery(page, clampedPageSize, entityType, entityId, action, createdFromUtc, createdToUtc),
            cancellationToken);
        return Ok(result);
    }
}
