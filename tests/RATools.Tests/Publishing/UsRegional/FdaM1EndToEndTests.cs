using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.Applications;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Publishing.UsRegional;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class FdaM1EndToEndTests
{
    private static readonly string[] ExpectedValues1 = new[] { "First source contact", "Second source contact" };
    private static readonly string[] ExpectedValues2 = new[] { "first@example.test", "alternate@example.test", "second@example.test" };
    private static readonly string[] ExpectedValues3 = new[] { "356h", "1571" };
    private static readonly string[] ExpectedValues4 = new[] { "source-form-a", "source-form-b" };
    private static readonly string[] ExpectedValues5 = new[] { "fr", "de" };
    private static readonly string[] ExpectedValues6 = new[] { "356h","1571" };
    private static readonly string[] ExpectedValues7 = new[] { "fr","de" };

    [Fact]
    public async Task ActualHttpSaveOfExplicitIdentifiersReachesDistinctFormXmlAndFullReimport()
    {
        var fixture = await CreateAsync();
        var apps = new InMemoryApplicationRepository(); var docs = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(apps); var nodes = new InMemoryCtdNodeRepository(revisions, placements); var metadata = new InMemoryRegionalMetadataStore();
        var snapshots = ImportAcceptanceFixture.MemorySnapshots(); var imports = new InMemoryApplicationImportStore(apps, docs, placements, nodes, metadata);
        var importer = new ApplicationImportService(apps, imports, fixture, snapshots);
        var imported = await importer.ImportAsync(new(fixture.Root, "us-fda-ectd-3.2.2", "Request sponsor"));
        Assert.Equal(1, imported.ImportedSequenceCount);
        var transaction = new InMemoryPersistenceTransaction(); var mutations = new WorkspaceMutationCoordinator(revisions, transaction);
        var regional = new RegionalMetadataService(metadata, apps, nodes, placements, snapshots, mutations, revisions, transaction);
        var shared = new SequencePublishingMetadataService(apps, new FdaEctd322StandardsProfileProvider(), mutations, regional);
        await using var host = ImportAcceptanceFixture.Host(fixture.Root, importer, shared); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/applications/{imported.ApplicationId}/sequences/0000/publishing-metadata";
        var dto = (await client.GetFromJsonAsync<SequencePublishingMetadataDto>(path))!;
        Assert.Equal("source-applicant", dto.Regional!.Content!.Us!.ApplicantIdentifier);
        var updated = dto.Regional.Content with { Us = dto.Regional.Content.Us with { ApplicantIdentifier = "001234567", SubmissionIdentifier = "http-submission-id" } };
        using var saved = await client.PutAsJsonAsync(path, new UpdateSequencePublishingMetadataRequest(dto.ApplicationType, dto.SubmissionType, dto.SubmissionSubtype,
            dto.SequenceDescription, dto.ApplicantName, dto.FormType, dto.ApplicantContactName, dto.ApplicantContactType, dto.Telephone, dto.TelephoneNumberType, dto.Email,
            dto.WorkspaceRevision, new(dto.Regional.StandardsSnapshotId, updated, dto.Regional.NodeContexts, dto.Regional.DocumentContexts)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var committed = (await saved.Content.ReadFromJsonAsync<SequencePublishingMetadataDto>())!;
        var builder = Builder(apps, docs, placements, nodes, revisions, imports, metadata, snapshots, fixture);
        var package = await builder.BuildAsync(new(imported.ApplicationId, "0000"));
        Assert.Equal(committed.Regional!.Id, package.RegionalMetadata!.Id); Assert.Equal(2, package.WorkspaceRevision);
        await VerifyOutputAndReimportAsync(package, snapshots, "001234567", "http-submission-id");
    }

    [RequiresPostgresFact]
    public async Task NewPostgresConnectionBuildsTheImportedTypedRevisionAndPreservesAllFormContexts()
    {
        var fixture = await CreateAsync(validApplicant: true); var appId = Guid.Empty; var headId = Guid.Empty;
        await using (var db = Context())
        {
            var apps = new EfCoreApplicationRepository(db); var docs = new EfCoreDocumentRepository(db); var placements = new EfCoreDocumentPlacementRepository(db);
            var metadata = new EfCoreRegionalMetadataStore(db); var imports = new EfCoreApplicationImportStore(db, apps, docs, placements, metadata);
            var snapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new FdaEctd322StandardsProfileProvider());
            var result = await new ApplicationImportService(apps, imports, fixture, snapshots).ImportAsync(new(fixture.Root, "us-fda-ectd-3.2.2", "Request sponsor"));
            Assert.Equal(1, result.ImportedSequenceCount); appId = result.ApplicationId; headId = (await metadata.GetHeadAsync(appId, "0000"))!.Id;
        }
        await using var read = Context(); var readApps = new EfCoreApplicationRepository(read); var readDocs = new EfCoreDocumentRepository(read);
        var readPlacements = new EfCoreDocumentPlacementRepository(read); var readMetadata = new EfCoreRegionalMetadataStore(read);
        var readImports = new EfCoreApplicationImportStore(read, readApps, readDocs, readPlacements, readMetadata);
        var readSnapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(read), new FdaEctd322StandardsProfileProvider());
        var package = await Builder(readApps, readDocs, readPlacements, new EfCoreCtdNodeRepository(read), new EfCoreWorkspaceRevisionStore(read), readImports,
            readMetadata, readSnapshots, fixture).BuildAsync(new(appId, "0000"));
        Assert.Equal(headId, package.RegionalMetadata!.Id); Assert.Equal(1, package.WorkspaceRevision);
        Assert.Equal(2, package.Module1Leaves.Count); Assert.Single(package.Module1Leaves.Select(item => item.DocumentId).Distinct());
        await VerifyOutputAndReimportAsync(package, readSnapshots, "009876543", "source-submission");
    }

    private static EctdPackageModelBuilder Builder(IApplicationRepository apps, IDocumentRepository docs, IDocumentPlacementRepository placements,
        ICtdNodeRepository nodes, IWorkspaceRevisionStore revisions, IApplicationImportStore imports, IRegionalMetadataStore metadata, StandardsSnapshotService snapshots, ImportAcceptanceFixture fixture)
        => new(apps, placements, docs, new FdaEctd322StandardsProfileProvider(), new DocumentStorageBoundary(fixture), nodes, revisions, imports,
            regionalMetadataStore: metadata, standardsSnapshots: snapshots);

    private static async Task VerifyOutputAndReimportAsync(EctdSequencePackage package, StandardsSnapshotService snapshots, string applicantId, string submissionId)
    {
        var original = package.RegionalMetadata!; original.Verify();
        var xml = new UsRegionalXmlWriter().Write(package); var profile = StandardsSnapshotService.Profile(await snapshots.GetAsync(original.StandardsSnapshotId));
        new EctdXmlValidator().Validate(new(xml.RelativePath, xml.XmlContent), profile);
        Assert.Equal(applicantId, xml.Document.Descendants("id").Single().Value); Assert.Equal(submissionId, xml.Document.Descendants("submission-id").Single().Value);
        Assert.Equal("987654", xml.Document.Descendants("application-number").Single().Value);
        Assert.Equal(ExpectedValues1, xml.Document.Descendants("applicant-contact-name").Select(item => item.Value));
        Assert.Equal(ExpectedValues2, xml.Document.Descendants("email").Select(item => item.Value));
        Assert.Equal(ExpectedValues3, xml.Document.Descendants("form").Select(item => item.Attribute("form-type")!.Value));
        Assert.Equal(ExpectedValues4, xml.Document.Descendants("form").SelectMany(item => item.Elements("leaf")).Select(item => item.Attribute("ID")!.Value));
        Assert.Equal(ExpectedValues5, xml.Document.Descendants("form").SelectMany(item => item.Elements("leaf")).Select(item => item.Attribute(System.Xml.Linq.XNamespace.Xml + "lang")?.Value));
        var emitted = new UsRegionalBackboneWriter(new UsRegionalXmlWriter()).WriteRegionalBackbones(package);
        var index = new IchIndexXmlWriter().Write(EctdBackboneComposition.AttachRegionalFiles(package, emitted));
        var root = Path.GetFullPath(".artifacts/p606-independent/reimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"0000/m1/us"));
        await File.WriteAllTextAsync(Path.Combine(root,"0000/index.xml"), index.XmlContent);
        await File.WriteAllTextAsync(Path.Combine(root,"0000",xml.RelativePath), xml.XmlContent);
        foreach (var file in package.PublishedFiles)
        {
            var target = Path.Combine(root,"0000",file.Href); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file.SourcePath,target);
        }
        var apps = new InMemoryApplicationRepository(); var docs = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(apps); var metadata = new InMemoryRegionalMetadataStore();
        var imports = new InMemoryApplicationImportStore(apps,docs,placements,new(revisions,placements),metadata);
        var restored = await new ApplicationImportService(apps,imports,new ImportAcceptanceFixture(root,Encoding.UTF8.GetBytes(xml.XmlContent)),snapshots)
            .ImportAsync(new(root,"us-fda-ectd-3.2.2","Unrelated request sponsor"));
        Assert.Equal(1,restored.ImportedSequenceCount); Assert.Equal(2,restored.ImportedPlacementCount);
        var head = (await metadata.GetHeadAsync(restored.ApplicationId,"0000"))!;
        Assert.Equal(applicantId,head.Content!.Us!.ApplicantIdentifier); Assert.Equal(submissionId,head.Content.Us.SubmissionIdentifier);
        Assert.Equal(ExpectedValues6,head.DocumentContexts.Select(item=>item.FormType));
        Assert.Equal(ExpectedValues7,head.DocumentContexts.Select(item=>item.Language));
        Assert.Equal(2,head.Content.Us.Contacts.Count); Assert.Equal(2,head.Content.Us.Contacts[0].Emails.Count);
        Assert.Equal(original.Digest,package.RegionalMetadata!.Digest);
    }
    private static RAToolsDbContext Context() => new(new DbContextOptionsBuilder<RAToolsDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES") ?? throw new InvalidOperationException("Dedicated PostgreSQL required.")).Options);
    private static async Task<ImportAcceptanceFixture> CreateAsync(bool validApplicant = false)
    {
        var root=Path.GetFullPath(".artifacts/p606-independent/source-"+Guid.NewGuid().ToString("N"));var region=Path.Combine(root,"0000/m1/us");Directory.CreateDirectory(region);
        // Independently hand-written synthetic US3.3 shape, not an external FDA submission.
        const string xml="""
            <fda-regional:fda-regional xmlns:fda-regional="http://www.ich.org/fda" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.3">
            <admin><applicant-info><id>source-applicant</id><company-name>Source company</company-name><submission-description>Source description</submission-description><applicant-contacts>
            <applicant-contact><applicant-contact-name applicant-contact-type="regulatory">First source contact</applicant-contact-name><telephones><telephone telephone-number-type="office">5551000</telephone><telephone telephone-number-type="fax">5551001</telephone></telephones><emails><email>first@example.test</email><email>alternate@example.test</email></emails></applicant-contact>
            <applicant-contact><applicant-contact-name applicant-contact-type="technical">Second source contact</applicant-contact-name><telephones><telephone telephone-number-type="mobile">5551002</telephone></telephones><emails><email>second@example.test</email></emails></applicant-contact>
            </applicant-contacts></applicant-info><application-set><application application-containing-files="true"><application-information><application-number application-type="nda">987654</application-number></application-information><submission-information><submission-id submission-type="original-application">source-submission</submission-id><sequence-number submission-sub-type="application">0000</sequence-number></submission-information></application></application-set></admin>
            <m1-regional><m1-1-forms><form form-type="356h"><leaf ID="source-form-a" operation="new" xml:lang="fr" xlink:href="shared.txt"><title>First form</title></leaf></form><form form-type="1571"><leaf ID="source-form-b" operation="new" xml:lang="de" xlink:href="shared.txt"><title>Second form</title></leaf></form></m1-1-forms></m1-regional>
            </fda-regional:fda-regional>
            """;
        var bytes=Encoding.UTF8.GetBytes(validApplicant ? xml.Replace("source-applicant", "009876543", StringComparison.Ordinal) : xml);await File.WriteAllBytesAsync(Path.Combine(region,"us-regional.xml"),bytes);
        await File.WriteAllTextAsync(Path.Combine(root,"0000/index.xml"),"<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" dtd-version=\"3.2\"/>");
        await File.WriteAllTextAsync(Path.Combine(region,"shared.txt"),"Shared source for two explicit forms.");return new(root,bytes);
    }
}
