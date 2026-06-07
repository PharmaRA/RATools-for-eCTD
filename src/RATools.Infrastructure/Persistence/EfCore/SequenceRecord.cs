namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class SequenceRecord
{
    public Guid ApplicationId { get; set; }

    public string SequenceNumber { get; set; } = string.Empty;

    public string SubmissionType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? FdaApplicationType { get; set; }

    public string? FdaSubmissionType { get; set; }

    public string? FdaSubmissionSubtype { get; set; }

    public string? FdaSequenceDescription { get; set; }

    public string? FdaApplicantName { get; set; }

    public string? FdaFormType { get; set; }

    public DateTime CreatedUtc { get; set; }
}
