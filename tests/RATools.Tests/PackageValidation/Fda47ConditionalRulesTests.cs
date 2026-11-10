using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using System.Xml.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace RATools.Tests.PackageValidation;

public sealed class Fda47ConditionalRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() => new StandardsSnapshotService(
        new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));
    private static string Xml(string type = "fdast2", string subtype = "fdasst3", string? effective = null)
    {
        var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
        xml.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat1");
        var submission = xml.Descendants("submission-id").Single();
        submission.SetAttributeValue("submission-type", type);
        submission.SetAttributeValue("supplement-effective-date-type", effective);
        xml.Descendants("sequence-number").Single().SetAttributeValue("submission-sub-type", subtype);
        return xml.ToString();
    }
    private static async Task<PackageDeliveryInspection> Inspect(string xml, PackageStandardsContext? context = null) =>
        await PackageRegionalInspectionTests.Inspect(xml, context: context ?? PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 10)));
    private static RuleCheckResult Check(PackageDeliveryInspection result, string id) => Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);

    // Independently transcribed from M1 2.6 Tables 2/3, including REMS added to those tables.
    [Theory]
    [InlineData("fdast2")]
    [InlineData("fdast3")]
    [InlineData("fdast4")]
    [InlineData("fdast11")]
    public async Task EachSupplementApplicationRequiresAnExplicitEffectiveDateType(string type)
    {
        foreach (var missing in new string?[] { null, "", " " })
        {
            var result = await Inspect(Xml(type, effective: missing));
            var check = Check(result, "2041"); Assert.Equal(CheckStatus.Fail, check.Status);
            Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@supplement-effective-date-type" &&
                finding.Location.NodePath!.EndsWith("submission-id[1]", StringComparison.Ordinal) && finding.Location.Line > 0 && finding.Actual == missing);
            Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
        }
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(Xml(type, effective: "fdasedt1")), "2041").Status);
    }

    [Theory]
    [InlineData("fdast1", "fdasst3")]
    [InlineData("fdast2", "fdasst2")]
    [InlineData("fdast3", "fdasst4")]
    [InlineData("fdast4", "fdasst5")]
    [InlineData("fdast11", "fdasst4")]
    public async Task NonSupplementApplicationsAndOtherUnitsDoNotRequireAnEffectiveDate(string type, string subtype) =>
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(Xml(type, subtype)), "2041").Status);

    [Theory]
    [InlineData("unknown", "fdasst3")]
    [InlineData("", "fdasst3")]
    [InlineData("fdast2", "unknown")]
    [InlineData("fdast2", "")]
    public async Task UnknownApplicabilityCannotProducePassingCoverage(string type, string subtype) =>
        Assert.Equal(CheckStatus.NotEvaluated, Check(await Inspect(Xml(type, subtype)), "2041").Status);

    [Fact]
    public async Task PresenceAndDictionaryValidityRemainSeparateChecks()
    {
        var result = await Inspect(Xml(effective: "invalid"));
        Assert.Equal(CheckStatus.Pass, Check(result, "2041").Status);
        Assert.Equal(CheckStatus.Fail, Check(result, "2013").Status);
    }

    [Theory]
    [InlineData("fdaft1", CheckStatus.Pass)]
    [InlineData("fdaft2", CheckStatus.Pass)]
    [InlineData("fdaft3", CheckStatus.Fail)]
    [InlineData("fdaft4", CheckStatus.Fail)]
    [InlineData("fdaft5", CheckStatus.Fail)]
    [InlineData("fdaft6", CheckStatus.Fail)]
    [InlineData("fdaft7", CheckStatus.Fail)]
    [InlineData("fdaft8", CheckStatus.Fail)]
    [InlineData("fdaft9", CheckStatus.Fail)]
    [InlineData("fdaft10", CheckStatus.Fail)]
    [InlineData("fdaft11", CheckStatus.Fail)]
    public async Task Only1571And356hMayBeReferencedInAdministrativeSubmissionInformation(string form, CheckStatus expected)
    {
        var xml = XDocument.Parse(Xml(effective: "fdasedt1"));
        xml.Descendants("submission-information").Single().Elements("form").Remove();
        xml.Descendants("submission-information").Single().Add(new XElement("form", new XAttribute("form-type", form)));
        var check = Check(await Inspect(xml.ToString()), "2045"); Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Contains(check.Findings, finding => finding.Actual == form &&
            finding.Location.FieldPath == "@form-type" && finding.Location.NodePath!.Contains("submission-information", StringComparison.Ordinal));
        xml.Descendants("submission-information").Single().Elements("form").Remove();
        xml.Descendants("m1-1-forms").Single().Add(new XElement("form", new XAttribute("form-type", form)));
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(xml.ToString()), "2045").Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingAdminFormTypeCannotEstablishLocationEligibility(string? form)
    {
        var xml = XDocument.Parse(Xml(effective: "fdasedt1"));
        xml.Descendants("submission-information").Single().Add(new XElement("form", form is null ? null : new XAttribute("form-type", form)));
        Assert.Equal(CheckStatus.NotEvaluated, Check(await Inspect(xml.ToString()), "2045").Status);
    }

    [Fact]
    public async Task EachApplicationOwnsItsConditionAndKnownFailureSurvivesAnAmbiguousNeighbour()
    {
        var xml = XDocument.Parse(Xml(effective: "fdasedt1"));
        var first = xml.Descendants("application").Single(); var second = new XElement(first);
        second.SetAttributeValue("application-containing-files", "false");
        second.Descendants("submission-id").Single().SetAttributeValue("supplement-effective-date-type", null);
        first.AddAfterSelf(second);
        first.Add(new XElement(first.Element("submission-information")!));
        var check = Check(await Inspect(xml.ToString()), "2041"); Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.CheckStatus == CheckStatus.Fail && finding.Location.NodePath!.Contains("application[2]", StringComparison.Ordinal));
        Assert.Contains(check.Findings, finding => finding.CheckStatus == CheckStatus.NotEvaluated);
    }

    [Theory]
    [InlineData(2015, 6, 14, CheckStatus.NotApplicable)]
    [InlineData(2015, 6, 15, CheckStatus.Fail)]
    public async Task EffectiveDateIsBoundToTheSelectedSnapshotDate(int year, int month, int day, CheckStatus expected)
    {
        var context = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(year, month, day));
        Assert.Equal(expected, Check(await Inspect(Xml(), context), "2041").Status);
    }

    [Fact]
    public async Task RepositoryResourcesCannotReplaceAnAbsentCapturedConditionalPolicy()
    {
        var context = new PackageStandardsContext(PackageValidationCatalog.Current, PackageXmlAssets.Current,
            RATools.Application.Ctd.IchSectionDefinitions.Current, selectionDate: new(2026, 11, 10));
        var result = await Inspect(Xml(), context);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "2041").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "2045").Status);
    }

    [Theory]
    [InlineData("fda-m1-conditional-policy")]
    [InlineData("official-fda-m1-specification")]
    [InlineData("xml-us-dtd")]
    [InlineData("official-fda-submission-types")]
    [InlineData("official-fda-submission-subtypes")]
    [InlineData("official-fda-supplement-effective-types")]
    [InlineData("official-fda-form-types")]
    public async Task MissingOrChangedOriginalsNeverAuthorizeConditionalCoverage(string key)
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == key);
        foreach (var changed in new[] { false, true })
        {
            var resources = snapshot.Resources.Where(resource => resource.Key != key).ToList();
            if (changed) resources.Add(StandardsSnapshotResource.Create(key, original.Category, original.RelativePath, original.MediaType, [.. original.ReadBytes(), 32]));
            var descriptor = snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() };
            if (key == "xml-us-dtd")
            {
                // The XML asset prerequisite rejects absent/mismatched DTD bytes before rule dispatch.
                Assert.Throws<InvalidDataException>(() => PackageStandardsContext.FromSnapshot(StandardsProfileSnapshot.Create(descriptor, resources, snapshot.AcquiredUtc), selectionDate: new(2026, 11, 10)));
                continue;
            }
            var altered = StandardsProfileSnapshot.Create(descriptor, resources, snapshot.AcquiredUtc);
            var result = await Inspect(Xml(), PackageStandardsContext.FromSnapshot(altered, selectionDate: new(2026, 11, 10)));
            foreach (var id in new[] { "2041", "2045" }) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("id")]
    [InlineData("kind")]
    [InlineData("version")]
    [InlineData("sha256")]
    [InlineData("verificationStatus")]
    public async Task ExactCatalogAuthorizationIsRequiredForTheConditionalPolicy(string change)
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == "rules");
        var catalog = JsonNode.Parse(original.ReadBytes())!;
        var sources = catalog["sources"]!.AsArray();
        var source = sources.Single(item => item!["id"]!.GetValue<string>() == "ratools-fda-m1-conditional-v1")!;
        if (change == "missing") sources.Remove(source);
        else source[change] = change switch { "kind" => "RegulatorySpecification", "sha256" => new string('a', 64), "verificationStatus" => "Unverified", _ => "unsupported" };
        var resources = snapshot.Resources.Where(resource => resource.Key != "rules").Append(StandardsSnapshotResource.Create(
            "rules", original.Category, original.RelativePath, original.MediaType, Encoding.UTF8.GetBytes(catalog.ToJsonString())));
        var changed = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var result = await Inspect(Xml(), PackageStandardsContext.FromSnapshot(changed, selectionDate: new(2026, 11, 10)));
        foreach (var id in new[] { "2041", "2045" }) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
    }

    [Fact]
    public async Task OldCatalogDoesNotAcquireConditionalChecks()
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == "rules");
        var catalog = JsonNode.Parse(original.ReadBytes())!;
        var rules = catalog["rules"]!.AsArray();
        foreach (var rule in rules.Where(rule => rule!["authorityRuleId"]?.GetValue<string>() is "2041" or "2045").ToArray()) rules.Remove(rule);
        var resources = snapshot.Resources.Where(resource => resource.Key is not ("rules" or "fda-m1-conditional-policy")).Append(
            StandardsSnapshotResource.Create("rules", original.Category, original.RelativePath, original.MediaType, Encoding.UTF8.GetBytes(catalog.ToJsonString())));
        var old = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var result = await Inspect(Xml(), PackageStandardsContext.FromSnapshot(old, selectionDate: new(2026, 11, 10)));
        Assert.DoesNotContain(result.Checks, check => check.RuleId is "FDA47-2041" or "FDA47-2045");
        Assert.Equal(CheckStatus.Pass, Check(result, "2013").Status);
    }
}
