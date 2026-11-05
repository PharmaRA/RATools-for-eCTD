using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Api.Middleware;
using RATools.Application.Abstractions.Security;
using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Applications;

public sealed class RegionalImportAcceptanceTests
{
    [Fact]
    public async Task KnownMetadataElementOutsideItsSupportedParentHasAPathDiagnostic()
    {
        var snapshot = await ImportAcceptanceFixture.MemorySnapshots().CaptureAsync("eu-ectd-3.2.2");
        var xml = """
            <eu:eu-backbone xmlns:eu="http://europa.eu.int" dtd-version="3.1">
              <eu-envelope><envelope country="de"><identifier>scoped</identifier><applicant>Supported applicant</applicant><sequence>0000</sequence></envelope></eu-envelope>
              <invented-name>Must not silently disappear</invented-name><m1-eu/>
            </eu:eu-backbone>
            """;
        var parsed = RegionalImportParser.Parse(Encoding.UTF8.GetBytes(xml), true, "0000", snapshot);
        Assert.Empty(parsed.Content.Eu!.Envelopes[0].InventedNames);
        Assert.Contains(parsed.Diagnostics, item => item.Code == "RegionalUnsupportedShape" && item.FieldPath.Contains("/invented-name[1]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnmodifiedOfficialLegacyXmlRetainsRepeatedNamesAndItsActualPiContexts()
    {
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/RegionalImport/ema-national-eu-m1-1.4.xml");
        Assert.Equal("c4e10a0f5cc78d15a2e851dadab83b1eeffea676583db86d3416efdf2cd9019a", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var snapshot = await ImportAcceptanceFixture.MemorySnapshots().CaptureAsync("eu-ectd-3.2.2");
        var parsed = RegionalImportParser.Parse(bytes, true, "0012", snapshot);
        var envelope = Assert.Single(parsed.Content.Eu!.Envelopes);
        Assert.Equal("be", envelope.Country); Assert.Equal("BE-FAMHP", envelope.Agency);
        Assert.Equal("Pharma Unlimited", envelope.Applicant); Assert.Null(envelope.Identifier);
        Assert.Equal(new[] { "Magieke Pil", "Pilulle Magique", "Magiesche Pille" }, envelope.InventedNames.Select(item => item.Value));
        Assert.Equal("2131577", Assert.Single(envelope.ProcedureTrackingNumbers).Value);
        Assert.Equal("0011", Assert.Single(envelope.RelatedSequences).SequenceNumber);
        Assert.Contains(parsed.Diagnostics, item => item.Code == "RegionalUnsupportedVersion" && item.FieldPath.StartsWith("/", StringComparison.Ordinal));
        using var stream = new MemoryStream(bytes);
        var xml = XDocument.Load(stream);
        var leaves = xml.Descendants("pi-doc").SelectMany(element => element.Elements("leaf")).ToArray();
        Assert.Equal(9, leaves.Length);
        var contexts = leaves.Select(leaf => RegionalImportParser.Context(leaf, Guid.NewGuid(), null)).ToArray();
        Assert.Equal(new[] { "fr", "fr", "fr", "nl", "nl", "nl", "de", "de", "de" }, contexts.Select(item => item.Language));
        Assert.Equal(new[] { "spc", "outer", "pl", "spc", "outer", "pl", "spc", "outer", "pl" }, contexts.Select(item => item.ProductInformationType));
        Assert.All(contexts, item => { Assert.Equal("be", item.Country); Assert.Null(item.NodeInstanceId); });
    }

    [Fact]
    public async Task RealHttpImportExposesExactImportedHeadAndUnknownPathsWithoutReturningRawBytes()
    {
        var fixture = await ImportAcceptanceFixture.CreateAsync();
        var apps = new InMemoryApplicationRepository(); var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository(); var revisions = new InMemoryWorkspaceRevisionStore(apps);
        var nodes = new InMemoryCtdNodeRepository(revisions, placements); var metadata = new InMemoryRegionalMetadataStore();
        var snapshots = ImportAcceptanceFixture.MemorySnapshots(); var imports = new InMemoryApplicationImportStore(apps, documents, placements, nodes, metadata);
        var profiles = new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]);
        var transaction = new InMemoryPersistenceTransaction(); var mutations = new WorkspaceMutationCoordinator(revisions, transaction);
        var regional = new RegionalMetadataService(metadata, apps, nodes, placements, snapshots, mutations, revisions, transaction);
        var importer = new ApplicationImportService(apps, imports, fixture, snapshots);
        var shared = new SequencePublishingMetadataService(apps, profiles, mutations, regional);
        await using var host = ImportAcceptanceFixture.Host(fixture.Root, importer, shared);
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var response = await client.PostAsJsonAsync("/api/applications/import", new { workingDirectoryPath = fixture.Root, ectdTemplateKey = "eu-ectd-3.2.2", sponsorName = "Request sponsor" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawResponse = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("originalBytes", rawResponse, StringComparison.OrdinalIgnoreCase);
        var imported = (await response.Content.ReadFromJsonAsync<ApplicationImportResultDto>())!;
        Assert.Equal(1, imported.ImportedSequenceCount); Assert.Equal(0, imported.FailedSequenceCount);
        Assert.Contains(imported.Issues, issue => issue.Code == "RegionalUnknownAttribute" && issue.FieldPath?.StartsWith("/", StringComparison.Ordinal) == true);
        using var read = await client.GetAsync($"/api/applications/{imported.ApplicationId}/sequences/0000/publishing-metadata");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var dto = (await read.Content.ReadFromJsonAsync<SequencePublishingMetadataDto>())!;
        Assert.Equal(1, dto.WorkspaceRevision); Assert.NotNull(dto.Regional);
        Assert.Equal("External applicant", dto.Regional.Content!.Eu!.Envelopes[0].Applicant);
        Assert.Equal(new[] { "First product", "Second product" }, dto.Regional.Content.Eu.Envelopes[0].InventedNames.Select(item => item.Value));
        Assert.NotEmpty(dto.Regional.Diagnostics);
        var evidence = await imports.GetRegionalEvidenceAsync(imported.ApplicationId, "0000");
        ImportAcceptanceFixture.CheckEvidence(evidence, dto.Regional.Id, dto.Regional.StandardsSnapshotId, fixture.RegionalBytes);
        Assert.All(dto.Regional.Provenance, item =>
        {
            Assert.Equal("Imported", item.Origin); Assert.Equal("Unconfirmed", item.Status);
            Assert.Contains(evidence, source => source.Id == item.SourceRevisionId && source.Sha256 == item.SourceDigest);
        });
        var mounted = (await placements.ListAsync()).ToArray(); Assert.Equal(2, mounted.Length);
        Assert.Equal(mounted[0].DocumentId, mounted[1].DocumentId);
        Assert.Equal(new[] { "de", "fr" }, dto.Regional.DocumentContexts.Select(item => item.Language));
        Assert.All(dto.Regional.DocumentContexts, context => Assert.Contains(mounted, item => item.Id == context.PlacementId && item.NodeInstanceId == context.NodeInstanceId));
        using var duplicate = await client.PostAsJsonAsync("/api/applications/import", new { workingDirectoryPath = fixture.Root, ectdTemplateKey = "eu-ectd-3.2.2", sponsorName = "Request sponsor" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Equal(1, (await apps.GetAsync(imported.ApplicationId))!.Sequences.Single().WorkspaceRevision);
    }
}

internal sealed class ImportAcceptanceFixture(string root, byte[] regionalBytes) : IWorkspacePathPolicy
{
    internal string Root { get; } = root;
    internal byte[] RegionalBytes { get; } = regionalBytes;
    public IReadOnlyCollection<string> GetAllowedRoots() => [Root];
    public string EnsureAllowed(string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, Root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Outside retained import fixture.");
        return full;
    }
    internal static StandardsSnapshotService MemorySnapshots() => new(new InMemoryStandardsSnapshotStore(),
        new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
    internal static async Task<ImportAcceptanceFixture> CreateAsync()
    {
        var root = Path.GetFullPath(".artifacts/p605-independent/acceptance-" + Guid.NewGuid().ToString("N"));
        var regional = Path.Combine(root, "0000/m1/eu"); Directory.CreateDirectory(regional);
        // Independent hand-written synthetic input, not generated by a publisher.
        // UTF-16, CRLF and unknown attributes intentionally catch normalized-XML archival.
        var text = "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" + """
            <eu:eu-backbone xmlns:eu="http://europa.eu.int" xmlns:xlink="http://www.w3.org/1999/xlink" dtd-version="3.1">
              <eu-envelope><envelope country="de" outside-profile="retain exactly"><identifier>source-identifier</identifier><submission type="maa"><procedure-tracking><number>TRACK-2</number><number>TRACK-1</number></procedure-tracking></submission><submission-unit type="initial"/><applicant>External applicant</applicant><agency code="DE-BFARM"/><procedure type="national"/><invented-name>First product</invented-name><invented-name>Second product</invented-name><inn>Explicit active</inn><sequence>0000</sequence><related-sequence>0000</related-sequence><submission-description>Source description</submission-description></envelope></eu-envelope>
              <m1-eu><m1-3-pi><m1-3-1-spc-label-pl><pi-doc country="de" xml:lang="de" type="spc"><leaf ID="source-de" operation="new" xlink:href="shared.txt"><title>DE source</title></leaf></pi-doc><pi-doc country="fr" xml:lang="fr" type="pl"><leaf ID="source-fr" operation="new" xlink:href="shared.txt"><title>FR source</title></leaf></pi-doc></m1-3-1-spc-label-pl></m1-3-pi></m1-eu>
            </eu:eu-backbone>
            """;
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(regional, "eu-regional.xml"), bytes);
        await File.WriteAllTextAsync(Path.Combine(root, "0000/index.xml"), "<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" dtd-version=\"3.2\"/>");
        await File.WriteAllTextAsync(Path.Combine(regional, "shared.txt"), "A single source mounted in two language contexts.");
        return new(root, bytes);
    }
    internal static void CheckEvidence(IReadOnlyList<RATools.Domain.RegionalMetadata.ImportedRegionalEvidence> evidence, Guid metadataId, string snapshotId, byte[] expected)
    {
        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, item => { Assert.Equal(metadataId, item.MetadataId); Assert.Equal(snapshotId, item.StandardsSnapshotId); });
        var regional = Assert.Single(evidence, item => item.RelativePath == "m1/eu/eu-regional.xml");
        Assert.Equal(expected, regional.OriginalBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), regional.Sha256);
        var exposed = regional.OriginalBytes; exposed[0] ^= 0xff;
        Assert.Equal(expected, regional.OriginalBytes); regional.Verify();
    }
    internal static WebApplication Host(string root, ApplicationImportService importer, SequencePublishingMetadataService metadata)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(ApplicationsController).Assembly.GetName().Name, EnvironmentName = "Development", ContentRootPath = root });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ApplicationsController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new Selection())).AddControllersAsServices();
        builder.Services.Replace(ServiceDescriptor.Singleton(new ApplicationsController(null!, importer, null!, metadata, null!)));
        var host = builder.Build(); host.UseMiddleware<GlobalExceptionMiddleware>(); host.MapControllers(); host.Urls.Add("http://127.0.0.1:0"); return host;
    }
    private sealed class Selection : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { for (var index = feature.Controllers.Count - 1; index >= 0; index--) if (feature.Controllers[index] != typeof(ApplicationsController).GetTypeInfo()) feature.Controllers.RemoveAt(index); }
    }
}
