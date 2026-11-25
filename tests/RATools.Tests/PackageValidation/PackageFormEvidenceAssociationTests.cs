using System.Security.Cryptography;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class PackageFormEvidenceAssociationTests
{
    [Theory]
    [InlineData("single", 1)]
    [InlineData("multiple", 1)]
    [InlineData("attachment", 1)]
    [InlineData("alias", 1)]
    [InlineData("missing-target", 0)]
    [InlineData("delete", 0)]
    [InlineData("fragment", 0)]
    [InlineData("query", 0)]
    public async Task ExactLeafReferenceAssociatesFieldsWithoutChoosingAnApplication(string scenario, int expected)
    {
        var fixture = await Fixture(scenario);
        var result = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.True(result.Files.Forms.InventoryComplete);
        Assert.Single(result.Pdf.FormFieldEvidence);
        var group = Assert.Single(result.Files.Forms.Groups, item => item.FormType?.Value == "fdaft4");
        Assert.Equal(scenario == "multiple" ? 2 : 1, group.Applications.Count);
        Assert.Equal(scenario == "attachment" ? 2 : 1, group.Leaves.Count);
        var associations = result.FormEvidenceAssociations;
        Assert.Equal(expected, associations.Count);
        if (expected == 1)
        {
            var association = Assert.Single(associations);
            Assert.Equal(group.NodePath, association.GroupNodePath);
            Assert.Equal("candidate", association.LeafId);
            Assert.Equal(group.Applications, association.CandidateApplications);
            Assert.Equal(Assert.Single(result.Pdf.FormFieldEvidence), association.Fields);
            Assert.Equal("form-leaf-evidence-association-v1", association.AlgorithmVersion);
        }
        Assert.DoesNotContain(result.Checks, check => check.RuleId == "FDA47-5");
    }

    [Theory]
    [InlineData("incomplete-inventory")]
    [InlineData("incomplete-group")]
    [InlineData("dtd")]
    [InlineData("unknown-scope")]
    [InlineData("duplicate-group")]
    [InlineData("duplicate-leaf")]
    [InlineData("wrong-group-path")]
    [InlineData("leaf-location")]
    [InlineData("leaf-id")]
    [InlineData("leaf-operation")]
    [InlineData("reference-missing")]
    [InlineData("reference-duplicate")]
    [InlineData("reference-location")]
    [InlineData("reference-id")]
    [InlineData("reference-value")]
    [InlineData("reference-resolution")]
    [InlineData("reference-kind")]
    [InlineData("reference-existence")]
    [InlineData("document-hash")]
    [InlineData("document-duplicate")]
    [InlineData("template-hash")]
    [InlineData("template-snapshot")]
    [InlineData("template-duplicate")]
    [InlineData("field-value")]
    [InlineData("field-datasets")]
    [InlineData("field-algorithm")]
    [InlineData("field-duplicate")]
    [InlineData("context-without-snapshot")]
    public async Task ConflictingEvidenceCannotCreateAnAssociation(string scenario)
    {
        var fixture = await Fixture("single");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        Assert.Single(delivery.FormEvidenceAssociations);
        var group = delivery.Files.Forms.Groups.Single(item => item.FormType?.Value == "fdaft4");
        var leaf = Assert.Single(group.Leaves);
        var leaves = new List<ParsedPackageLeaf> { scenario switch
        {
            "leaf-location" => leaf with { Location = leaf.Location with { LogicalPath = "0001/index.xml" } },
            "leaf-id" => leaf with { Id = "different" },
            "leaf-operation" => leaf with { Operation = "unknown" },
            _ => leaf
        } };
        if (scenario == "duplicate-leaf") leaves.Add(leaf);
        var replacement = new PackageFormGroup(group.SourceLogicalPath, scenario == "wrong-group-path" ? "/unrelated" : group.NodePath,
            scenario == "unknown-scope" ? "Unknown" : group.Scope, group.FormType, scenario != "incomplete-group",
            scenario == "dtd" ? CheckStatus.NotEvaluated : group.DtdStatus, group.Applications, leaves);
        var groups = delivery.Files.Forms.Groups.Select(item => item == group ? replacement : item).ToList();
        if (scenario == "duplicate-group") groups.Add(replacement);
        var references = delivery.Files.References.ToList();
        var reference = references.Single(item => item.SourceLogicalPath == group.SourceLogicalPath && item.Location.NodePath == leaf.NodePath);
        if (scenario == "reference-missing") references.Remove(reference);
        else if (scenario == "reference-duplicate") references.Add(reference);
        else references[references.IndexOf(reference)] = scenario switch
        {
            "reference-location" => reference with { Location = reference.Location with { LogicalPath = "0001/index.xml" } },
            "reference-id" => reference with { Location = reference.Location with { LeafId = "different" } },
            "reference-value" => reference with { Reference = "other.pdf" },
            "reference-resolution" => reference with { Resolved = reference.Resolved! with { LogicalPath = "0001/index.xml" } },
            "reference-kind" => reference with { Kind = "Stylesheet" },
            "reference-existence" => reference with { Exists = false },
            _ => reference
        };
        var files = new PackageFileInspection(fixture.Input, delivery.Lifecycle, references, delivery.Files.Checks,
            new(scenario != "incomplete-inventory", groups));
        var documents = delivery.Pdf.Documents.ToList(); var document = Assert.Single(documents);
        if (scenario == "document-hash") documents[0] = document with { CapturedSha256 = new string('0', 64) };
        if (scenario == "document-duplicate") documents.Add(document);
        var templates = delivery.Pdf.FormTemplates.ToList(); var template = Assert.Single(templates);
        if (scenario == "template-hash") templates[0] = template with { CapturedSha256 = new string('0', 64) };
        if (scenario == "template-snapshot") templates[0] = template with { StandardsSnapshotId = "different" };
        if (scenario == "template-duplicate") templates.Add(template);
        var fields = delivery.Pdf.FormFieldEvidence.ToList(); var field = Assert.Single(fields);
        if (scenario == "field-value") fields[0] = field with { ApplicationNumber = field.ApplicationNumber with { State = PackageFormRawFieldState.Text, RawText = "999999" } };
        if (scenario == "field-datasets") fields[0] = field with { DatasetsSha256 = new string('0', 64) };
        if (scenario == "field-algorithm") fields[0] = field with { AlgorithmVersion = "different" };
        if (scenario == "field-duplicate") fields.Add(field);
        var pdf = new PackagePdfInspection(files, "association-fixture", documents, delivery.Pdf.Links, delivery.Pdf.Checks, templates, fields);
        var context = scenario == "context-without-snapshot" ? PackageStandardsContext.Current : fixture.Context;
        Assert.Empty(PackageFormEvidenceAssociationReader.Read(fixture.Input, files, pdf, context, CancellationToken.None));
    }

    [Fact]
    public async Task TwoDistinctLeafReferencesRetainTheirOwnLocationsAndAllCandidates()
    {
        var fixture = await Fixture("multiple");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        var group = delivery.Files.Forms.Groups.Single(item => item.FormType?.Value == "fdaft4"); var leaf = Assert.Single(group.Leaves);
        var second = leaf with { NodePath = group.NodePath + "/leaf[2]", Id = "second", Location = leaf.Location with { NodePath = group.NodePath + "/leaf[2]", LeafId = "second" } };
        var reference = delivery.Files.References.Single(item => item.SourceLogicalPath == group.SourceLogicalPath && item.Location.NodePath == leaf.NodePath);
        var groups = delivery.Files.Forms.Groups.Select(item => item == group ? new PackageFormGroup(group.SourceLogicalPath, group.NodePath, group.Scope,
            group.FormType, true, CheckStatus.Pass, group.Applications, [leaf, second]) : item);
        var files = new PackageFileInspection(fixture.Input, delivery.Lifecycle, [.. delivery.Files.References, reference with { Location = second.Location }], delivery.Files.Checks, new(true, groups));
        var result = PackageFormEvidenceAssociationReader.Read(fixture.Input, files, delivery.Pdf, fixture.Context, CancellationToken.None);
        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].LeafNodePath, result[1].LeafNodePath);
        Assert.All(result, item => Assert.Equal(2, item.CandidateApplications.Count));
        Assert.Equal(result[0].Fields, result[1].Fields);
    }

    [Fact]
    public async Task CancellationPropagatesBeforePublishingAssociations()
    {
        var fixture = await Fixture("single");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackageFormEvidenceAssociationReader.Read(fixture.Input, delivery.Files, delivery.Pdf, fixture.Context, cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateBudgetExhaustionPublishesNoPartialAssociation(bool late)
    {
        var fixture = await Fixture("single");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        var groups = delivery.Files.Forms.Groups;
        var ticks = fixture.Input.Packages.Target.Manifest.Files.Count + delivery.Pdf.Documents.Count * 2 + delivery.Pdf.FormTemplates.Count * 2 +
            delivery.Pdf.FormFieldEvidence.Count * 2 + delivery.Files.References.Count + groups.Count * 2 + groups.Sum(group => group.Leaves.Count * 2 +
                group.Applications.Count + group.Applications.Sum(application => application.Numbers.Count));
        var capture = new NoReadInput(fixture.Input.Packages.Target, late ? ticks - 1 : 1);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var input = new PackageDeliveryInput(packages);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [], [], [], []);
        var files = new PackageFileInspection(input, lifecycle, delivery.Files.References, delivery.Files.Checks, delivery.Files.Forms);
        var pdf = new PackagePdfInspection(files, "budget-association-fixture", delivery.Pdf.Documents, delivery.Pdf.Links, delivery.Pdf.Checks,
            delivery.Pdf.FormTemplates, delivery.Pdf.FormFieldEvidence);
        Assert.Empty(PackageFormEvidenceAssociationReader.Read(input, files, pdf, fixture.Context, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 10)]
    public async Task EachLeafApplicationCopyConsumesTheAggregateBudget(int adjustment, int expected)
    {
        var fixture = await Fixture("single");
        var delivery = await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: fixture.Context).InspectAsync(fixture.Input, PackageValidationCatalog.UsProfile);
        var group = delivery.Files.Forms.Groups.Single(item => item.FormType?.Value == "fdaft4");
        var leaf = Assert.Single(group.Leaves); var application = Assert.Single(group.Applications);
        var applications = Enumerable.Range(0, 100).Select(index => new PackageFormApplication(application.NodePath + "/candidate-" + index,
            application.ContainingFiles, application.Numbers)).ToArray();
        var leaves = Enumerable.Range(0, 10).Select(index => leaf with { NodePath = group.NodePath + "/leaf[" + (index + 1) + "]",
            Id = "candidate-" + index, Location = leaf.Location with { NodePath = group.NodePath + "/leaf[" + (index + 1) + "]", LeafId = "candidate-" + index } }).ToArray();
        var expanded = new PackageFormGroup(group.SourceLogicalPath, group.NodePath, group.Scope, group.FormType, true, CheckStatus.Pass, applications, leaves);
        var reference = delivery.Files.References.Single(item => item.SourceLogicalPath == group.SourceLogicalPath && item.Location.NodePath == leaf.NodePath);
        var references = leaves.Select(item => reference with { Location = item.Location }).ToArray();
        var indexingTicks = fixture.Input.Packages.Target.Manifest.Files.Count + delivery.Pdf.Documents.Count * 2 + delivery.Pdf.FormTemplates.Count * 2 +
            delivery.Pdf.FormFieldEvidence.Count * 2 + references.Length + 2 + leaves.Length * 2 + applications.Length + applications.Sum(item => item.Numbers.Count);
        var capture = new NoReadInput(fixture.Input.Packages.Target, indexingTicks + leaves.Length * applications.Length + adjustment);
        var packages = new PackageInputSet(capture, new(capture.Manifest.ApplicationId, "0001", []), []);
        var input = new PackageDeliveryInput(packages);
        var lifecycle = new PackageLifecycleInspection(packages, PackageValidationCatalog.UsProfile, [], [], [], []);
        var files = new PackageFileInspection(input, lifecycle, references, delivery.Files.Checks, new(true, [expanded]));
        var pdf = new PackagePdfInspection(files, "expanded-budget-fixture", delivery.Pdf.Documents, delivery.Pdf.Links, delivery.Pdf.Checks,
            delivery.Pdf.FormTemplates, delivery.Pdf.FormFieldEvidence);
        var result = PackageFormEvidenceAssociationReader.Read(input, files, pdf, fixture.Context, CancellationToken.None);
        Assert.Equal(expected, result.Count);
        Assert.All(result, item => Assert.Equal(100, item.CandidateApplications.Count));
    }

    private sealed class NoReadInput(ICapturedPackageInput original, int budget) : ICapturedPackageInput
    {
        public string SourceId => original.SourceId;
        public PackageSourceKind SourceKind => original.SourceKind;
        public PackageInputManifest Manifest => original.Manifest;
        public string? ArchiveSha256 => null;
        public string InputDigest => original.InputDigest;
        public PackageReadLimits Limits { get; } = new() { MaxXmlNodes = budget };
        public Stream OpenRead(string path) => throw new InvalidOperationException("Association must not reopen files.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Association consumes frozen facts only.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record FixtureData(PackageDeliveryInput Input, PackageStandardsContext Context);
    private static async Task<FixtureData> Fixture(string scenario)
    {
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider(), clock: new Clock()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 26));
        var root = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("Repository root required.");
        var pdf = await File.ReadAllBytesAsync(Path.Combine(root, "reference/publisher/official-sources/2026-11-20-forms/fda-form-2252.pdf"));
        var regional = XDocument.Parse(Fda47AttributeRulesTests.Valid());
        if (scenario == "multiple")
        {
            var first = regional.Descendants("application").Single(); var second = new XElement(first);
            second.SetAttributeValue("application-containing-files", "false");
            second.Descendants("application-number").Single().Value = "654321";
            first.AddAfterSelf(second);
        }
        var href = scenario switch { "missing-target" => "missing.pdf", "alias" => "content%2520name.pdf", "fragment" => "content.pdf#page=1", "query" => "content.pdf?view=1", _ => "content.pdf" };
        var leaf = Leaf("candidate", href, pdf);
        if (scenario == "delete") leaf.SetAttributeValue("operation", "delete");
        var form = new XElement("form", new XAttribute("form-type", "fdaft4"), leaf);
        var additions = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        { ["0001/m1/us/" + (scenario == "alias" ? "content%20name.pdf" : "content.pdf")] = pdf };
        if (scenario == "attachment")
        {
            var attachment = PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]");
            additions["0001/m1/us/attachment.pdf"] = attachment;
            form.Add(Leaf("attachment", "attachment.pdf", attachment));
        }
        regional.Descendants("m1-1-forms").Single().Add(form);
        var capture = new Capture(regional.ToString(), additions);
        return new(new(new(capture, new(capture.Manifest.ApplicationId, "0001", []), [])), context);
    }

    private static XElement Leaf(string id, string href, byte[] bytes) => new("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
        new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), href), new XAttribute("checksum-type", "md5"),
        new XAttribute("checksum", Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant()), new XElement("title", id));
    private sealed class Capture : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files;
        public Capture(string regional, Dictionary<string, byte[]> additions)
        {
            var original = new PackageRegionalInspectionTests.MemoryInput(Guid.NewGuid(), "0001", regional, PackageValidationCatalog.UsProfile);
            _files = original.Manifest.Files.ToDictionary(file => file.LogicalPath, file => { using var stream = original.OpenRead(file.LogicalPath); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }, StringComparer.Ordinal);
            foreach (var addition in additions) _files.Add(addition.Key, addition.Value);
            Manifest = new(original.Manifest.ApplicationId, "0001", _files.Select(file => new PackageInputFile(file.Key, file.Value.Length, Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(file.Value)).ToLowerInvariant())), [], [], []);
        }
        public string SourceId => "fda5-association";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string path) => new MemoryStream(_files[path], false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    }
}
