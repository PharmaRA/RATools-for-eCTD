using RATools.Application.Validation.Dtos;

namespace RATools.Application.Publishing.Dtos;

public sealed record PublishExecutionReportDto(
    string ReportVersion,
    Guid ApplicationId,
    string SequenceNumber,
    string ValidationProfile,
    string? ReportPath,
    ValidationReportDto ValidationReport,
    PublishJobDto PublishJob,
    long DurationMs,
    PublishIntegritySummaryDto? IntegritySummary,
    PublishIntegrityEvidenceDto? IntegrityEvidence,
    PublishReadinessReportDto? PublishReadiness,
    PublishArtifactSummaryDto? ArtifactSummary,
    PublishAuditSummaryDto? AuditSummary,
    int ErrorCount,
    int WarningCount,
    string? WarningSummary,
    bool Succeeded,
    string? Message);
