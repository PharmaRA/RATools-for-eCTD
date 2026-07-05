using RATools.Application.Auditing.Dtos;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;

namespace RATools.Tests.Publishing;

public sealed class PublishAuditSummaryBuilderTests
{
    [Fact]
    public void Create_CountsMatchingAuditEventsAndSelectsLatestPublishJobEvent()
    {
        var publishJobId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var sequenceNumber = "0001";
        var older = DateTime.UtcNow.AddMinutes(-10);
        var newer = DateTime.UtcNow.AddMinutes(-1);
        var publishJob = new PublishJobDto(
            publishJobId,
            applicationId,
            sequenceNumber,
            "Completed",
            null,
            null,
            older,
            newer,
            null);
        var auditLogs = new[]
        {
            AuditLog("PublishJob", publishJobId.ToString(), "Created", older),
            AuditLog("SequenceValidation", $"{applicationId}:{sequenceNumber}", "ValidationPassed", older.AddMinutes(1)),
            AuditLog("PublishJob", Guid.NewGuid().ToString(), "OtherJob", newer),
            AuditLog("PublishJob", publishJobId.ToString(), "Completed", newer),
            AuditLog("SequenceValidation", $"{applicationId}:0002", "OtherSequence", newer),
        };

        var summary = PublishAuditSummaryBuilder.Create(auditLogs, publishJob, sequenceNumber);

        Assert.Equal(2, summary.PublishJobEventCount);
        Assert.Equal(1, summary.ValidationEventCount);
        Assert.Equal("Completed", summary.LatestPublishJobAction);
        Assert.Equal(newer, summary.LatestPublishJobEventUtc);
    }

    private static AuditLogDto AuditLog(string entityType, string entityId, string action, DateTime createdUtc)
        => new(Guid.NewGuid(), entityType, entityId, action, "system", null, createdUtc);
}
