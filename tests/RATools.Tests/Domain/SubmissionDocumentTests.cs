using RATools.Domain.Documents;

namespace RATools.Tests.Domain;

public sealed class SubmissionDocumentTests
{
    [Fact]
    public void Constructor_RequiresMd5ForNewDocuments()
    {
        // 新文档必须携带 MD5（backbone 校验和的事实来源）；Rehydrate 容忍存量空值。
        Assert.Throws<ArgumentException>(
            () => new SubmissionDocument("a.pdf", "application/pdf", 1, "sha", "", "C:/w/a.pdf"));

        var legacy = SubmissionDocument.Rehydrate(
            Guid.NewGuid(), "a.pdf", "application/pdf", 1, "sha", "", "C:/w/a.pdf", DateTime.UtcNow);
        Assert.Equal(string.Empty, legacy.Md5);
    }

    [Fact]
    public void Constructor_RejectsNegativeFileSizeAndBlankCoreFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SubmissionDocument("a.pdf", "application/pdf", -1, "sha", "md5", "C:/w/a.pdf"));
        Assert.Throws<ArgumentException>(
            () => new SubmissionDocument("", "application/pdf", 1, "sha", "md5", "C:/w/a.pdf"));
        Assert.Throws<ArgumentException>(
            () => new SubmissionDocument("a.pdf", "", 1, "sha", "md5", "C:/w/a.pdf"));
        Assert.Throws<ArgumentException>(
            () => new SubmissionDocument("a.pdf", "application/pdf", 1, "", "md5", "C:/w/a.pdf"));
        Assert.Throws<ArgumentException>(
            () => new SubmissionDocument("a.pdf", "application/pdf", 1, "sha", "md5", " "));
    }

    [Fact]
    public void Relocate_UpdatesStoragePathAndRejectsBlank()
    {
        var document = CreateDocument();

        document.Relocate(" D:/moved/a.pdf ");

        Assert.Equal("D:/moved/a.pdf", document.StoragePath);
        Assert.Throws<ArgumentException>(() => document.Relocate("  "));
    }

    [Fact]
    public void ReviseFileMetadata_UpdatesNameAndMediaType()
    {
        var document = CreateDocument();

        document.ReviseFileMetadata(" renamed.pdf ", " application/pdf ");

        Assert.Equal("renamed.pdf", document.FileName);
        Assert.Equal("application/pdf", document.MediaType);
        Assert.Throws<ArgumentException>(() => document.ReviseFileMetadata("", "application/pdf"));
        Assert.Throws<ArgumentException>(() => document.ReviseFileMetadata("a.pdf", " "));
    }

    private static SubmissionDocument CreateDocument()
        => new("a.pdf", "application/pdf", 1, "sha", "md5", "C:/w/a.pdf");
}
