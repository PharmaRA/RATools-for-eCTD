using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageRuleCatalogTests
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static JsonObject CatalogJson() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(Root, "reference/publisher/package-rules-v1.json")))!.AsObject();

    private static ValidationRuleCatalog Load(JsonNode json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        return PackageValidationCatalog.Load(stream);
    }

    [Fact]
    public void EmbeddedCatalogMatchesIndependentPythonDigestAndPreservesQualificationGaps()
    {
        var catalog = PackageValidationCatalog.Current;
        Assert.Equal(File.ReadAllText(Path.Combine(Root, "reference/publisher/package-rules-v1.sha256")).Trim(), catalog.Digest);
        Assert.Equal(catalog.Digest, Load(CatalogJson()).Digest);
        Assert.All(catalog.Profiles.Values, profile => Assert.False(profile.QualifiedForFinalization));
        Assert.All(catalog.Rules.Values, rule =>
        {
            Assert.Contains(rule.SourceId, catalog.Sources.Keys);
            Assert.All(rule.ProfileSnapshotIds, profile => Assert.Contains(profile, catalog.Profiles.Keys));
            Assert.NotEmpty(rule.SourceSection);
            Assert.NotEmpty(rule.ImplementationNote);
            if (rule.ImplementationStatus == ImplementationStatus.Implemented)
            {
                Assert.NotEmpty(rule.PositiveFixtures);
                Assert.NotEmpty(rule.NegativeFixtures);
            }
        });
        Assert.Null(catalog.Rules["FILE-NAMING"].AuthorityRuleId);
        Assert.NotEqual(ImplementationStatus.Implemented, catalog.Rules["FILE-NAMING"].ImplementationStatus);
        Assert.NotEqual(ImplementationStatus.Implemented, catalog.Rules["PDF-SECURITY"].ImplementationStatus);
        Assert.Equal("Unverified", catalog.Sources["fda-us-m1-3.3"].VerificationStatus);
        Assert.Contains(catalog.ForProfile(PackageValidationCatalog.UsProfile), rule => rule.InternalRuleId == "US-CRITERIA-INVENTORY");
        Assert.DoesNotContain(catalog.ForProfile(PackageValidationCatalog.IchProfile), rule => rule.InternalRuleId == "US-CRITERIA-INVENTORY");
        Assert.Throws<ArgumentException>(() => catalog.ForProfile("guess-from-namespace"));
    }

    [Theory]
    [InlineData("sourceSection")]
    [InlineData("applicableScope")]
    [InlineData("implementationNote")]
    [InlineData("ruleVersion")]
    public void ChangingAnyRulePolicyOrProvenanceChangesTheDigest(string field)
    {
        var json = CatalogJson();
        json["rules"]![0]![field] = "Changed exact value";
        Assert.NotEqual(PackageValidationCatalog.Current.Digest, Load(json).Digest);
    }

    [Theory]
    [InlineData("requiredForReadiness")]
    [InlineData("allowsManualClosure")]
    [InlineData("authorityRuleId")]
    [InlineData("implementationStatus")]
    [InlineData("positiveFixtures")]
    public void MissingPolicyMembersAreNotSilentlyDefaulted(string field)
    {
        var json = CatalogJson();
        json["rules"]![0]!.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Theory]
    [InlineData("implementationStatus", "Magic")]
    [InlineData("applicability", "Guess")]
    [InlineData("severity", "Critical")]
    public void UnknownEnumsAreRejected(string field, string value)
    {
        var json = CatalogJson();
        json["rules"]![0]![field] = value;
        Assert.Throws<JsonException>(() => Load(json));
    }

    [Fact]
    public void UnknownFieldsNumericEnumsAndDuplicatePropertiesAreRejected()
    {
        var json = CatalogJson();
        json["rules"]![0]!["unexpected"] = true;
        Assert.Throws<JsonException>(() => Load(json));
        json["rules"]![0]!.AsObject().Remove("unexpected");
        json["rules"]![0]!["implementationStatus"] = 0;
        Assert.Throws<JsonException>(() => Load(json));
        using var duplicate = new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}"));
        Assert.Throws<ArgumentException>(() => PackageValidationCatalog.Load(duplicate));
    }

    [Theory]
    [InlineData("sourceId")]
    [InlineData("profileSnapshotIds")]
    public void ReferencesMustResolve(string field)
    {
        var json = CatalogJson();
        json["rules"]![0]![field] = field == "sourceId" ? JsonValue.Create("unknown") : new JsonArray("unknown");
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Theory]
    [InlineData("sources")]
    [InlineData("profiles")]
    [InlineData("rules")]
    public void DuplicateCatalogIdentitiesAreRejected(string inventory)
    {
        var json = CatalogJson();
        json[inventory]!.AsArray().Add(json[inventory]![0]!.DeepClone());
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Fact]
    public void ImplementedRequiresPositiveAndNegativeEvidenceAndManualRequiresExplicitPolicy()
    {
        var json = CatalogJson();
        json["rules"]![0]!["implementationStatus"] = "Implemented";
        json["rules"]![0]!["negativeFixtures"] = new JsonArray();
        Assert.Throws<ArgumentException>(() => Load(json));
        json["rules"]![0]!["implementationStatus"] = "Manual";
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Fact]
    public void VerifiedSourcesRequireHashAndUnverifiedSourcesPreventProfileQualification()
    {
        var json = CatalogJson();
        json["sources"]![0]!["sha256"] = null;
        Assert.Throws<ArgumentException>(() => Load(json));
        json = CatalogJson();
        json["profiles"]![1]!["qualifiedForFinalization"] = true;
        Assert.Throws<ArgumentException>(() => Load(json));
    }

    [Fact]
    public void RuleListsAreDefensivelyCopiedAndCannotMutateCatalogPolicy()
    {
        var profiles = new[] { "test-profile" };
        var positive = new[] { "positive" };
        var descriptor = new ValidationRuleDescriptor("RULE", null, "source", "section", "title", "category", "1",
            profiles, "scope", RuleApplicability.Always, ValidationSeverity.Error, true, false,
            ImplementationStatus.Implemented, "qualified", positive, ["negative"], [], []);
        profiles[0] = "different";
        positive[0] = "removed";
        Assert.Equal("test-profile", Assert.Single(descriptor.ProfileSnapshotIds));
        Assert.Equal("positive", Assert.Single(descriptor.PositiveFixtures));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)descriptor.PositiveFixtures)[0] = "mutated");
    }
}
