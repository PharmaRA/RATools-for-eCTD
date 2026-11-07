using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Validation;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.Applications;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Publishing.EuRegional;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class EuM1EndToEndTests
{
    private static readonly string[] ExpectedValues1 = new[] { "de","fr" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues2 = { "de","fr" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues3 = { "de","fr" };
    private static readonly string[] ExpectedValues4 = new[] { "EU-DE-EXPLICIT","EU-FR-EXPLICIT" };
    private static readonly string[] ExpectedValues5 = new[] { "EU-DE-EXPLICIT","EU-FR-EXPLICIT" };
    private static readonly string[] ExpectedValues6 = new[] { "TRACK-B","TRACK-A","TRACK-B","FR-TRACK" };
    private static readonly string[] ExpectedValues7 = new[] { "Product B","Product A","Produit" };
    private static readonly string[] ExpectedValues8 = new[] { "active two","active one","substance FR" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues9 = { "de" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues10 = { "de" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues11 = { "spc" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues12 = { "spc","pl" };
    private static readonly string[] ExpectedValues13 = new[] { "pi-de","pi-de-2","pi-fr","pi-fr-2" };
    private static readonly string[] ExpectedValues14 = new[] { "TRACK-B","TRACK-A","TRACK-B" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues15 = { "be","de","de","de","de" };
    private static readonly string[] ExpectedVerifyOutputAndReimportAsyncValues16 = { "be","de","de","fr","fr" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualHttpSaveKeepsBothEnvelopesAndIndependentPiContextsThroughOutputAndReimport(bool samePiContext)
    {
        var fixture = await CreateAsync(samePiContext);
        var apps = new InMemoryApplicationRepository(); var docs = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(apps); var nodes = new InMemoryCtdNodeRepository(revisions, placements); var metadata = new InMemoryRegionalMetadataStore();
        var snapshots = ImportAcceptanceFixture.MemorySnapshots(); var imports = new InMemoryApplicationImportStore(apps, docs, placements, nodes, metadata);
        var importer = new ApplicationImportService(apps, imports, fixture, snapshots);
        var imported = await importer.ImportAsync(new(fixture.Root, "eu-ectd-3.2.2", "Unrelated sponsor"));
        Assert.Equal(1, imported.ImportedSequenceCount); Assert.Equal(5, imported.ImportedPlacementCount);
        var transaction = new InMemoryPersistenceTransaction(); var mutations = new WorkspaceMutationCoordinator(revisions, transaction);
        var regional = new RegionalMetadataService(metadata, apps, nodes, placements, snapshots, mutations, revisions, transaction);
        var shared = new SequencePublishingMetadataService(apps, new EuEctd322StandardsProfileProvider(), mutations, regional);
        await using var host = ImportAcceptanceFixture.Host(fixture.Root, importer, shared); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/applications/{imported.ApplicationId}/sequences/0000/publishing-metadata";
        var dto = (await client.GetFromJsonAsync<SequencePublishingMetadataDto>(path))!;
        var envelopes = dto.Regional!.Content!.Eu!.Envelopes.ToArray();
        Assert.Equal(2, envelopes.Length); envelopes[0] = envelopes[0] with { Applicant = "HTTP DE applicant" };
        var content = dto.Regional.Content with { Eu = new(envelopes) };
        using var saved = await client.PutAsJsonAsync(path, new UpdateSequencePublishingMetadataRequest(dto.ApplicationType, dto.SubmissionType, dto.SubmissionSubtype,
            dto.SequenceDescription, dto.ApplicantName, dto.FormType, dto.ApplicantContactName, dto.ApplicantContactType, dto.Telephone, dto.TelephoneNumberType, dto.Email,
            dto.WorkspaceRevision, new(dto.Regional.StandardsSnapshotId, content, dto.Regional.NodeContexts, dto.Regional.DocumentContexts)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var committed = (await saved.Content.ReadFromJsonAsync<SequencePublishingMetadataDto>())!;
        var package = await Builder(apps, docs, placements, nodes, revisions, imports, metadata, snapshots, fixture).BuildAsync(new(imported.ApplicationId, "0000"));
        Assert.Equal(committed.Regional!.Id, package.RegionalMetadata!.Id);
        await VerifyOutputAndReimportAsync(package, snapshots, "HTTP DE applicant", samePiContext);
    }

    [RequiresPostgresFact]
    public async Task IndependentPostgresReadReproducesNondefaultEnvelopesAndPlacementContexts()
    {
        var fixture = await CreateAsync(); var appId = Guid.Empty; var headId = Guid.Empty;
        await using (var db = Context())
        {
            var apps = new EfCoreApplicationRepository(db); var docs = new EfCoreDocumentRepository(db); var placements = new EfCoreDocumentPlacementRepository(db);
            var metadata = new EfCoreRegionalMetadataStore(db); var imports = new EfCoreApplicationImportStore(db, apps, docs, placements, metadata);
            var snapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new EuEctd322StandardsProfileProvider());
            var imported = await new ApplicationImportService(apps, imports, fixture, snapshots).ImportAsync(new(fixture.Root, "eu-ectd-3.2.2", "Unrelated sponsor"));
            Assert.Equal(1, imported.ImportedSequenceCount); appId = imported.ApplicationId; headId = (await metadata.GetHeadAsync(appId,"0000"))!.Id;
        }
        await using var read = Context(); var readApps = new EfCoreApplicationRepository(read); var readDocs = new EfCoreDocumentRepository(read);
        var readPlacements = new EfCoreDocumentPlacementRepository(read); var readMetadata = new EfCoreRegionalMetadataStore(read);
        var readImports = new EfCoreApplicationImportStore(read, readApps, readDocs, readPlacements, readMetadata);
        var readSnapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(read), new EuEctd322StandardsProfileProvider());
        var package = await Builder(readApps, readDocs, readPlacements, new EfCoreCtdNodeRepository(read), new EfCoreWorkspaceRevisionStore(read), readImports,
            readMetadata, readSnapshots, fixture).BuildAsync(new(appId,"0000"));
        Assert.Equal(headId,package.RegionalMetadata!.Id); Assert.Equal(5,package.Module1Leaves.Count);
        Assert.Equal(2,package.Module1Leaves.Select(item=>item.DocumentId).Distinct().Count());
        await VerifyOutputAndReimportAsync(package,readSnapshots,"Source DE applicant");
    }

    private static EctdPackageModelBuilder Builder(IApplicationRepository apps, IDocumentRepository docs, IDocumentPlacementRepository placements,
        ICtdNodeRepository nodes, IWorkspaceRevisionStore revisions, IApplicationImportStore imports, IRegionalMetadataStore metadata, StandardsSnapshotService snapshots, ImportAcceptanceFixture fixture)
        => new(apps, placements, docs, new EuEctd322StandardsProfileProvider(), new DocumentStorageBoundary(fixture), nodes, revisions, imports,
            regionalMetadataStore: metadata, standardsSnapshots: snapshots);

    private static async Task VerifyOutputAndReimportAsync(EctdSequencePackage package, StandardsSnapshotService snapshots, string firstApplicant, bool samePiContext = false)
    {
        var original = package.RegionalMetadata!; var snapshot = await snapshots.GetAsync(original.StandardsSnapshotId);
        var xml = new EuRegionalXmlWriter().Write(package);
        Directory.CreateDirectory(".artifacts/p607-independent");
        var caseKey=(firstApplicant.StartsWith("HTTP",StringComparison.Ordinal)?"http":"postgres")+(samePiContext?"-same-context":"");
        await File.WriteAllTextAsync($".artifacts/p607-independent/{caseKey}-output.xml",xml.XmlContent);
        await File.WriteAllTextAsync($".artifacts/p607-independent/{caseKey}-output.source.json",JsonSerializer.Serialize(new {
            original.Id,original.Digest,original.StandardsSnapshotId,package.WorkspaceRevision,
            inputLabel="independent handwritten EU3.1.1 synthetic fixture; not official submission",writerOutput=xml.RelativePath }));
        new EctdXmlValidator().Validate(new(xml.RelativePath,xml.XmlContent),StandardsSnapshotService.Profile(snapshot));
        Assert.Equal(ExpectedValues1,xml.Document.Descendants("envelope").Select(item=>item.Attribute("country")!.Value));
        Assert.Equal(new[] { firstApplicant,"Source FR applicant" },xml.Document.Descendants("applicant").Select(item=>item.Value));
        Assert.Equal(ExpectedValues4,xml.Document.Descendants("identifier").Select(item=>item.Value));
        Assert.Equal(ExpectedValues6,xml.Document.Descendants("procedure-tracking").SelectMany(item=>item.Elements("number")).Select(item=>item.Value));
        Assert.Equal(ExpectedValues7,xml.Document.Descendants("invented-name").Select(item=>item.Value));
        Assert.Equal(ExpectedValues8,xml.Document.Descendants("inn").Select(item=>item.Value));
        Assert.Equal("be",xml.Document.Descendants("m1-0-cover").Single().Element("specific")!.Attribute("country")!.Value);
        var pi=xml.Document.Descendants("pi-doc").ToArray();Assert.Equal(samePiContext?1:2,pi.Length);
        Assert.Equal(samePiContext?ExpectedVerifyOutputAndReimportAsyncValues9:ExpectedVerifyOutputAndReimportAsyncValues2,pi.Select(item=>item.Attribute("country")!.Value));
        Assert.Equal(samePiContext?ExpectedVerifyOutputAndReimportAsyncValues10:ExpectedVerifyOutputAndReimportAsyncValues3,pi.Select(item=>item.Attribute(System.Xml.Linq.XNamespace.Xml+"lang")!.Value));
        Assert.Equal(samePiContext?ExpectedVerifyOutputAndReimportAsyncValues11:ExpectedVerifyOutputAndReimportAsyncValues12,pi.Select(item=>item.Attribute("type")!.Value));
        Assert.Equal(ExpectedValues13,pi.SelectMany(item=>item.Elements("leaf")).Select(item=>item.Attribute("ID")!.Value));
        Assert.Equal(5,xml.Document.Descendants("leaf").Count());
        var baselineDigest = new string('c',64);
        var context = PackageStandardsContext.FromSnapshot(snapshot);
        var binding = new ValidationBinding(new string('a',64),baselineDigest,snapshot.RuleProfileAlias,context.Catalog.Digest,
            "eu-output-capture-test",PackageValidationMode.Development,new PackageReadLimits().Digest(),snapshot.Id,DateOnly.FromDateTime(DateTime.UtcNow));
        var run = new PackageValidationRun(package.ApplicationId,package.SequenceNumber,binding,"{}",Guid.NewGuid().ToString("N"),DateTime.UtcNow);
        var baseline = JsonSerializer.SerializeToElement(new { applicationId=package.ApplicationId.ToString("D"),targetSequence=package.SequenceNumber,digest=baselineDigest,entries=Array.Empty<object>() });
        var stored = new StoredPackageValidationRequest(1,Guid.NewGuid(),null,[],default,baseline,new PackageReadLimits());
        var planner = new EctdReleaseCapturePlanner(new CapturedPackage(package),new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new EuRegionalBackboneWriter(new EuRegionalXmlWriter())]),new EuEctd322StandardsProfileProvider(),snapshots);
        var planned = await planner.PlanAsync(new(package.ApplicationId,package.SequenceNumber,package.WorkspaceRevision,run.Id,snapshot.RuleProfileAlias,"3.2.2","3.1.1",baselineDigest,Guid.NewGuid().ToString("N")),run,stored);
        var captured = JsonSerializer.Deserialize<RegionalMetadataRevision>(Assert.Single(planned.Capture.Files,file=>file.RelativePath=="regional-metadata.json").Content!)!;
        captured.Verify();Assert.Equal(original.Id,captured.Id);Assert.Equal(original.Digest,captured.Digest);
        Assert.Equal(2,captured.Content!.Eu!.Envelopes.Count);Assert.Equal(original.DocumentContexts,captured.DocumentContexts);
        var regionalFiles=new EuRegionalBackboneWriter(new EuRegionalXmlWriter()).WriteRegionalBackbones(package);
        var index=new IchIndexXmlWriter().Write(EctdBackboneComposition.AttachRegionalFiles(package,regionalFiles));
        var root=Path.GetFullPath(".artifacts/p607-independent/reimport-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"0000/m1/eu"));
        await File.WriteAllTextAsync(Path.Combine(root,"0000/index.xml"),index.XmlContent);
        await File.WriteAllTextAsync(Path.Combine(root,"0000",xml.RelativePath),xml.XmlContent);
        foreach(var file in package.PublishedFiles)
        {var destination=Path.Combine(root,"0000",file.Href);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file.SourcePath,destination);}
        var apps=new InMemoryApplicationRepository();var docs=new InMemoryDocumentRepository();var placements=new InMemoryDocumentPlacementRepository();
        var revisions=new InMemoryWorkspaceRevisionStore(apps);var metadata=new InMemoryRegionalMetadataStore();var imports=new InMemoryApplicationImportStore(apps,docs,placements,new(revisions,placements),metadata);
        var imported=await new ApplicationImportService(apps,imports,new ImportAcceptanceFixture(root,Encoding.UTF8.GetBytes(xml.XmlContent)),snapshots)
            .ImportAsync(new(root,"eu-ectd-3.2.2","Unrelated reimport sponsor"));
        Assert.Equal(1,imported.ImportedSequenceCount);Assert.Equal(5,imported.ImportedPlacementCount);
        var restored=(await metadata.GetHeadAsync(imported.ApplicationId,"0000"))!;
        Assert.Equal(ExpectedValues5,restored.Content!.Eu!.Envelopes.Select(item=>item.Identifier));
        Assert.Equal(new[] { firstApplicant,"Source FR applicant" },restored.Content.Eu.Envelopes.Select(item=>item.Applicant));
        Assert.Equal(ExpectedValues14,restored.Content.Eu.Envelopes[0].ProcedureTrackingNumbers.Select(item=>item.Value));
        Assert.Equal(samePiContext?ExpectedVerifyOutputAndReimportAsyncValues15:ExpectedVerifyOutputAndReimportAsyncValues16,restored.DocumentContexts.Select(item=>item.Country));
        Assert.Equal(samePiContext?new string?[] { null,"de","de","de","de" }:new string?[] { null,"de","de","fr","fr" },restored.DocumentContexts.Select(item=>item.Language));
        Assert.Equal(original.Digest,package.RegionalMetadata!.Digest);
    }

    private static RAToolsDbContext Context()=>new(new DbContextOptionsBuilder<RAToolsDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES")??throw new InvalidOperationException("Dedicated PostgreSQL required.")).Options);
    private sealed class CapturedPackage(EctdSequencePackage package) : IStandardsBoundPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request,CancellationToken cancellationToken=default)=>Task.FromResult(package);
        public Task<EctdSequencePackage> BuildWithStandardsAsync(BuildEctdPackageRequest request,StandardsProfile profile,CancellationToken cancellationToken=default)=>Task.FromResult(package);
    }
    private static async Task<ImportAcceptanceFixture> CreateAsync(bool samePiContext = false)
    {
        var root=Path.GetFullPath(".artifacts/p607-independent/source-"+Guid.NewGuid().ToString("N"));var regional=Path.Combine(root,"0000/m1/eu");Directory.CreateDirectory(regional);
        // Independent hand-written EU3.1.1 fixture, not an official submission or writer output.
        const string xml="""
            <eu:eu-backbone xmlns:eu="http://europa.eu.int" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.1"><eu-envelope>
            <envelope country="de"><identifier>EU-DE-EXPLICIT</identifier><submission type="maa" mode="single"><number>42</number><procedure-tracking><number>TRACK-B</number><number>TRACK-A</number><number>TRACK-B</number></procedure-tracking></submission><submission-unit type="initial"/><applicant>Source DE applicant</applicant><agency code="DE-BFARM"/><procedure type="national"/><invented-name>Product B</invented-name><invented-name>Product A</invented-name><inn>active two</inn><inn>active one</inn><sequence>0000</sequence><related-sequence>0000</related-sequence><submission-description>DE exact description</submission-description></envelope>
            <envelope country="fr"><identifier>EU-FR-EXPLICIT</identifier><submission type="maa"><procedure-tracking><number>FR-TRACK</number></procedure-tracking></submission><submission-unit type="initial"/><applicant>Source FR applicant</applicant><agency code="FR-ANSM"/><procedure type="national"/><invented-name>Produit</invented-name><inn>substance FR</inn><sequence>0000</sequence><related-sequence>0000</related-sequence><submission-description>FR exact description</submission-description></envelope></eu-envelope>
            <m1-eu><m1-0-cover><specific country="be"><leaf ID="cover-be" operation="new" xlink:href="cover.txt"><title>Belgian cover</title></leaf></specific></m1-0-cover>
            <m1-3-pi><m1-3-1-spc-label-pl><pi-doc country="de" xml:lang="de" type="spc"><leaf ID="pi-de" operation="new" xlink:href="shared.txt"><title>DE SPC</title></leaf><leaf ID="pi-de-2" operation="new" xlink:href="shared.txt"><title>Second DE SPC</title></leaf></pi-doc><pi-doc country="fr" xml:lang="fr" type="pl"><leaf ID="pi-fr" operation="new" xlink:href="shared.txt"><title>FR PL</title></leaf><leaf ID="pi-fr-2" operation="new" xlink:href="shared.txt"><title>Second FR PL</title></leaf></pi-doc></m1-3-1-spc-label-pl></m1-3-pi></m1-eu></eu:eu-backbone>
            """;
        var selectedXml=samePiContext?xml.Replace("<pi-doc country=\"fr\" xml:lang=\"fr\" type=\"pl\">","<pi-doc country=\"de\" xml:lang=\"de\" type=\"spc\">",StringComparison.Ordinal):xml;
        var bytes=Encoding.UTF8.GetBytes(selectedXml);await File.WriteAllBytesAsync(Path.Combine(regional,"eu-regional.xml"),bytes);
        await File.WriteAllTextAsync(Path.Combine(root,"0000/index.xml"),"<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" dtd-version=\"3.2\"/>");
        await File.WriteAllTextAsync(Path.Combine(regional,"cover.txt"),"Independent cover source.");await File.WriteAllTextAsync(Path.Combine(regional,"shared.txt"),"Independent PI source mounted twice.");return new(root,bytes);
    }
}
