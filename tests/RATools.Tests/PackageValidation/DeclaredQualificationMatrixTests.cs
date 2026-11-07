using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Applications;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.Publishing.EuRegional;
using RATools.Tests.Publishing.UsRegional;

namespace RATools.Tests.PackageValidation;

public sealed class DeclaredQualificationMatrixTests
{
    private static readonly JsonSerializerOptions CachedJsonOptions1 = new JsonSerializerOptions { WriteIndented = true };

    private static readonly string MatrixPath = Path.Combine("tests", "RATools.Tests", "Fixtures", "RegionalQualification", "p609-declared-matrix.json");

    [Fact]
    public async Task VersionedDeclaredMatrixExercisesSelectedRegionalRulesAndRetainsPendingRows()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(MatrixPath));
        Assert.Equal("p609-2026-11-06", document.RootElement.GetProperty("matrixVersion").GetString());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var ids = rows.Select(row => row.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("US-M11-FORM", ids); Assert.Contains("US-M12-ADMIN", ids);
        Assert.Contains("EU-DE-ENVELOPE-PI", ids); Assert.Contains("EU-FR-ENVELOPE-PI", ids); Assert.Contains("EU-BE-COVER", ids);
        Assert.Contains("US-M115-PROMOTIONAL", ids); Assert.Contains("EU-OTHER-COUNTRY", ids); Assert.Contains("EU-EMPTY-COVER", ids);
        Assert.Contains("US-FORMAL-EXTERNAL", ids); Assert.Contains("EU-FORMAL-EXTERNAL", ids); Assert.Contains("EU-OTHER-PROCEDURE", ids);

        var receipts = new List<object>();
        var receiptDirectory = Path.Combine(".artifacts", "p609-declared-matrix", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(receiptDirectory);
        foreach (var row in rows.Where(row => row.GetProperty("trial").GetString() == "Pass"))
        {
            var template = row.GetProperty("templateKey").GetString()!;
            var alias = row.GetProperty("ruleProfileAlias").GetString()!;
            var isUs = template == "us-fda-ectd-3.2.2";
            var provider = new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]);
            var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), provider);
            var snapshot = await snapshots.CaptureAsync(template);
            Assert.Equal(alias, snapshot.RuleProfileAlias);
            var country = row.GetProperty("country").GetString()!.ToLowerInvariant();
            var language = row.GetProperty("language").GetString()!;
            var section = row.GetProperty("section").GetString()!;
            var operation = row.GetProperty("operation").GetString()!;
            var application = row.GetProperty("application").GetString()!;
            var procedure = row.GetProperty("procedure").GetString()!;
            var leaf = isUs ? ScopedUsRegionalWriterTests.Leaf(1) with { CtdSection = section, Operation = operation }
                : ScopedEuRegionalWriterTests.Leaf(1, section) with { Operation = operation };
            var package = isUs
                ? ScopedUsRegionalWriterTests.Package([leaf], documents: [new(leaf.PlacementId, leaf.NodeInstanceId, null, language, null, section == "m1.1" ? "1571" : null)])
                : ScopedEuRegionalWriterTests.Package([leaf], content: new(1, "EU", Eu: new([ScopedEuRegionalWriterTests.Envelope(country) with { Procedure = procedure, SubmissionType = application }])),
                    documents: [new(leaf.PlacementId, null, country, language, section == "m1.3.1" ? "spc" : null, null)], emptyCover: section != "m1.0");
            var content = package.RegionalMetadata!.Content!;
            if (isUs) content = content with { Us = content.Us! with { ApplicationType = application } };
            var captured = RegionalMetadataRevision.Create(Guid.NewGuid(), package.ApplicationId, "0001", 1, package.WorkspaceRevision,
                snapshot.Id, content, null, package.RegionalMetadata.NodeContexts, package.RegionalMetadata.DocumentContexts,
                null, null, [], [], null, null, DateTime.UtcNow);
            package = package with { RegionalMetadata = captured };
            var output = isUs ? new UsRegionalXmlWriter().Write(package) : null;
            var euOutput = isUs ? null : new EuRegionalXmlWriter().Write(package);
            var xml = output?.XmlContent ?? euOutput!.XmlContent;
            var path = output?.RelativePath ?? euOutput!.RelativePath;
            new EctdXmlValidator().Validate(new BackboneGeneratedFile(path, xml), provider.GetProfile(template));
            var parsed = RegionalImportParser.Parse(Encoding.UTF8.GetBytes(xml), !isUs, "0001", snapshot);
            if (isUs)
            {
                Assert.Equal(application, parsed.Content.Us!.ApplicationType);
                Assert.Equal(section == "m1.1" ? "1571" : null, XDocument.Parse(xml).Descendants("form").SingleOrDefault()?.Attribute("form-type")?.Value);
            }
            else
            {
                Assert.Equal(country, Assert.Single(parsed.Content.Eu!.Envelopes).Country);
                Assert.Equal(procedure, parsed.Content.Eu.Envelopes.Single().Procedure);
                Assert.Equal(application, parsed.Content.Eu.Envelopes.Single().SubmissionType);
                var element = XDocument.Parse(xml).Descendants(section == "m1.0" ? "specific" : "pi-doc").Single(item => item.Elements("leaf").Any(leafXml => leafXml.Attribute("ID")?.Value == leaf.LeafId));
                Assert.Equal(country, element.Attribute("country")!.Value);
                if (section == "m1.3.1") Assert.Equal(language, element.Attribute(XNamespace.Xml + "lang")!.Value);
            }
            var inspection = await PackageRegionalInspectionTests.Inspect(xml, alias, context: PackageStandardsContext.FromSnapshot(snapshot));
            var required = isUs ? new[] { "US-ADMIN-METADATA", "US-FORM-ATTRIBUTES", "US-SUBMISSION-SCOPE" }
                : ["EU-ENVELOPE-METADATA", "EU-PI-CONTEXT", "EU-RELATED-SEQUENCE"];
            foreach (var id in required) Assert.Equal(CheckStatus.Pass, Assert.Single(inspection.Checks, check => check.RuleId == id).Status);
            Assert.False(inspection.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
            var reasons = snapshot.Evaluate(new(application, procedure, row.GetProperty("country").GetString()!, language, section, operation), DateOnly.FromDateTime(snapshot.AcquiredUtc), template);
            Assert.Contains("EvidenceIncomplete: ExternalComparison", reasons);
            Assert.Contains(reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
            var rowId = row.GetProperty("id").GetString()!;
            await File.WriteAllTextAsync(Path.Combine(receiptDirectory, rowId + ".xml"), xml);
            await File.WriteAllTextAsync(Path.Combine(receiptDirectory, rowId + ".snapshot.json"), JsonSerializer.Serialize(snapshot));
            receipts.Add(new { Row = row.GetProperty("id").GetString(), TemplateKey = template, RuleProfileAlias = alias, StandardsSnapshotId = snapshot.Id,
                InputSha256 = Hash(Encoding.UTF8.GetBytes(xml)), snapshot.Descriptor.Evidence, inspection.RulesDigest, inspection.EngineVersion,
                FormalReasons = reasons, RegionalRevisionDigest = captured.Digest });
        }
        var promoRow = rows.Single(row => row.GetProperty("id").GetString() == "US-M115-PROMOTIONAL");
        var promotional = await PackageRegionalInspectionTests.Inspect(PackageRegionalInspectionTests.Us().Replace("</m1-regional>", "<" + promoRow.GetProperty("section").GetString()!.Replace('.', '-') + "-promotional-material /></m1-regional>", StringComparison.Ordinal), promoRow.GetProperty("ruleProfileAlias").GetString()!);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(promotional.Checks, check => check.RuleId == "US-SUBMISSION-SCOPE").Status);
        foreach (var row in rows.Where(row => row.GetProperty("id").GetString() is "EU-OTHER-COUNTRY" or "EU-OTHER-PROCEDURE"))
        {
            var outside = await PackageRegionalInspectionTests.Inspect(PackageRegionalInspectionTests.Eu()
                .Replace("country=\"de\"", "country=\"" + row.GetProperty("country").GetString()!.ToLowerInvariant() + "\"", StringComparison.Ordinal)
                .Replace("procedure type=\"national\"", "procedure type=\"" + row.GetProperty("procedure").GetString() + "\"", StringComparison.Ordinal), row.GetProperty("ruleProfileAlias").GetString()!);
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(outside.Checks, check => check.RuleId == "EU-ENVELOPE-METADATA").Status);
            Assert.False(outside.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
        }
        var emptyCover = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(ScopedEuRegionalWriterTests.Package(emptyCover: false)));
        Assert.Contains("m1.0.country", emptyCover.Message, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(receiptDirectory, "receipt.json"), JsonSerializer.Serialize(new
        { MatrixVersion = "p609-2026-11-06", MatrixSha256 = Hash(await File.ReadAllBytesAsync(MatrixPath)), Runtime = Environment.Version.ToString(),
            TestAssemblySha256 = Hash(await File.ReadAllBytesAsync(typeof(DeclaredQualificationMatrixTests).Assembly.Location)),
            TestSourceSha256 = Hash(await File.ReadAllBytesAsync("tests/RATools.Tests/PackageValidation/DeclaredQualificationMatrixTests.cs")),
            Rows = receipts, Pending = rows.Where(row => row.GetProperty("trial").GetString() != "Pass").ToArray() }, CachedJsonOptions1));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
