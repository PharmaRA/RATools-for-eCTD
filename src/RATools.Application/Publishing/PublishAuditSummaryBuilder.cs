using RATools.Application.Auditing.Dtos;
using RATools.Application.Publishing.Dtos;

namespace RATools.Application.Publishing;

internal static class PublishAuditSummaryBuilder
{
    public static PublishAuditSummaryDto Create(
        IEnumerable<AuditLogDto> auditLogs,
        PublishJobDto publishJob,
        string sequenceNumber)
    {
        var publishJobEventCount = 0;
        var validationEventCount = 0;
        AuditLogDto? latestPublishJobEvent = null;
        var publishJobEntityId = publishJob.Id.ToString();
        var validationEntityId = $"{publishJob.ApplicationId}:{sequenceNumber}";

        foreach (var auditLog in auditLogs)
        {
            if (auditLog.EntityType == "PublishJob" && auditLog.EntityId == publishJobEntityId)
            {
                publishJobEventCount++;

                if (latestPublishJobEvent is null || auditLog.CreatedUtc > latestPublishJobEvent.CreatedUtc)
                {
                    latestPublishJobEvent = auditLog;
                }
            }
            else if (auditLog.EntityType == "SequenceValidation" && auditLog.EntityId == validationEntityId)
            {
                validationEventCount++;
            }
        }

        return new PublishAuditSummaryDto(
            publishJobEventCount,
            validationEventCount,
            latestPublishJobEvent?.Action,
            latestPublishJobEvent?.CreatedUtc);
    }
}
