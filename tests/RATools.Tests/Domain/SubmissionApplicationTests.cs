using RATools.Domain.Applications;

namespace RATools.Tests.Domain;

public sealed class SubmissionApplicationTests
{
    public static TheoryData<string> UnsafeApplicationNumbers => new()
    {
        ".",
        "..",
        "../escape",
        "..\\escape",
        "/var/tmp/escape",
        "C:\\escape",
        "\\\\server\\share",
        "mixed/..\\escape",
        "CON",
        "nul.txt",
        "COM1",
        "COM\u00B9",
        "LPT9.log",
        "application."
    };

    [Fact]
    public void Constructor_TrimsFieldsAndRejectsBlank()
    {
        var application = new SubmissionApplication(" NDA123456 ", " US ", " Acme ", " C:/workspace ", " us-fda-ectd-3.2.2 ");

        Assert.Equal("NDA123456", application.ApplicationNumber);
        Assert.Equal("US", application.Region);
        Assert.Equal("Acme", application.SponsorName);
        Assert.Equal("C:/workspace", application.WorkingDirectoryPath);
        Assert.Equal("us-fda-ectd-3.2.2", application.EctdTemplateKey);

        Assert.Throws<ArgumentException>(() => new SubmissionApplication("", "US", "Acme", "C:/w", "key"));
        Assert.Throws<ArgumentException>(() => new SubmissionApplication("APP", " ", "Acme", "C:/w", "key"));
        Assert.Throws<ArgumentException>(() => new SubmissionApplication("APP", "US", "", "C:/w", "key"));
        Assert.Throws<ArgumentException>(() => new SubmissionApplication("APP", "US", "Acme", "", "key"));
        Assert.Throws<ArgumentException>(() => new SubmissionApplication("APP", "US", "Acme", "C:/w", " "));
    }

    [Fact]
    public void CreateSequence_AddsSequenceAndRejectsDuplicates()
    {
        var application = CreateApplication();

        var sequence = application.CreateSequence("0000", "original-application", "Initial");

        Assert.Single(application.Sequences);
        Assert.Equal("0000", sequence.SequenceNumber);
        Assert.Throws<InvalidOperationException>(
            () => application.CreateSequence("0000", "supplement", "Duplicate"));
    }

    [Theory]
    [MemberData(nameof(UnsafeApplicationNumbers))]
    public void Constructor_RejectsApplicationNumbersThatAreNotPortablePathSegments(string applicationNumber)
    {
        Assert.Throws<ArgumentException>(() => new SubmissionApplication(
            applicationNumber,
            "US",
            "Acme",
            "C:/workspace",
            "us-fda-ectd-3.2.2"));
    }

    [Fact]
    public void RemoveSequence_RemovesExistingAndReportsMissing()
    {
        var application = CreateApplication();
        application.CreateSequence("0000", "original-application", "Initial");

        Assert.True(application.RemoveSequence("0000"));
        Assert.Empty(application.Sequences);
        Assert.False(application.RemoveSequence("0000"));
    }

    [Fact]
    public void Sequences_IsReadOnlyView()
    {
        var application = CreateApplication();
        application.CreateSequence("0000", "original-application", "Initial");

        Assert.IsNotType<List<SubmissionSequence>>(application.Sequences);
    }

    private static SubmissionApplication CreateApplication()
        => new("NDA123456", "US", "Acme Pharma", "C:/workspace/NDA123456", "us-fda-ectd-3.2.2");
}
