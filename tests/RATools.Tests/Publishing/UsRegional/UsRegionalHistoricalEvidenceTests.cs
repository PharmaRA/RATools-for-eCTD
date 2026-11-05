using System.Text.Json;
using RATools.Application.Publishing.PackageModel;
using RATools.Domain.RegionalMetadata;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalHistoricalEvidenceTests
{
    [Fact]
    public void HistoricalLiteralRevisionRetainsItsPreExtensionDigest()
    {
        const string oldJson = """
            {"Id":"11111111-1111-1111-1111-111111111111","ApplicationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","SequenceNumber":"0001","MetadataRevision":1,"WorkspaceRevision":1,"StandardsSnapshotId":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Content":{"SchemaVersion":1,"RegionKey":"US","Us":{"ApplicationIdentifier":"123456","ApplicationType":"nda","ApplicantName":"Historical applicant","Contacts":[],"SubmissionType":"original-application","SubmissionSubtype":"initial","FormType":null,"Description":"Historical description"},"Eu":null},"Defaults":null,"NodeContexts":[],"DocumentContexts":[],"InheritedDefaultsId":null,"InheritedDefaultsDigest":null,"Provenance":[],"Diagnostics":[],"LegacyJson":null,"LegacyDigest":null,"Digest":"35c9f31d91da1705a2e018e704d501a5d7c5ee3b04fce293a8899d36845d23a7","CreatedUtc":"2026-11-01T00:00:00Z"}
            """;
        var revision = JsonSerializer.Deserialize<RegionalMetadataRevision>(oldJson)!;
        revision.Verify();
        Assert.Equal("35c9f31d91da1705a2e018e704d501a5d7c5ee3b04fce293a8899d36845d23a7", revision.Digest);
        var output = JsonSerializer.SerializeToElement(revision.Content!.Us!);
        Assert.False(output.TryGetProperty("ApplicantIdentifier", out _));
        Assert.False(output.TryGetProperty("SubmissionIdentifier", out _));
    }

    [Fact]
    public void LegacyPackageOmitsTheOptionalRegionalRevision()
    {
        var package = ScopedUsRegionalWriterTests.Package();
        var properties = JsonSerializer.SerializeToElement(package).EnumerateObject().Where(property => property.Name != "RegionalMetadata");
        var legacyJson = "{" + string.Join(",", properties.Select(property => JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
        var legacy = JsonSerializer.Deserialize<EctdSequencePackage>(legacyJson)!;
        var serialized = JsonSerializer.SerializeToElement(legacy);
        Assert.False(serialized.TryGetProperty("RegionalMetadata", out _));
    }
}
