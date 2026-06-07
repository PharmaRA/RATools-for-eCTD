using RATools.Application.Publishing.PackageModel;

namespace RATools.Tests.Publishing.PackageModel;

public sealed class EctdPackageModelBuilderTests
{
    [Fact]
    public void PackageRecords_ExposeExpectedImmutableContract()
    {
        var applicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var placementId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var documentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var package = new EctdSequencePackage(
            applicationId,
            "ANDA123456",
            "0001",
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "3.2.2",
            "3.3",
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
            new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
            [
                new EctdLeaf(
                    placementId,
                    documentId,
                    "leaf-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "0001",
                    "m1.1",
                    "m1",
                    "new",
                    "Cover Letter",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    "application/pdf",
                    "C:/work/0001/m1/us/cover.pdf",
                    20,
                    "sha256",
                    null)
            ],
            [],
            [
                new EctdPublishedFile(
                    documentId,
                    "C:/work/0001/m1/us/cover.pdf",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    20,
                    "sha256")
            ]);

        Assert.Equal(applicationId, package.ApplicationId);
        Assert.Equal("3.2.2", package.IchEctdVersion);
        Assert.Single(package.Module1Leaves);
        Assert.Empty(package.IchBackboneLeaves);
        Assert.Single(package.PublishedFiles);
    }
}
