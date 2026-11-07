using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RATools.Application.Applications;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Application.RegionalMetadata;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class ScopedUsPackageBindingTests
{
    private static readonly string[] ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues1 = { "source-form-b", "source-form-a" };
    private static readonly string[] ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues2 = { "source-form-a", "source-form-b" };
    private static readonly string[] ExpectedValues3 = new[] { "source-form-a", "source-form-b" };
    private static readonly string[] ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues4 = { "form-b", "form-a" };
    private static readonly string[] ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues5 = { "form-a", "form-b" };
    private static readonly string[] ExpectedValues6 = new[] { "form-a", "form-b" };
    private static readonly string[] ExpectedValues7 = new[] { "first@example.test", "alternate@example.test" };

    [Fact]
    public async Task BuilderCapturesExactRegionalRevisionAndArchivedProfileWithoutCurrentProviderDrift()
    {
        var fixture = await CreateAsync();
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Equal("historical-profile", package.StandardsProfile);
        var captured = JsonSerializer.SerializeToElement(package).GetProperty("RegionalMetadata");
        Assert.Equal(fixture.Revision.Id, captured.GetProperty("Id").GetGuid());
        Assert.Equal(fixture.Revision.Digest, captured.GetProperty("Digest").GetString());
        Assert.Equal(fixture.Revision.StandardsSnapshotId, captured.GetProperty("StandardsSnapshotId").GetString());
        var next = RegionalMetadataRevision.Create(Guid.NewGuid(), fixture.ApplicationId, "0001", 2, 4, fixture.Revision.StandardsSnapshotId,
            fixture.Revision.Content! with { Us = fixture.Revision.Content!.Us! with { ApplicantName = "later-company" } }, null, [], [], null, null, [], [], null, null, DateTime.UtcNow);
        await new InMemoryPersistenceTransaction().ExecuteAsync(ct => fixture.Metadata.AppendAsync(next, fixture.Revision.Id, ct));
        Assert.Equal("typed-company", new UsRegionalXmlWriter().Write(package).Document.Descendants("company-name").Single().Value);
        Assert.Equal(fixture.Revision.Digest, JsonSerializer.SerializeToElement(package).GetProperty("RegionalMetadata").GetProperty("Digest").GetString());
    }

    [Fact]
    public async Task SuppliedDifferentHistoricalProfileCannotReinterpretTypedMetadata()
    {
        var fixture = await CreateAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Builder.BuildWithStandardsAsync(new(fixture.ApplicationId, "0001"), fixture.Profile with { DisplayName = "different-historical-profile" }));
    }

    [Fact]
    public async Task IncompleteHeadCannotFallBackToSharedMetadata()
    {
        var fixture = await CreateAsync(incomplete: true);
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001")));
        Assert.Equal("us.applicantName", error.FieldName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSort(bool explicitSort)
    {
        var fixture = await CreateAsync(importedForms: true, explicitSort: explicitSort);
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Equal(explicitSort ? ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues1 : ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues2, package.Module1Leaves.Select(leaf => leaf.LeafId));
        var xml = new UsRegionalXmlWriter().Write(package).Document;
        Assert.Equal(explicitSort ? ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues4 : ExpectedImportedFormsKeepSourceOrderOnTiesAndRespectExplicitPlacementSortValues5, xml.Descendants("form").Select(form => form.Attribute("form-type")!.Value));
    }

    [Fact]
    public async Task UppercaseImportedM1FormsRetainPersistedContextOrderOnSortTies()
    {
        var fixture = await CreateAsync(importedForms: true, section: "M1.1");
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Equal(ExpectedValues3, package.Module1Leaves.Select(leaf => leaf.LeafId));
    }

    [Theory]
    [InlineData("M1.1", "form-a", null)]
    [InlineData("M1.1", null, "Required")]
    [InlineData("M1.2", "form-a", "RegionalFieldNotApplicable")]
    public async Task DraftValidationUsesCaseInsensitiveExactFormSection(string section, string? form, string? expectedDiagnostic)
    {
        var fixture = await CreateAsync(importedForms: true, section: section);
        var contexts = fixture.Revision.DocumentContexts.Select(context => context with { FormType = form }).ToArray();
        var saved = await fixture.Service.SaveSequenceAsync(fixture.ApplicationId, "0001", 4, fixture.Revision.StandardsSnapshotId,
            fixture.Revision.Content!, documentContexts: contexts);
        var issues = saved.Diagnostics.Where(item => item.FieldPath == "documentContexts[0].formType").ToArray();
        if (expectedDiagnostic is null) Assert.Empty(issues);
        else Assert.Equal(expectedDiagnostic, Assert.Single(issues).Code);
    }

    [Fact]
    public async Task ParserReimportsAllExplicitIdentifiersContactsAndDistinctFormContexts()
    {
        var fixture = await CreateAsync();
        var first = ScopedUsRegionalWriterTests.Leaf(1); var second = ScopedUsRegionalWriterTests.Leaf(2);
        var package = ScopedUsRegionalWriterTests.Package([first, second], documents:
            [new(first.PlacementId, first.NodeInstanceId, null, null, null, "form-a"), new(second.PlacementId, second.NodeInstanceId, null, null, null, "form-b")]);
        var xml = new UsRegionalXmlWriter().Write(package);
        var snapshot = await fixture.Snapshots.GetAsync(fixture.Revision.StandardsSnapshotId);
        var parsed = RegionalImportParser.Parse(Encoding.UTF8.GetBytes(xml.XmlContent), false, "0001", snapshot);
        var content = JsonSerializer.SerializeToElement(parsed.Content.Us);
        Assert.Equal("applicant-business-id", content.GetProperty("ApplicantIdentifier").GetString());
        Assert.Equal("submission-business-id", content.GetProperty("SubmissionIdentifier").GetString());
        Assert.Equal("999888", parsed.Content.Us!.ApplicationIdentifier);
        Assert.Equal(2, parsed.Content.Us.Contacts.Count);
        Assert.Equal(ExpectedValues7, parsed.Content.Us.Contacts[0].Emails.Select(item => item.Value));
        var contexts = xml.Document.Descendants("leaf").Select((leaf, index) => RegionalImportParser.Context(leaf, index == 0 ? first.PlacementId : second.PlacementId, null)).ToArray();
        Assert.Equal(ExpectedValues6, contexts.Select(context => context.FormType));
        Assert.DoesNotContain(parsed.Diagnostics, item => item.Code == "RegionalUnmappedValue" && (item.FieldPath.EndsWith("/id[1]", StringComparison.Ordinal) || item.FieldPath.EndsWith("/submission-id[1]", StringComparison.Ordinal)));
    }

    private sealed record Fixture(Guid ApplicationId, EctdPackageModelBuilder Builder, RegionalMetadataRevision Revision,
        InMemoryRegionalMetadataStore Metadata, StandardsSnapshotService Snapshots, StandardsProfile Profile, RegionalMetadataService Service);

    private static async Task<Fixture> CreateAsync(bool incomplete = false, bool importedForms = false, bool explicitSort = false, string section = "m1.1")
    {
        var applications = new InMemoryApplicationRepository(); var placements = new InMemoryDocumentPlacementRepository(); var documents = new InMemoryDocumentRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(applications); var nodes = new InMemoryCtdNodeRepository(revisions, placements);
        var provider = new FdaEctd322StandardsProfileProvider(Directory.GetCurrentDirectory());
        var historical = provider.GetProfile("us-fda-ectd-3.2.2") with { DisplayName = "historical-profile" };
        var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FixedProfiles(historical), new() { AssetRootPath = Directory.GetCurrentDirectory() });
        var snapshot = await snapshots.CaptureAsync("us-fda-ectd-3.2.2");
        var appId = Guid.NewGuid();
        var sequence = SubmissionSequence.Rehydrate("0001", "legacy-submission", "legacy-description", DateTime.UtcNow, workspaceRevision: 4);
        await applications.AddAsync(SubmissionApplication.Rehydrate(appId, "ANDA-folder-value", "US", "legacy-sponsor", DateTime.UtcNow, [sequence], "C:/fixture/ANDA-folder-value", "us-fda-ectd-3.2.2"));
        var contentJson = JsonNode.Parse(JsonSerializer.Serialize(new RegionalMetadataContent(1, "US", Us: new("999888", "typed-application-type", "typed-company",
            [new(Guid.NewGuid(), "First Contact", "regulatory", [new(Guid.NewGuid(), "555-0101", "office")], [new(Guid.NewGuid(), "first@example.test")])],
            "typed-submission", "typed-subtype", "sequence-form", "typed-description"))))!;
        contentJson["Us"]!["ApplicantIdentifier"] = "applicant-business-id";
        contentJson["Us"]!["SubmissionIdentifier"] = "submission-business-id";
        var content = JsonSerializer.Deserialize<RegionalMetadataContent>(contentJson.ToJsonString())!;
        if (incomplete) content = content with { Us = content.Us! with { ApplicantName = null } };
        RegionalDocumentContext[] contexts = [];
        if (importedForms)
        {
            var document = SubmissionDocument.Rehydrate(Guid.NewGuid(), "shared.pdf", "application/pdf", 10, new string('a', 64), new string('b', 32), "C:/fixture/0001/m1/us/shared.pdf", DateTime.UtcNow);
            await documents.AddAsync(document);
            var first = DocumentPlacement.Rehydrate(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), document.Id, appId, "0001", section, DocumentPlacementOperation.New, "First", null, DateTime.UtcNow,
                "source-form-a", sortOrder: explicitSort ? 1 : 0, importedSource: new("m1/us/us-regional.xml", "shared.pdf", null));
            var second = DocumentPlacement.Rehydrate(Guid.Parse("00000001-0000-0000-0000-000000000000"), document.Id, appId, "0001", section, DocumentPlacementOperation.New, "Second", null, DateTime.UtcNow,
                "source-form-b", importedSource: new("m1/us/us-regional.xml", "shared.pdf", null));
            await placements.AddAsync(second); await placements.AddAsync(first);
            contexts = [new(first.Id, null, null, "fr", null, "form-a"), new(second.Id, null, null, "de", null, "form-b")];
        }
        var regional = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, "0001", 1, 4, snapshot.Id, content, null, [], contexts, null, null, [], [], null, null, DateTime.UtcNow);
        var metadata = new InMemoryRegionalMetadataStore(); await new InMemoryPersistenceTransaction().ExecuteAsync(ct => metadata.AppendAsync(regional, null, ct));
        var imports = new InMemoryApplicationImportStore(applications, documents, placements, nodes);
        var constructor = typeof(EctdPackageModelBuilder).GetConstructors().Single();
        var args = new object?[] { applications, placements, documents, new FixedProfiles(historical with { DisplayName = "current-drift" }), PermissiveDocumentStorageBoundary.Instance, nodes, revisions, imports }
            .Concat(constructor.GetParameters().Skip(8).Select(parameter => parameter.Name switch { "regionalMetadataStore" => (object)metadata, "standardsSnapshots" => snapshots, _ => null })).ToArray();
        var builder = (EctdPackageModelBuilder)constructor.Invoke(args);
        var transaction = new InMemoryPersistenceTransaction();
        var service = new RegionalMetadataService(metadata, applications, nodes, placements, snapshots,
            new WorkspaceMutationCoordinator(revisions, transaction), revisions, transaction);
        return new(appId, builder, regional, metadata, snapshots, historical, service);
    }

    private sealed class FixedProfiles(StandardsProfile profile) : IStandardsProfileProvider
    { public StandardsProfile GetProfile(string templateKey) => profile; }
}
