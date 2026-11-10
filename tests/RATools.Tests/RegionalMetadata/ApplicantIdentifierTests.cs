using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.Ctd;
using RATools.Application.Documents;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.RegionalMetadata;

public sealed class ApplicantIdentifierTests
{
    [Theory]
    [InlineData("000001")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678A")]
    [InlineData("１２３４５６７８９")]
    [InlineData(" 123456789 ")]
    public async Task InvalidIdentifierIsRetainedWithFieldDiagnosticAndCannotProduceXml(string value)
    {
        var rig = await Rig.Create(); var content = Content(value);
        var before = await rig.Revision();
        var preview = await rig.Service.PreflightAsync(rig.App.Id, "0001", new(rig.Snapshot.Id, content));
        Assert.Contains(preview.Diagnostics, issue => issue.Code == "FDA47-2044" && issue.FieldPath == "us.applicantIdentifier");
        Assert.Equal(before, await rig.Revision()); Assert.Null(await rig.Service.GetSequenceAsync(rig.App.Id, "0001"));
        var saved = await rig.Save(content);
        Assert.Equal(value, saved.Content!.Us!.ApplicantIdentifier);
        Assert.Contains(saved.Diagnostics, issue => issue.Code == "FDA47-2044" && issue.FieldPath == "us.applicantIdentifier");
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("us.applicantIdentifier", error.FieldName);
        Assert.Equal(value, (await rig.Service.GetRevisionAsync(saved.Id)).Content!.Us!.ApplicantIdentifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingIdentifierStaysMissingAndRequiresExplicitCorrection(string? value)
    {
        var rig = await Rig.Create(); var saved = await rig.Save(Content(value));
        Assert.Equal(value, saved.Content!.Us!.ApplicantIdentifier);
        Assert.Contains(saved.Diagnostics, issue => issue.Code == "Required" && issue.FieldPath == "us.applicantIdentifier");
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("us.applicantIdentifier", error.FieldName);
    }

    [Fact]
    public async Task LegacyApplicationNumberIsNeverInventedAsApplicantIdentifier()
    {
        var rig = await Rig.Create();
        var package = await rig.Builder().BuildAsync(new(rig.App.Id, "0001"));
        Assert.Equal("000001", package.ApplicationNumber);
        Assert.Equal("", package.UsRegional.ApplicantId);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal("us.applicantIdentifier", error.FieldName);
        Assert.Contains("regional", error.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await rig.Service.GetSequenceAsync(rig.App.Id, "0001"));
    }

    [Fact]
    public async Task ExplicitIdentifierSurvivesSaveReloadBuildXmlAndIndependentValidation()
    {
        var rig = await Rig.Create(); var saved = await rig.Save(Content("001234567"));
        Assert.DoesNotContain(saved.Diagnostics, issue => issue.FieldPath == "us.applicantIdentifier");
        var reread = await rig.Service.GetRevisionAsync(saved.Id);
        Assert.Equal("001234567", reread.Content!.Us!.ApplicantIdentifier);
        var package = await rig.Builder().BuildAsync(new(rig.App.Id, "0001"));
        Assert.Equal("001234567", package.UsRegional.ApplicantId);
        var xml = new UsRegionalXmlWriter().Write(package);
        Assert.Equal("001234567", Assert.Single(xml.Document.Descendants("id")).Value);
        Assert.Equal("000001", Assert.Single(xml.Document.Descendants("application-number")).Value);
        var context = PackageStandardsContext.FromSnapshot(rig.Snapshot, selectionDate: new(2026, 11, 10));
        var result = await PackageRegionalInspectionTests.Inspect(xml.XmlContent, context: context);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2044").Status);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2036").Status);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
        await rig.Save(Content("009876543"));
        Assert.Equal("001234567", (await rig.Service.GetRevisionAsync(saved.Id)).Content!.Us!.ApplicantIdentifier);
        Assert.Equal("009876543", new UsRegionalXmlWriter().Write(await rig.Builder().BuildAsync(new(rig.App.Id, "0001"))).Document.Descendants("id").Single().Value);
    }

    private static RegionalMetadataContent Content(string? applicant) => new(1, "US", Us: new("000001", "nda", "Synthetic Applicant",
        [new(Guid.NewGuid(), "Contact", "regulatory", [new(Guid.NewGuid(), "5550100", "business")], [new(Guid.NewGuid(), "synthetic@example.test")])],
        "original-application", "application", null, "Synthetic submission", applicant, "0001"));

    private sealed class Rig
    {
        public required SubmissionApplication App { get; init; }
        public required InMemoryApplicationRepository Apps { get; init; }
        public required InMemoryDocumentPlacementRepository Placements { get; init; }
        public required InMemoryDocumentRepository Documents { get; init; }
        public required InMemoryCtdNodeRepository Nodes { get; init; }
        public required InMemoryWorkspaceRevisionStore Revisions { get; init; }
        public required InMemoryApplicationImportStore Imports { get; init; }
        public required InMemoryRegionalMetadataStore Store { get; init; }
        public required StandardsSnapshotService Snapshots { get; init; }
        public required StandardsProfileSnapshot Snapshot { get; init; }
        public required RegionalMetadataService Service { get; init; }
        public EctdPackageModelBuilder Builder() => new(Apps, Placements, Documents, new FdaEctd322StandardsProfileProvider(),
            new DocumentStorageBoundary(new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [App.WorkingDirectoryPath] }))),
            Nodes, Revisions, Imports, regionalMetadataStore: Store, standardsSnapshots: Snapshots);
        public async Task<long> Revision() => (await Revisions.GetRevisionAsync(App.Id, "0001"))!.Value;
        public async Task<RegionalMetadataRevision> Save(RegionalMetadataContent content) => await Service.SaveSequenceAsync(App.Id, "0001", await Revision(), Snapshot.Id, content);
        public static async Task<Rig> Create()
        {
            var root = Path.GetFullPath(".artifacts/applicant-id-fixtures/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var app = new SubmissionApplication("000001", "US", "Synthetic Applicant", root, "us-fda-ectd-3.2.2");
            app.CreateSequence("0001", "original-application", "Synthetic submission").RevisePublishingMetadata(SequencePublishingMetadata.Create(
                "nda", "original-application", "application", "Synthetic submission", "Synthetic Applicant", null, "Contact", "regulatory", "5550100", "business", "synthetic@example.test"));
            var apps = new InMemoryApplicationRepository(); await apps.AddAsync(app);
            var placements = new InMemoryDocumentPlacementRepository(); var documents = new InMemoryDocumentRepository();
            var revisions = new InMemoryWorkspaceRevisionStore(apps); var nodes = new InMemoryCtdNodeRepository(revisions, placements);
            await nodes.SaveSequenceAsync(new CtdNodeGraph(app.Id, IchSectionDefinitions.Current), "0001", [], 0);
            var imports = new InMemoryApplicationImportStore(apps, documents, placements, nodes);
            var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider());
            var snapshot = await snapshots.CaptureAsync(app.EctdTemplateKey); var store = new InMemoryRegionalMetadataStore();
            var transaction = new InMemoryPersistenceTransaction();
            return new() { App = app, Apps = apps, Placements = placements, Documents = documents, Nodes = nodes, Revisions = revisions, Imports = imports,
                Store = store, Snapshots = snapshots, Snapshot = snapshot,
                Service = new(store, apps, nodes, placements, snapshots, new WorkspaceMutationCoordinator(revisions, transaction), revisions, transaction) };
        }
    }
}
