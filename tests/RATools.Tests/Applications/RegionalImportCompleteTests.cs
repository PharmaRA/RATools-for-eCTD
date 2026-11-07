using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;
using Xunit;

namespace RATools.Tests.Applications;

// Synthetic, independently hand-written DTD-derived inputs, not official examples.
// Sources: EMA EU M1 3.1.1 eu-envelope.mod/eu-regional.dtd, acquired June 2024;
// https://esubmission.ema.europa.eu/eumodule1/index.htm
// FDA regional 3.3: https://www.fda.gov/ectd
// Retained artifacts only: no IDisposable, deletion, writer-generated expectations.
public sealed class RegionalImportCompleteTests
{
    private static readonly string[] ExpectedValues1 = new[] { "EU-DE-001", "EU-FR-002" };
    private static readonly string[] ExpectedValues2 = new[] { "tracking-b", "tracking-a", "tracking-b" };
    private static readonly string[] ExpectedValues3 = new[] { "Product B", "Product A" };
    private static readonly string[] ExpectedValues4 = new[] { "inn two", "inn one" };
    private static readonly string[] ExpectedValues5 = new[] { "0000", "0000" };
    private static readonly string[] ExpectedValues6 = new[] { "Alice", "Bob" };
    private static readonly string[] ExpectedValues7 = new[] { "business", "mobile" };
    private static readonly string[] ExpectedValues8 = new[] { "+1-111", "+1-222" };
    private static readonly string[] ExpectedValues9 = new[] { "alice1@example.test", "alice2@example.test" };

    [Fact]
    public async Task EuImportRetainsEveryOrderedEnvelopeFieldInTheCurrentRegionalHead()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml);
        var result = await rig.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        var head = await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000");
        Assert.NotNull(head);
        var envelopes = head.Content!.Eu!.Envelopes;
        Assert.Equal(2, envelopes.Count);
        Assert.Equal(ExpectedValues1, envelopes.Select(item => item.Identifier));
        Assert.Equal("de", envelopes[0].Country); Assert.Equal("DE-BFARM", envelopes[0].Agency);
        Assert.Equal("decentralised", envelopes[0].Procedure); Assert.Equal("var-type2", envelopes[0].SubmissionType);
        Assert.Equal("worksharing", envelopes[0].SubmissionMode); Assert.Equal("0007", envelopes[0].SubmissionNumber);
        Assert.Equal("initial", envelopes[0].SubmissionUnit); Assert.Equal("Declared DE applicant", envelopes[0].Applicant);
        Assert.Equal(ExpectedValues2, envelopes[0].ProcedureTrackingNumbers.Select(item => item.Value));
        Assert.Equal(ExpectedValues3, envelopes[0].InventedNames.Select(item => item.Value));
        Assert.Equal(ExpectedValues4, envelopes[0].Inns.Select(item => item.Value));
        Assert.Equal(ExpectedValues5, envelopes[0].RelatedSequences.Select(item => item.SequenceNumber));
        Assert.Equal("DE exact description", envelopes[0].Description);
        Assert.Equal("fr", envelopes[1].Country); Assert.Equal("Declared FR applicant", envelopes[1].Applicant);
        Assert.False(head.IsReady);
        Assert.All(head.Provenance, item => { Assert.Equal("Imported", item.Origin); Assert.Equal("Unconfirmed", item.Status); Assert.NotNull(item.SourceRevisionId); Assert.NotNull(item.SourceDigest); });
        Assert.Equal(1, head.WorkspaceRevision);
        var app = (await rig.Applications.GetAsync(result.ApplicationId))!;
        Assert.Equal(1, app.Sequences.Single().WorkspaceRevision);
        Assert.Equal("Declared DE applicant", app.Sequences.Single().PublishingMetadata!.ApplicantName);
    }

    [Fact]
    public async Task TwoLeavesOfOneFileRetainIndependentExactAncestorContexts()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml);
        var result = await rig.ImportAsync();
        var head = await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"); Assert.NotNull(head);
        var placements = (await rig.Placements.ListAsync()).OrderBy(item => item.LeafId).ToArray();
        Assert.Equal(2, placements.Length); Assert.Equal(placements[0].DocumentId, placements[1].DocumentId);
        var first = head.DocumentContexts.Single(item => item.PlacementId == placements[0].Id);
        var second = head.DocumentContexts.Single(item => item.PlacementId == placements[1].Id);
        Assert.Equal("de", first.Country); Assert.Equal("de", first.Language); Assert.Equal("spc", first.ProductInformationType);
        Assert.Equal("fr", second.Country); Assert.Equal("fr", second.Language); Assert.Equal("pl", second.ProductInformationType);
        Assert.Null(first.NodeInstanceId); Assert.Null(second.NodeInstanceId);
        Assert.Equal("m1/eu/eu-regional.xml", placements[0].ImportedSource!.BackboneRelativePath);
    }

    [Fact]
    public async Task UsImportRetainsOrderedContactsTypedPhonesAndEmailsWithoutApplicationFallbacks()
    {
        var rig = await RegionalImportRig.CreateAsync("US", RegionalImportRig.UsXml);
        var result = await rig.ImportAsync();
        var head = await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"); Assert.NotNull(head);
        var us = head.Content!.Us!;
        Assert.Equal("123456", us.ApplicationIdentifier); Assert.Equal("nda", us.ApplicationType);
        Assert.Equal("Declared US applicant", us.ApplicantName); Assert.Equal("original-application", us.SubmissionType);
        Assert.Equal("application", us.SubmissionSubtype); Assert.Equal("US XML description", us.Description);
        Assert.Equal("356h", us.FormType);
        Assert.Equal(ExpectedValues6, us.Contacts.Select(item => item.Name));
        Assert.Equal(ExpectedValues7, us.Contacts[0].Phones.Select(item => item.Type));
        Assert.Equal(ExpectedValues8, us.Contacts[0].Phones.Select(item => item.Number));
        Assert.Equal(ExpectedValues9, us.Contacts[0].Emails.Select(item => item.Value));
        Assert.Equal("regulatory", us.Contacts[1].Type);
        Assert.Equal("356h", Assert.Single(head.DocumentContexts).FormType);
    }

    [Theory]
    [InlineData("attribute", "RegionalUnknownAttribute")]
    [InlineData("element", "RegionalUnknownElement")]
    [InlineData("version", "RegionalUnsupportedVersion")]
    [InlineData("namespace", "RegionalUnsupportedNamespace")]
    public async Task UnsupportedXmlEvidenceStaysVisibleAsUnqualifiedPathAddressedDiagnostics(string shape, string code)
    {
        var xml = shape switch
        {
            "attribute" => RegionalImportRig.EuXml.Replace("country=\"de\"><identifier>", "country=\"de\" future-attribute=\"keep me\"><identifier>", StringComparison.Ordinal),
            "element" => RegionalImportRig.EuXml.Replace("<identifier>EU-DE-001</identifier>", "<identifier>EU-DE-001</identifier><future-node>raw evidence</future-node>", StringComparison.Ordinal),
            "version" => RegionalImportRig.EuXml.Replace("dtd-version=\"3.1\"", "dtd-version=\"99.0\"", StringComparison.Ordinal),
            _ => RegionalImportRig.EuXml.Replace("http://europa.eu.int", "urn:future-eu", StringComparison.Ordinal)
        };
        var rig = await RegionalImportRig.CreateAsync("EU", xml);
        var result = await rig.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        var head = await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"); Assert.NotNull(head);
        Assert.Contains(head.Diagnostics, item => item.Code == code && item.FieldPath.StartsWith('/'));
        Assert.Contains(result.Issues, item => item.Code == code);
        Assert.False(head.IsReady);
    }

    [Fact]
    public async Task AmbiguousScalarShapeDoesNotPartiallyCommitSequenceOrPlacements()
    {
        var xml = RegionalImportRig.EuXml.Replace("<identifier>EU-DE-001</identifier>", "<identifier>EU-DE-001</identifier><identifier>ambiguous</identifier>", StringComparison.Ordinal);
        var rig = await RegionalImportRig.CreateAsync("EU", xml);
        var result = await rig.ImportAsync();
        Assert.Equal(0, result.ImportedSequenceCount); Assert.Equal(1, result.FailedSequenceCount);
        Assert.Empty(await rig.Placements.ListAsync()); Assert.Empty(await rig.Documents.ListAsync());
        Assert.Null(await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"));
    }

    [Fact]
    public async Task OriginalByteEvidenceSurvivesExactlyAndCannotBeMutatedThroughItsReadArray()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml);
        var path = Path.Combine(rig.Workspace, "0000", "m1", "eu", "eu-regional.xml");
        var original = await File.ReadAllBytesAsync(path);
        var result = await rig.ImportAsync();
        var evidence = await rig.Imports.GetRegionalEvidenceAsync(result.ApplicationId, "0000");
        Assert.Equal(2, evidence.Count);
        var regional = evidence.Single(item => item.RelativePath == "m1/eu/eu-regional.xml");
        Assert.Equal(original, regional.OriginalBytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant(), regional.Sha256);
        var exposed = regional.OriginalBytes; exposed[0] ^= 1;
        Assert.Equal(original, regional.OriginalBytes);
        var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Equal(head.Id, regional.MetadataId); Assert.Equal(head.StandardsSnapshotId, regional.StandardsSnapshotId);
    }

    [Fact]
    public async Task NodeAndPlacementContextBindActualImportedNodeAndSourceEvidence()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml);
        await File.WriteAllTextAsync(Path.Combine(rig.Workspace, "0000", "index.xml"), """
            <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3.org/1999/xlink" xml:lang="it" dtd-version="3.2"><m2-common-technical-document-summaries><m2-2-introduction><leaf ID="ich-italian" operation="new" xlink:href="m1/eu/shared.txt"><title>ICH context</title></leaf></m2-2-introduction></m2-common-technical-document-summaries></ectd:ectd>
            """);
        var result = await rig.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        var placement = (await rig.Placements.ListAsync()).Single(item => item.LeafId == "ich-italian");
        Assert.NotNull(placement.NodeInstanceId);
        var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Contains(head.NodeContexts, item => item.NodeInstanceId == placement.NodeInstanceId && item.Language == "it");
        var context = head.DocumentContexts.Single(item => item.PlacementId == placement.Id);
        Assert.Equal(placement.NodeInstanceId, context.NodeInstanceId); Assert.Equal("it", context.Language);
        var evidence = (await rig.Imports.GetRegionalEvidenceAsync(result.ApplicationId, "0000")).Single(item => item.RelativePath == "index.xml");
        var ordinal = head.DocumentContexts.ToList().FindIndex(item => item.PlacementId == placement.Id);
        Assert.Contains(head.Provenance, item => item.FieldPath == $"documentContexts[{ordinal}].language" && item.SourceRevisionId == evidence.Id && item.SourceDigest == evidence.Sha256);
    }

    [Fact]
    public async Task RegionalAppendFailureRollsBackApplicationPlacementsHeadAndRawEvidence()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml, failAppend: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.ImportAsync());
        Assert.Empty(await rig.Applications.ListAsync()); Assert.Empty(await rig.Placements.ListAsync()); Assert.Empty(await rig.Documents.ListAsync());
        Assert.Empty(await rig.Imports.GetRegionalEvidenceAsync(rig.LastAttemptedApplicationId, "0000"));
        Assert.Null(await rig.Metadata.GetHeadAsync(rig.LastAttemptedApplicationId, "0000"));
    }

    [Fact]
    public async Task IncompleteBusinessInputImportsAsAnUnconfirmedDraftWithoutSponsorDefaults()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml.Replace("<applicant>Declared DE applicant</applicant>", "<applicant/>", StringComparison.Ordinal));
        var result = await rig.ImportAsync(); var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Equal(1, result.ImportedSequenceCount); Assert.Equal("", head.Content!.Eu!.Envelopes[0].Applicant);
        Assert.False(head.IsReady); Assert.Null((await rig.Applications.GetAsync(result.ApplicationId))!.Sequences.Single().PublishingMetadata);
        Assert.Contains(head.Diagnostics, item => item.Code == "Required" && item.FieldPath == "eu.envelopes[0].applicant");
    }

    [Fact]
    public async Task AnUnknownDeclaredNamespaceHasAnExplicitDiagnosticEvenWhenItsPrefixIsUnused()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml.Replace("xmlns:eu=", "xmlns:future=\"urn:future-feature\" xmlns:eu=", StringComparison.Ordinal));
        var result = await rig.ImportAsync(); var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Contains(head.Diagnostics, item => item.Code == "RegionalUnsupportedNamespace" && item.FieldPath.EndsWith("/@xmlns:future", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KnownButUnrepresentedFdaAttributeRetainsAnExplicitPathDiagnostic()
    {
        var rig = await RegionalImportRig.CreateAsync("US", RegionalImportRig.UsXml.Replace("<submission-id submission-type=", "<submission-id supplement-effective-date-type=\"immediate\" submission-type=", StringComparison.Ordinal));
        var result = await rig.ImportAsync(); var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Contains(head.Diagnostics, item => item.Code == "RegionalUnmappedValue" && item.FieldPath.EndsWith("/@supplement-effective-date-type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/admin[1]/application-set[1]/application[1]/@application-containing-files", "true")]
    [InlineData("/m1-regional[1]/m1-15-promotional-material[1]/@promotional-material-audience-type", "professional")]
    [InlineData("/m1-regional[1]/m1-15-promotional-material[1]/m1-15-2-materials[1]/@promotional-material-doc-type", "advertisement")]
    [InlineData("/m1-regional[1]/m1-15-promotional-material[1]/m1-15-2-materials[1]/m1-15-2-1-material[1]/@promotional-material-type", "print")]
    [InlineData("/m1-regional[1]/m1-15-promotional-material[1]/m1-15-2-materials[1]/m1-15-2-1-material[1]/@material-id", "M-201")]
    [InlineData("/m1-regional[1]/m1-15-promotional-material[1]/m1-15-2-materials[1]/m1-15-2-1-material[1]/@issue-date", "2026-09-01")]
    public async Task AllRemainingKnownUnrepresentedFdaAttributesHaveExactEvidenceDiagnostics(string suffix, string originalValue)
    {
        var xml = RegionalImportRig.UsXml.Replace("</fda-regional:fda-regional>", """
            <m1-regional><m1-15-promotional-material promotional-material-audience-type="professional"><m1-15-2-materials promotional-material-doc-type="advertisement"><m1-15-2-1-material promotional-material-type="print" material-id="M-201" issue-date="2026-09-01"/></m1-15-2-materials></m1-15-promotional-material></m1-regional>
            </fda-regional:fda-regional>
            """, StringComparison.Ordinal);
        var rig = await RegionalImportRig.CreateAsync("US", xml);
        var result = await rig.ImportAsync();
        Assert.Equal(1, result.ImportedSequenceCount);
        var path = "/fda-regional[1]" + suffix;
        var head = (await rig.Metadata.GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.Contains(head.Diagnostics, item => item.Code == "RegionalUnmappedValue" && item.FieldPath == path);
        Assert.Contains(result.Issues, item => item.Code == "RegionalUnmappedValue" && item.FieldPath == path);
        var evidence = (await rig.Imports.GetRegionalEvidenceAsync(result.ApplicationId, "0000")).Single(item => item.RelativePath == "m1/us/us-regional.xml");
        var attribute = suffix[(suffix.LastIndexOf('@') + 1)..];
        Assert.Contains(attribute + "=\"" + originalValue + "\"", Encoding.UTF8.GetString(evidence.OriginalBytes));
    }

    [Fact]
    public async Task AggregateXmlBudgetRejectsBeforeTheImportStoreCanWriteAnyBatch()
    {
        var rig = await RegionalImportRig.CreateAsync("EU", RegionalImportRig.EuXml);
        const string root = "<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" dtd-version=\"3.2\"/>";
        var bytes = Encoding.UTF8.GetBytes(root + "<!--" + new string('b', 8 * 1024 * 1024 - root.Length - 7) + "-->");
        Assert.Equal(8 * 1024 * 1024, bytes.Length);
        for (var sequence = 0; sequence < 17; sequence++)
        {
            var directory = Path.Combine(rig.Workspace, sequence.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "index.xml"), bytes);
        }
        var store = new MustNotWriteImportStore();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.ImportWithStoreAsync(store));
        Assert.StartsWith("IMPORT_EVIDENCE_BUDGET_EXCEEDED:", error.Message);
        Assert.Equal(0, store.WriteAttempts);
        Assert.Empty(await rig.Applications.ListAsync());
    }

    private sealed class MustNotWriteImportStore : IApplicationImportStore
    {
        public int WriteAttempts { get; private set; }
        public Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default)
        { WriteAttempts++; throw new InvalidOperationException("STORE_BOUNDARY_REACHED: XML budget was not enforced before persistence."); }
        public Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImportedBackbone>>([]);
    }

    [Theory]
    [InlineData("C:/raw.xml")]
    [InlineData("C:raw.xml")]
    [InlineData("urn:raw.xml")]
    public void RawEvidenceRejectsDriveAndSchemePaths(string relativePath)
    {
        var bytes = new byte[] { 1 };
        Assert.Throws<ArgumentException>(() => new ImportedRegionalEvidence(Guid.NewGuid(), Guid.NewGuid(), "0000", Guid.NewGuid(),
            "sha256:" + new string('a', 64), relativePath, bytes, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()));
    }
}

public sealed class RegionalImportRig
{
    public InMemoryApplicationRepository Applications { get; } = new();
    public InMemoryDocumentRepository Documents { get; } = new();
    public InMemoryDocumentPlacementRepository Placements { get; } = new();
    public InMemoryRegionalMetadataStore Metadata { get; } = new();
    public string Workspace { get; }
    public string Template { get; }
    public StandardsSnapshotService Snapshots { get; }
    public InMemoryApplicationImportStore Imports { get; }
    public ApplicationImportService Service { get; }
    public Guid LastAttemptedApplicationId { get; private set; }
    private RegionalImportRig(string workspace, string region, bool failAppend)
    {
        Workspace = workspace; Template = region == "EU" ? "eu-ectd-3.2.2" : "us-fda-ectd-3.2.2";
        var root = Directory.GetCurrentDirectory();
        Snapshots = new(new InMemoryStandardsSnapshotStore(), new CompositeStandardsProfileProvider(
            [new EuEctd322StandardsProfileProvider(root), new FdaEctd322StandardsProfileProvider(root)]), new() { AssetRootPath = root });
        var revisions = new InMemoryWorkspaceRevisionStore(Applications);
        Imports = new(Applications, Documents, Placements, new(revisions, Placements), failAppend ? new AppendFailureStore(this) : Metadata);
        Service = new(Applications, Imports, new RetainedPathPolicy(workspace), Snapshots);
    }
    public static async Task<RegionalImportRig> CreateAsync(string region, string regionalXml, bool failAppend = false)
    {
        var workspace = Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "p605-retained-import", "sample-" + Guid.NewGuid().ToString("N"));
        var rig = new RegionalImportRig(workspace, region, failAppend);
        var regionalDirectory = Path.Combine(workspace, "0000", "m1", region == "EU" ? "eu" : "us");
        Directory.CreateDirectory(regionalDirectory);
        await File.WriteAllTextAsync(Path.Combine(workspace, "0000", "index.xml"), "<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" dtd-version=\"3.2\"/>");
        await File.WriteAllBytesAsync(Path.Combine(regionalDirectory, region == "EU" ? "eu-regional.xml" : "us-regional.xml"), new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(regionalXml)).ToArray());
        await File.WriteAllTextAsync(Path.Combine(regionalDirectory, "shared.txt"), "independent retained source");
        return rig;
    }
    public Task<RATools.Application.Applications.Dtos.ApplicationImportResultDto> ImportAsync() => Service.ImportAsync(new ImportApplicationRequest(Workspace, Template, "Request sponsor must not become XML fact"));
    public Task<RATools.Application.Applications.Dtos.ApplicationImportResultDto> ImportWithStoreAsync(IApplicationImportStore store) => new ApplicationImportService(Applications, store, new RetainedPathPolicy(Workspace), Snapshots)
        .ImportAsync(new ImportApplicationRequest(Workspace, Template, "Request sponsor must not become XML fact"));
    private sealed class AppendFailureStore(RegionalImportRig rig) : IRegionalMetadataStore
    {
        public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default) => rig.Metadata.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => rig.Metadata.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default) => rig.Metadata.FindLegacyAsync(applicationId, sequenceNumber, legacyDigest, cancellationToken);
        public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
        { rig.LastAttemptedApplicationId = revision.ApplicationId; await rig.Metadata.AppendAsync(revision, expectedHeadId, cancellationToken); throw new InvalidOperationException("Injected failure after staged regional append."); }
    }
    private sealed class RetainedPathPolicy(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        { var full = Path.GetFullPath(path); if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Outside retained fixture."); return full; }
    }
    public const string EuXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <eu:eu-backbone xmlns:eu="http://europa.eu.int" xmlns:xlink="http://www.w3.org/1999/xlink" dtd-version="3.1" xml:lang="en">
          <eu-envelope>
            <envelope country="de"><identifier>EU-DE-001</identifier><submission type="var-type2" mode="worksharing"><number>0007</number><procedure-tracking><number>tracking-b</number><number>tracking-a</number><number>tracking-b</number></procedure-tracking></submission><submission-unit type="initial"/><applicant>Declared DE applicant</applicant><agency code="DE-BFARM"/><procedure type="decentralised"/><invented-name>Product B</invented-name><invented-name>Product A</invented-name><inn>inn two</inn><inn>inn one</inn><sequence>0000</sequence><related-sequence>0000</related-sequence><related-sequence>0000</related-sequence><submission-description>DE exact description</submission-description></envelope>
            <envelope country="fr"><identifier>EU-FR-002</identifier><submission type="maa"><procedure-tracking><number>FR-track</number></procedure-tracking></submission><submission-unit type="initial"/><applicant>Declared FR applicant</applicant><agency code="FR-ANSM"/><procedure type="national"/><invented-name>Produit</invented-name><sequence>0000</sequence><related-sequence>0000</related-sequence><submission-description>FR description</submission-description></envelope>
          </eu-envelope>
          <m1-eu><m1-3-pi><m1-3-1-spc-label-pl><pi-doc country="de" xml:lang="de" type="spc"><leaf ID="leaf-de" operation="new" xlink:href="shared.txt"><title>German SPC</title></leaf></pi-doc><pi-doc country="fr" xml:lang="fr" type="pl"><leaf ID="leaf-fr" operation="new" xlink:href="shared.txt"><title>French PL</title></leaf></pi-doc></m1-3-1-spc-label-pl></m1-3-pi></m1-eu>
        </eu:eu-backbone>
        """;
    public const string UsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <fda-regional:fda-regional xmlns:fda-regional="http://www.ich.org/fda" xmlns:xlink="http://www.w3.org/1999/xlink" dtd-version="3.3" xml:lang="en">
          <admin><applicant-info><id>applicant-id</id><company-name>Declared US applicant</company-name><submission-description>US XML description</submission-description><applicant-contacts>
            <applicant-contact><applicant-contact-name applicant-contact-type="regulatory">Alice</applicant-contact-name><telephones><telephone telephone-number-type="business">+1-111</telephone><telephone telephone-number-type="mobile">+1-222</telephone></telephones><emails><email>alice1@example.test</email><email>alice2@example.test</email></emails></applicant-contact>
            <applicant-contact><applicant-contact-name applicant-contact-type="regulatory">Bob</applicant-contact-name><telephones><telephone telephone-number-type="fax">+1-333</telephone></telephones><emails><email>bob@example.test</email></emails></applicant-contact>
          </applicant-contacts></applicant-info><application-set><application application-containing-files="true"><application-information><application-number application-type="nda">123456</application-number></application-information><submission-information><submission-id submission-type="original-application">1</submission-id><sequence-number submission-sub-type="application">0000</sequence-number><form form-type="356h"><leaf ID="form-leaf" operation="new" xlink:href="shared.txt"><title>Exact form</title></leaf></form></submission-information></application></application-set></admin>
        </fda-regional:fda-regional>
        """;
}
