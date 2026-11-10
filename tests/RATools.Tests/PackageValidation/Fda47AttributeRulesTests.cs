using RATools.Application.Ctd;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace RATools.Tests.PackageValidation;

public sealed class Fda47AttributeRulesTests
{
    private static readonly Lazy<Task<StandardsProfileSnapshot>> Snapshot = new(() => new StandardsSnapshotService(
        new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2"));
    private static readonly string[] Ids = ["1496", "2012", "2013", "2022", "2034"];
    internal static string Valid() => PackageRegionalInspectionTests.Us()
        .Replace("applicant-id", "001234567", StringComparison.Ordinal).Replace("application-id", "123456", StringComparison.Ordinal)
        .Replace(">submission-id<", ">0001<", StringComparison.Ordinal)
        .Replace("=\"regulatory\"", "=\"fdaact1\"", StringComparison.Ordinal).Replace("=\"agent\"", "=\"fdaact3\"", StringComparison.Ordinal)
        .Replace("=\"business\"", "=\"fdatnt1\"", StringComparison.Ordinal).Replace("=\"fax\"", "=\"fdatnt2\"", StringComparison.Ordinal)
        .Replace("=\"ind\"", "=\"fdaat4\"", StringComparison.Ordinal).Replace("=\"original-application\"", "=\"fdast1\"", StringComparison.Ordinal)
        .Replace("=\"original\"", "=\"fdasst3\"", StringComparison.Ordinal)
        .Replace("=\"1571\"", "=\"fdaft1\"", StringComparison.Ordinal).Replace("=\"356h\"", "=\"fdaft2\"", StringComparison.Ordinal);
    private static async Task<PackageDeliveryInspection> Inspect(string xml, PackageStandardsContext? context = null) =>
        await PackageRegionalInspectionTests.Inspect(xml, context: context ?? PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(2026, 11, 10)));
    private static RuleCheckResult Check(PackageDeliveryInspection result, string id) => Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);

    [Fact]
    public async Task OfficialCodesWithCapturedSourcesProduceObservedCoverageWithoutFormalQualification()
    {
        var result = await Inspect(Valid());
        foreach (var id in Ids) Assert.Equal(CheckStatus.Pass, Check(result, id).Status);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
    }

    [Theory]
    [InlineData("fdaat1", false, CheckStatus.Pass)]
    [InlineData("fdaat6", false, CheckStatus.Pass)]
    [InlineData("fdaat7", false, CheckStatus.Fail)]
    [InlineData("fdaat9", false, CheckStatus.Fail)]
    [InlineData("fdaat10", false, CheckStatus.Fail)]
    [InlineData("fdaat8", false, CheckStatus.Fail)]
    [InlineData("fdaat7", true, CheckStatus.Pass)]
    [InlineData("fdaat9", true, CheckStatus.Pass)]
    [InlineData("fdaat10", true, CheckStatus.Pass)]
    [InlineData("fdaat8", true, CheckStatus.Fail)]
    [InlineData("NDA", false, CheckStatus.Fail)]
    [InlineData("fdaat1 ", false, CheckStatus.Fail)]
    public async Task ApplicationDictionaryHonorsPrimaryCrossReferenceAndFdaOnlyRestrictions(string value, bool crossReference, CheckStatus expected)
    {
        var xml = crossReference ? Valid().Replace("</application-information>", "<cross-reference-application-number application-type=\"" + value + "\">654321</cross-reference-application-number></application-information>", StringComparison.Ordinal)
            : Valid().Replace("application-type=\"fdaat4\"", "application-type=\"" + value + "\"", StringComparison.Ordinal);
        Assert.Equal(expected, Check(await Inspect(xml), "1496").Status);
    }

    [Theory]
    [InlineData("applicant-contact-type", "fdaact1", "regulatory")]
    [InlineData("telephone-number-type", "fdatnt1", "business")]
    [InlineData("application-type", "fdaat4", "FDAAT4")]
    [InlineData("submission-type", "fdast1", "Original Application")]
    [InlineData("submission-sub-type", "fdasst3", "fdasst3 ")]
    [InlineData("form-type", "fdaft1", "1571")]
    public async Task DisplayNamesUnknownCodesAndCdataWhitespaceAreNotOfficialValues(string attribute, string original, string wrong)
    {
        var result = await Inspect(Valid().Replace(attribute + "=\"" + original + "\"", attribute + "=\"" + wrong + "\"", StringComparison.Ordinal));
        var check = Check(result, "2013"); Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.Actual == wrong && finding.Location.FieldPath == "@" + attribute && finding.Location.Line > 0);
    }

    [Theory]
    [InlineData("fdaact1", "fdaact4")]
    [InlineData("fdatnt1", "fdatnt3")]
    [InlineData("fdaft1", "fdaft11")]
    public async Task AdditionalActiveOfficialCodesAreAccepted(string original, string replacement) =>
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(Valid().Replace("=\"" + original + "\"", "=\"" + replacement + "\"", StringComparison.Ordinal)), "2013").Status);

    [Theory]
    [InlineData("fdapmt17", CheckStatus.Fail)]
    [InlineData("fdapmt20", CheckStatus.Fail)]
    [InlineData("fdapmt34", CheckStatus.Fail)]
    [InlineData("fdapmt47", CheckStatus.Pass)]
    public async Task PromotionalDictionaryStatusIsRespected(string material, CheckStatus expected)
    {
        var xml = Valid().Replace("</m1-regional>", "<m1-15-promotional-material promotional-material-audience-type=\"fdapmat1\"><m1-15-2-materials promotional-material-doc-type=\"fdapmdt1\"><m1-15-2-1-material promotional-material-type=\"" + material + "\" material-id=\"a\" /></m1-15-2-materials></m1-15-promotional-material></m1-regional>", StringComparison.Ordinal);
        Assert.Equal(expected, Check(await Inspect(xml), "2013").Status);
    }

    [Theory]
    [InlineData("applicant-contact-type", "fdaact1")]
    [InlineData("telephone-number-type", "fdatnt1")]
    [InlineData("application-type", "fdaat4")]
    [InlineData("submission-type", "fdast1")]
    [InlineData("submission-sub-type", "fdasst3")]
    [InlineData("form-type", "fdaft1")]
    public async Task EveryDeclaredRequiredAttributeMustHaveAnExplicitValue(string attribute, string value)
    {
        foreach (var replacement in new[] { "", attribute + "=\" \"" })
        {
            var result = await Inspect(Valid().Replace(attribute + "=\"" + value + "\"", replacement, StringComparison.Ordinal));
            var check = Check(result, "2012"); Assert.Equal(CheckStatus.Fail, check.Status);
            Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@" + attribute);
        }
    }

    [Fact]
    public async Task RequiredXrefAttributesAndOptionalLeafHrefAreDistinguished()
    {
        var leaf = "<leaf ID=\"leaf1\" operation=\"delete\"><title>T</title><link-text><xref /></link-text></leaf>";
        var result = await Inspect(Valid().Replace("<form form-type=\"fdaft1\"/>", "<form form-type=\"fdaft1\">" + leaf + "</form>", StringComparison.Ordinal));
        var check = Check(result, "2012"); Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@xlink:title");
        Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@xlink:href");
        Assert.DoesNotContain(check.Findings, finding => finding.Location.NodePath?.EndsWith("leaf[1]", StringComparison.Ordinal) == true);
    }

    // Expected sets are transcribed independently from FDA M1 2.6 Table 2.
    [Theory]
    [InlineData("fdast1", "1,2,3,4,5,6", "2,3,4,5")]
    [InlineData("fdast2", "1,3", "2,3,4,5")]
    [InlineData("fdast3", "1,2,3", "2,3,4,5")]
    [InlineData("fdast4", "1,2,3", "2,3,4,5")]
    [InlineData("fdast5", "1,2,3,4,5", "4,6")]
    [InlineData("fdast6", "1,2,3,4,5", "4,7")]
    [InlineData("fdast7", "1,3", "1,4")]
    [InlineData("fdast8", "1,2,3,6", "1,4,5")]
    [InlineData("fdast9", "4", "4,6")]
    [InlineData("fdast10", "1,2,3", "4,6")]
    [InlineData("fdast11", "1,2,3", "3,4,5")]
    public async Task EverySubmissionTypeUsesItsOfficialApplicationAndSubtypeMatrix(string type, string applications, string subtypes)
    {
        for (var app = 1; app <= 6; app++)
        {
            var xml = Valid().Replace("=\"fdast1\"", "=\"" + type + "\"", StringComparison.Ordinal).Replace("=\"fdaat4\"", "=\"fdaat" + app + "\"", StringComparison.Ordinal);
            Assert.Equal(applications.Split(',').Contains(app.ToString(System.Globalization.CultureInfo.InvariantCulture)) ? CheckStatus.Pass : CheckStatus.Fail, Check(await Inspect(xml), "2034").Status);
        }
        for (var subtype = 1; subtype <= 7; subtype++)
        {
            var xml = Valid().Replace("=\"fdast1\"", "=\"" + type + "\"", StringComparison.Ordinal)
                .Replace("=\"fdaat4\"", "=\"fdaat" + applications.Split(',')[0] + "\"", StringComparison.Ordinal).Replace("=\"fdasst3\"", "=\"fdasst" + subtype + "\"", StringComparison.Ordinal);
            Assert.Equal(subtypes.Split(',').Contains(subtype.ToString(System.Globalization.CultureInfo.InvariantCulture)) ? CheckStatus.Pass : CheckStatus.Fail, Check(await Inspect(xml), "2022").Status);
        }
    }

    [Theory]
    [InlineData("fdast1", "unknown")]
    [InlineData("fdaat4", "fdaat8")]
    [InlineData("fdasst3", "")]
    public async Task MissingOrUnknownCombinationInputsCannotPass(string original, string value)
    {
        var result = await Inspect(Valid().Replace("=\"" + original + "\"", "=\"" + value + "\"", StringComparison.Ordinal));
        Assert.Equal(CheckStatus.NotEvaluated, Check(result, "2022").Status);
        if (original != "fdasst3") Assert.Equal(CheckStatus.NotEvaluated, Check(result, "2034").Status);
    }

    [Fact]
    public async Task CurrentFilesDoNotSubstituteForMissingCapturedSources()
    {
        var context = new PackageStandardsContext(PackageValidationCatalog.Current, PackageXmlAssets.Current, IchSectionDefinitions.Current, selectionDate: new(2026, 11, 10));
        var result = await Inspect(Valid(), context);
        foreach (var id in Ids) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
    }

    private static string CompleteAttributes() => Valid()
        .Replace("<submission-id submission-type", "<submission-id supplement-effective-date-type=\"fdasedt1\" submission-type", StringComparison.Ordinal)
        .Replace("</application-information>", "<cross-reference-application-number application-type=\"fdaat9\">654321</cross-reference-application-number></application-information>", StringComparison.Ordinal)
        .Replace("<form form-type=\"fdaft1\"/>", "<form form-type=\"fdaft1\"><leaf ID=\"a\" operation=\"delete\"><title>T</title><link-text><xref xlink:title=\"t\" xlink:href=\"x.pdf\"/></link-text></leaf></form>", StringComparison.Ordinal)
        .Replace("</m1-regional>", "<m1-15-promotional-material promotional-material-audience-type=\"fdapmat1\"><m1-15-2-materials promotional-material-doc-type=\"fdapmdt1\"><m1-15-2-1-material promotional-material-type=\"fdapmt47\" material-id=\"free text\"/></m1-15-2-materials></m1-15-promotional-material></m1-regional>", StringComparison.Ordinal);

    [Theory]
    [InlineData("application-type", "fdaat4")]
    [InlineData("submission-type", "fdast1")]
    [InlineData("submission-sub-type", "fdasst3")]
    [InlineData("applicant-contact-type", "fdaact1")]
    [InlineData("telephone-number-type", "fdatnt1")]
    [InlineData("supplement-effective-date-type", "fdasedt1")]
    [InlineData("form-type", "fdaft1")]
    [InlineData("promotional-material-audience-type", "fdapmat1")]
    [InlineData("promotional-material-doc-type", "fdapmdt1")]
    [InlineData("promotional-material-type", "fdapmt47")]
    public async Task EveryDictionaryAcceptsItsCodeAndRejectsUnknownCaseAndBlank(string attribute, string code)
    {
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(CompleteAttributes()), "2013").Status);
        foreach (var wrong in new[] { "unknown", code.ToUpperInvariant(), " ", code + " " })
        {
            var xml = CompleteAttributes().Replace(attribute + "=\"" + code + "\"", attribute + "=\"" + wrong + "\"", StringComparison.Ordinal);
            var result = Check(await Inspect(xml), "2013");
            Assert.Equal(CheckStatus.Fail, result.Status);
            Assert.Contains(result.Findings, finding => finding.Location.FieldPath == "@" + attribute && finding.Actual == wrong);
        }
    }

    [Theory]
    [InlineData("leaf", "ID")]
    [InlineData("leaf", "operation")]
    [InlineData("xref", "xlink:title")]
    [InlineData("xref", "xlink:href")]
    [InlineData("applicant-contact-name", "applicant-contact-type")]
    [InlineData("telephone", "telephone-number-type")]
    [InlineData("application", "application-containing-files")]
    [InlineData("application-number", "application-type")]
    [InlineData("cross-reference-application-number", "application-type")]
    [InlineData("submission-id", "submission-type")]
    [InlineData("sequence-number", "submission-sub-type")]
    [InlineData("form", "form-type")]
    [InlineData("m1-15-promotional-material", "promotional-material-audience-type")]
    [InlineData("m1-15-2-materials", "promotional-material-doc-type")]
    [InlineData("m1-15-2-1-material", "promotional-material-type")]
    [InlineData("m1-15-2-1-material", "material-id")]
    public async Task AllSixteenRequiredRelationshipsAreObserved(string element, string attribute)
    {
        foreach (var blank in new[] { false, true })
        {
            var xml = XDocument.Parse(CompleteAttributes());
            var item = xml.Descendants(element).First();
            var name = attribute.StartsWith("xlink:", StringComparison.Ordinal) ? XName.Get(attribute[6..], "http://www.w3c.org/1999/xlink") : XName.Get(attribute);
            item.SetAttributeValue(name, blank ? " " : null);
            var result = Check(await Inspect(xml.ToString()), "2012");
            Assert.Equal(CheckStatus.Fail, result.Status);
            Assert.Contains(result.Findings, finding => finding.Location.FieldPath == "@" + attribute && finding.Location.NodePath!.Contains("/" + element + "[", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task OptionalAttributesFreeTextAndFixedDefaultsAreNotMissingValues()
    {
        var xml = CompleteAttributes().Replace(" dtd-version=\"3.3\"", "", StringComparison.Ordinal);
        var result = await Inspect(xml);
        Assert.Equal(CheckStatus.Pass, Check(result, "2012").Status);
        Assert.Equal(CheckStatus.Pass, Check(result, "2013").Status);
    }

    [Fact]
    public async Task XlinkLocalNameImpersonationCannotSatisfyRequiredAttributes()
    {
        var xml = CompleteAttributes().Replace("<xref xlink:title=", "<xref xmlns:fake=\"urn:fake\" fake:title=", StringComparison.Ordinal)
            .Replace(" xlink:href=\"x.pdf\"", " fake:href=\"x.pdf\"", StringComparison.Ordinal);
        var check = Check(await Inspect(xml), "2012");
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@xlink:title" && finding.Actual is null);
        Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@xlink:href" && finding.Actual is null);
    }

    [Fact]
    public async Task TokenizedDtdEnumsUseParserNormalizationWhileUnknownValuesFail()
    {
        Assert.Equal(CheckStatus.Pass, Check(await Inspect(CompleteAttributes().Replace("operation=\"delete\"", "operation=\" delete \"", StringComparison.Ordinal)), "2013").Status);
        Assert.Equal(CheckStatus.Fail, Check(await Inspect(CompleteAttributes().Replace("operation=\"delete\"", "operation=\"REMOVE\"", StringComparison.Ordinal)), "2013").Status);
        Assert.Equal(CheckStatus.Fail, Check(await Inspect(CompleteAttributes().Replace("<xref xlink:title", "<xref xlink:show=\"unknown\" xlink:title", StringComparison.Ordinal)), "2013").Status);
    }

    [Theory]
    [InlineData("fda-m1-attribute-policy")]
    [InlineData("official-fda-m1-specification")]
    [InlineData("official-fda-application-types")]
    [InlineData("official-fda-submission-types")]
    [InlineData("official-fda-submission-subtypes")]
    [InlineData("official-fda-applicant-contact-types")]
    [InlineData("official-fda-telephone-types")]
    [InlineData("official-fda-supplement-effective-types")]
    [InlineData("official-fda-form-types")]
    [InlineData("official-fda-promotional-audience-types")]
    [InlineData("official-fda-promotional-doc-types")]
    [InlineData("official-fda-promotional-material-types")]
    public async Task MissingOrSelfConsistentlyChangedCapturedSourcesNeverAuthorizeTheseRules(string key)
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == key);
        foreach (var changed in new[] { false, true })
        {
            var resources = snapshot.Resources.Where(resource => resource.Key != key).ToList();
            if (changed)
            {
                resources.Add(StandardsSnapshotResource.Create(key, original.Category, original.RelativePath, original.MediaType, [.. original.ReadBytes(), 32]));
            }
            // A descriptor still claiming the removed original is rejected at the outer integrity boundary.
            if (snapshot.Descriptor.Provenance.Any(source => source.Sha256 == original.Sha256))
                Assert.Throws<InvalidDataException>(() => StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc));
            var descriptor = snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() };
            var altered = StandardsProfileSnapshot.Create(descriptor, resources, snapshot.AcquiredUtc);
            var result = await Inspect(Valid(), PackageStandardsContext.FromSnapshot(altered, selectionDate: new(2026, 11, 10)));
            foreach (var id in Ids) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
            Assert.Equal(CheckStatus.Pass, Check(result, "2044").Status);
        }
    }

    [Fact]
    public async Task HistoricalCatalogWithoutNewRulesDoesNotAcquireThem()
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == "rules");
        var catalog = JsonNode.Parse(original.ReadBytes())!;
        var rules = catalog["rules"]!.AsArray();
        foreach (var item in rules.Where(item => Ids.Contains(item!["authorityRuleId"]?.GetValue<string>(), StringComparer.Ordinal)).ToArray()) rules.Remove(item);
        var resources = snapshot.Resources.Where(resource => resource.Key is not ("rules" or "fda-m1-attribute-policy")).Append(
            StandardsSnapshotResource.Create("rules", original.Category, original.RelativePath, original.MediaType, Encoding.UTF8.GetBytes(catalog.ToJsonString())));
        var historical = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var result = await Inspect(Valid(), PackageStandardsContext.FromSnapshot(historical, selectionDate: new(2026, 11, 10)));
        Assert.DoesNotContain(result.Checks, check => Ids.Any(id => check.RuleId == "FDA47-" + id));
        Assert.Equal(CheckStatus.Pass, Check(result, "2044").Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("id")]
    [InlineData("kind")]
    [InlineData("version")]
    [InlineData("sha256")]
    [InlineData("verificationStatus")]
    public async Task CatalogMustAuthorizeTheExactDerivedPolicy(string change)
    {
        var snapshot = await Snapshot.Value;
        var original = snapshot.Resources.Single(resource => resource.Key == "rules");
        var catalog = JsonNode.Parse(original.ReadBytes())!;
        var sources = catalog["sources"]!.AsArray();
        var source = sources.Single(item => item!["id"]!.GetValue<string>() == "ratools-fda-m1-attributes-v1")!;
        if (change == "missing") sources.Remove(source);
        else source[change] = change switch
        {
            "kind" => "RegulatorySpecification", "sha256" => new string('a', 64),
            "verificationStatus" => "Unverified", _ => "unsupported"
        };
        var resources = snapshot.Resources.Where(resource => resource.Key != "rules").Append(
            StandardsSnapshotResource.Create("rules", original.Category, original.RelativePath, original.MediaType, Encoding.UTF8.GetBytes(catalog.ToJsonString())));
        var changed = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var result = await Inspect(Valid(), PackageStandardsContext.FromSnapshot(changed, selectionDate: new(2026, 11, 10)));
        foreach (var id in Ids) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
        Assert.Equal(CheckStatus.Pass, Check(result, "2044").Status);
    }

    [Fact]
    public async Task KnownCombinationConflictsSurviveOtherMissingInputs()
    {
        var noSubtype = Valid().Replace(" submission-sub-type=\"fdasst3\"", "", StringComparison.Ordinal).Replace("=\"fdast1\"", "=\"fdast2\"", StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Fail, Check(await Inspect(noSubtype), "2034").Status);
        var noApplication = Valid().Replace(" application-type=\"fdaat4\"", "", StringComparison.Ordinal).Replace("=\"fdasst3\"", "=\"fdasst1\"", StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Fail, Check(await Inspect(noApplication), "2022").Status);
    }

    [Fact]
    public async Task ProjectRuleWithAnAuthorityLabelCannotEnterOfficialPolicyDispatch()
    {
        var original = (await Snapshot.Value).Resources.Single(resource => resource.Key == "rules");
        var catalog = JsonNode.Parse(original.ReadBytes())!;
        catalog["rules"]!.AsArray().Single(item => item!["internalRuleId"]!.GetValue<string>() == "US-ADMIN-METADATA")!["authorityRuleId"] = "1496";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalog.ToJsonString()));
        var context = new PackageStandardsContext(PackageValidationCatalog.Load(stream), PackageXmlAssets.Current, IchSectionDefinitions.Current);
        var baseline = await Inspect(Valid(), PackageStandardsContext.Current);
        var actual = await Inspect(Valid(), context);
        Assert.Equal(baseline.Checks.Single(check => check.RuleId == "US-ADMIN-METADATA").Status,
            actual.Checks.Single(check => check.RuleId == "US-ADMIN-METADATA").Status);
    }

    [Fact]
    public async Task ApplicationsAreNeverJoinedToFillMissingOrAmbiguousMetadata()
    {
        var xml = XDocument.Parse(Valid());
        var first = xml.Descendants("application").Single();
        var second = new XElement(first);
        second.SetAttributeValue("application-containing-files", "false");
        first.Descendants("submission-id").Single().SetAttributeValue("submission-type", null);
        second.Descendants("application-number").Single().SetAttributeValue("application-type", null);
        first.AddAfterSelf(second);
        var result = await Inspect(xml.ToString());
        foreach (var id in new[] { "2022", "2034" }) Assert.Equal(CheckStatus.NotEvaluated, Check(result, id).Status);
        second.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat4");
        second.Descendants("submission-id").Single().SetAttributeValue("submission-type", "fdast2");
        result = await Inspect(xml.ToString());
        Assert.Equal(CheckStatus.Fail, Check(result, "2034").Status);
        Assert.Contains(Check(result, "2034").Findings, finding => finding.CheckStatus == CheckStatus.Fail && finding.Location.NodePath!.Contains("application[2]", StringComparison.Ordinal));
        var ambiguous = Valid().Replace("</submission-information>", "<submission-id submission-type=\"fdast2\">0002</submission-id></submission-information>", StringComparison.Ordinal);
        foreach (var id in new[] { "2022", "2034" }) Assert.Equal(CheckStatus.NotEvaluated, Check(await Inspect(ambiguous), id).Status);
    }

    [Theory]
    [InlineData(2013, 4, 29, CheckStatus.NotApplicable)]
    [InlineData(2013, 4, 30, CheckStatus.Fail)]
    public async Task ApplicationTypeUsesIts2013EffectiveDate(int year, int month, int day, CheckStatus status)
    {
        var context = PackageStandardsContext.FromSnapshot(await Snapshot.Value, selectionDate: new(year, month, day));
        Assert.Equal(status, Check(await Inspect(Valid().Replace("=\"fdaat4\"", "=\"fdaat8\"", StringComparison.Ordinal), context), "1496").Status);
    }
}
