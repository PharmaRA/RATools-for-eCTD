using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RATools.Application.Ctd;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Application.Validation.Rules;
using RATools.Application.Validation.Rules.Pdf;
using RATools.Application.Validation.Requests;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageRegionalInspectionTests
{
    [Fact]
    public async Task CompleteUsAdminAndTwoDistinctFormsHaveObservedScopedCoverage()
    {
        var result = await Inspect(Us());
        foreach (var id in new[] { "US-ADMIN-METADATA", "US-FORM-ATTRIBUTES", "US-SUBMISSION-SCOPE" })
            Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == id).Status);
        Assert.DoesNotContain(result.Checks, item => item.RuleId.StartsWith("EU-", StringComparison.Ordinal));
        Assert.Equal(2, result.Lifecycle.XmlInspections.Single().Backbones.Single(item => item.Kind == BackboneKind.UsRegional).Elements.Count(item => item.LocalName == "form"));
        var report = result.CreateReport(PackageValidationMode.Formal);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(report.Coverage, item => item.RuleId == "US-CRITERIA-INVENTORY").CheckStatus);
        Assert.False(report.IsReadyForFinalization);
        Assert.Contains(PackageRegionalInspector.RegionalEngineVersion, result.EngineVersion, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<id>applicant-id</id>", "<id> </id>")]
    [InlineData("<company-name>Applicant</company-name>", "<company-name />")]
    [InlineData("<submission-description>Description</submission-description>", "")]
    [InlineData("applicant-contact-type=\"regulatory\"", "applicant-contact-type=\" \"")]
    [InlineData("telephone-number-type=\"business\"", "telephone-number-type=\"\"")]
    [InlineData("<email>a@example.invalid</email>", "<email />")]
    [InlineData("submission-type=\"original-application\"", "submission-type=\"\"")]
    [InlineData("<sequence-number submission-sub-type=\"original\">0001</sequence-number>", "<sequence-number submission-sub-type=\"original\">0000</sequence-number>")]
    public async Task DtdValidBlankOrWrongSequenceUsMetadataIsLocatedAndBlocks(string before, string after)
    {
        var result = await Inspect(Us().Replace(before, after, StringComparison.Ordinal));
        var check = Assert.Single(result.Checks, item => item.RuleId == "US-ADMIN-METADATA");
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, item => item.Location.LogicalPath == "0001/m1/us/us-regional.xml" && item.Location.Line > 0 && item.Location.NodePath is not null);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
    }

    [Theory]
    [InlineData("form-type=\"1571\"", "form-type=\" \"", "US-FORM-ATTRIBUTES", CheckStatus.Fail)]
    [InlineData("<m1-1-forms>", "<m1-1-forms form-type=\"1571\">", "US-FORM-ATTRIBUTES", CheckStatus.Fail)]
    [InlineData("</m1-regional>", "<m1-15-promotional-material /></m1-regional>", "US-SUBMISSION-SCOPE", CheckStatus.NotEvaluated)]
    public async Task UsFormAndUnselectedPromotionalScopeDoNotSilentlyPass(string before, string after, string rule, CheckStatus expected)
    {
        var result = await Inspect(Us().Replace(before, after, StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == rule).Status);
    }

    [Fact]
    public async Task CompleteEuMultivaluesAndPiHaveObservedCoverageWithoutUsChecks()
    {
        var result = await Inspect(Eu(), PackageValidationCatalog.EuProfile);
        foreach (var id in new[] { "EU-ENVELOPE-METADATA", "EU-PI-CONTEXT", "EU-RELATED-SEQUENCE" })
            Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == id).Status);
        Assert.DoesNotContain(result.Checks, item => item.RuleId.StartsWith("US-", StringComparison.Ordinal));
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
    }

    [Theory]
    [InlineData("fr", "FR-ANSM")]
    [InlineData("be", "BE-FAMHP")]
    public async Task SelectedFrenchAndBelgianNationalEnvelopePoliciesHavePositiveCoverage(string country, string agency)
    {
        var xml = Eu().Replace("country=\"de\"", "country=\"" + country + "\"", StringComparison.Ordinal).Replace("DE-BFARM", agency, StringComparison.Ordinal);
        var result = await Inspect(xml, PackageValidationCatalog.EuProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == "EU-ENVELOPE-METADATA").Status);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == "EU-PI-CONTEXT").Status);
    }

    [Fact]
    public async Task TheSameUsFormContentRequiresItsDeclaredUsProfile()
    {
        var us = await Inspect(Us());
        var eu = await Inspect(Us(), PackageValidationCatalog.EuProfile);
        Assert.Equal(CheckStatus.Pass, Assert.Single(us.Checks, item => item.RuleId == "US-FORM-ATTRIBUTES").Status);
        Assert.DoesNotContain(eu.Checks, item => item.RuleId == "US-FORM-ATTRIBUTES");
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(eu.Checks, item => item.RuleId == "EU-PI-CONTEXT").Status);
        await Assert.ThrowsAsync<ArgumentException>(() => Inspect(Us(), "unknown-profile"));
    }

    [Theory]
    [InlineData("<identifier>business-id</identifier>", "<identifier />", "EU-ENVELOPE-METADATA")]
    [InlineData("<invented-name>Second</invented-name>", "<invented-name />", "EU-ENVELOPE-METADATA")]
    [InlineData("<number>track-two</number>", "<number />", "EU-ENVELOPE-METADATA")]
    [InlineData("code=\"DE-BFARM\"", "code=\"FR-ANSM\"", "EU-ENVELOPE-METADATA")]
    [InlineData("country=\"de\" xml:lang=\"de\" type=\"spc\"", "country=\"invalid\" xml:lang=\"de\" type=\"spc\"", "EU-PI-CONTEXT")]
    [InlineData("xml:lang=\"de\"", "xml:lang=\"xx\"", "EU-PI-CONTEXT")]
    [InlineData("type=\"spc\"", "type=\"unknown\"", "EU-PI-CONTEXT")]
    [InlineData("<m1-3-pi>", "<m1-3-pi form-type=\"1571\">", "EU-PI-CONTEXT")]
    public async Task EuRequiredValuesCombinationsAndPiContextsFailAtTheActualParsedElement(string before, string after, string id)
    {
        var result = await Inspect(Eu().Replace(before, after, StringComparison.Ordinal), PackageValidationCatalog.EuProfile);
        var check = Assert.Single(result.Checks, item => item.RuleId == id);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, item => item.Location.LogicalPath == "0001/m1/eu/eu-regional.xml" && item.Location.NodePath is not null);
    }

    [Theory]
    [InlineData("initial", "0000", false, CheckStatus.Fail)]
    [InlineData("reformat", "0001", false, CheckStatus.Pass)]
    [InlineData("response", "0001", false, CheckStatus.Fail)]
    [InlineData("response", "0000", false, CheckStatus.NotEvaluated)]
    [InlineData("response", "0000", true, CheckStatus.Pass)]
    [InlineData("response", "0002", false, CheckStatus.Fail)]
    [InlineData("response", "foreign/0000", false, CheckStatus.Fail)]
    [InlineData("response", "00x0", false, CheckStatus.Fail)]
    public async Task EuRelatedSequenceUsesActualSelectedSameApplicationHistory(string unit, string related, bool selectHistory, CheckStatus expected)
    {
        var result = await Inspect(Eu(unit, related), PackageValidationCatalog.EuProfile, selectHistory: selectHistory);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == "EU-RELATED-SEQUENCE").Status);
    }

    [Theory]
    [InlineData("foreign-business", CheckStatus.Fail)]
    [InlineData("", CheckStatus.NotEvaluated)]
    public async Task SelectedHistoryMustMatchActualEnvelopeBusinessIdentity(string historicalIdentity, CheckStatus expected)
    {
        var history = Eu("initial", "0000").Replace(">0001<", ">0000<", StringComparison.Ordinal).Replace("business-id", historicalIdentity, StringComparison.Ordinal);
        var result = await Inspect(Eu("response", "0000"), PackageValidationCatalog.EuProfile, selectHistory: true, historyXml: history);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == "EU-RELATED-SEQUENCE").Status);
    }

    [Theory]
    [InlineData("0001", CheckStatus.Fail)]
    [InlineData("00x0", CheckStatus.Fail)]
    [InlineData("", CheckStatus.NotEvaluated)]
    [InlineData("0000", CheckStatus.Pass)]
    public async Task RelatedHistoryEnvelopeMustDeclareItsActualSelectedSequenceEvenUnderAnOldCatalog(string declaredSequence, CheckStatus expected)
    {
        var history = Eu("initial", "0000").Replace("<sequence>0001</sequence>", "<sequence>" + declaredSequence + "</sequence>", StringComparison.Ordinal);
        var result = await Inspect(Eu("response", "0000"), PackageValidationCatalog.EuProfile, selectHistory: true,
            context: ArchivedHistoryContext(), historyXml: history);
        var parsedHistory = result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones).Single(backbone => backbone.Kind == BackboneKind.EuRegional && backbone.LogicalPath.StartsWith("0000/", StringComparison.Ordinal));
        Assert.True(parsedHistory.ReadComplete);
        Assert.Equal(CheckStatus.Pass, parsedHistory.DtdStatus);
        var check = Assert.Single(result.Checks, item => item.RuleId == "EU-RELATED-SEQUENCE");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Contains(check.Findings, finding => finding.Code == "EU_RELATED_HISTORY_SEQUENCE_MISMATCH");
        if (expected == CheckStatus.NotEvaluated) Assert.Contains(check.Findings, finding => finding.Code == "EU_RELATED_HISTORY_SEQUENCE_UNKNOWN");
    }

    [Theory]
    [InlineData("0001", "0000", "foreign-business", CheckStatus.Fail)]
    [InlineData("0000", "0001", "foreign-business", CheckStatus.Pass)]
    [InlineData("0000", "0000", "business-id", CheckStatus.Pass)]
    [InlineData("0000", "", "business-id", CheckStatus.NotEvaluated)]
    public async Task RelatedHistoryIdentityAndSequenceComeFromTheSameActualEnvelope(string firstSequence, string secondSequence, string secondIdentity, CheckStatus expected)
    {
        var first = Eu("initial", "0000").Replace("<sequence>0001</sequence>", "<sequence>" + firstSequence + "</sequence>", StringComparison.Ordinal);
        var second = Eu("initial", "0000").Replace("business-id", secondIdentity, StringComparison.Ordinal)
            .Replace("country=\"de\"", "country=\"fr\"", StringComparison.Ordinal).Replace("DE-BFARM", "FR-ANSM", StringComparison.Ordinal)
            .Replace("<sequence>0001</sequence>", "<sequence>" + secondSequence + "</sequence>", StringComparison.Ordinal);
        var start = second.IndexOf("<envelope country=", StringComparison.Ordinal);
        var end = second.IndexOf("</envelope>", start, StringComparison.Ordinal) + "</envelope>".Length;
        var history = first.Replace("</eu-envelope>", second[start..end] + "</eu-envelope>", StringComparison.Ordinal);
        var result = await Inspect(Eu("response", "0000"), PackageValidationCatalog.EuProfile, selectHistory: true,
            context: ArchivedHistoryContext(), historyXml: history);
        Assert.Equal(CheckStatus.Pass, result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones).Single(backbone => backbone.Kind == BackboneKind.EuRegional && backbone.LogicalPath.StartsWith("0000/", StringComparison.Ordinal)).DtdStatus);
        Assert.Equal(expected, Assert.Single(result.Checks, item => item.RuleId == "EU-RELATED-SEQUENCE").Status);
    }

    [Theory]
    [InlineData("us-fda-ectd-3.2.2", "4.5")]
    [InlineData("eu-ectd-3.2.2", "8.2")]
    public void SharedPdfAlgorithmUsesDeclaredProfileVersionPolicy(string key, string criteria)
    {
        var directory = Path.Combine(".artifacts", "p608-pdf-policy-fixtures");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllText(source, "Retained policy-input fixture; facts are supplied at the PDF inspector boundary.");
        var leaf = new EctdLeaf(Guid.NewGuid(), Guid.NewGuid(), "document", "0001", "m5.1", "m5", "new", "Document", "m5/document.pdf", "document.pdf", "application/pdf", source, 1, new('a', 64), new('a', 32), null);
        var profile = Profile(key, criteria);
        var package = new EctdSequencePackage(Guid.NewGuid(), "business", "0001", key, "3.2.2", "3.3", profile.BackboneXml!,
            new("business", "Applicant", "Region", key, "ind"), new("0001", "initial", null, "Description", "Applicant", null),
            new("id", "Applicant", "Description", "Contact", "regulatory", "111", "business", "a@example.invalid", "ind", "initial", "original", null),
            [], [leaf], []);
        var provider = new RegionalEctdRuleSetProvider([new PdfComplianceRule(new PdfFacts())]);
        var ruleSet = provider.GetRuleSet(profile);
        var rule = Assert.Single(ruleSet.Rules);
        var finding = Assert.Single(rule.Evaluate(new(profile, new ValidateSequenceRequest(package.ApplicationId, "0001"), package, null)), item => item.RuleId == "PDF_VERSION_UNQUALIFIED");
        Assert.Equal(EctdValidationSeverity.High, finding.Severity);
        var mapping = Assert.Single(ruleSet.Mappings);
        Assert.Equal("1.4", mapping.Parameters["AcceptedPdfVersions"]);
        Assert.Equal(RuleSourceKind.ProjectPolicy, mapping.SourceKind);
        Assert.Contains("ICH eCTD 3.2.2 Appendix 7", mapping.SourceLocator, StringComparison.Ordinal);
        Assert.Equal(key, mapping.ProfileKey);
        Assert.Equal(criteria, mapping.CriteriaVersion);
        var relaxed = mapping with
        {
            Parameters = new Dictionary<string, string> { ["AcceptedPdfVersions"] = "1.4,1.7", ["MaxRecommendedBookmarkDepth"] = "2" },
            FindingSeverities = new Dictionary<string, EctdValidationSeverity> { ["PDF_VERSION_UNQUALIFIED"] = EctdValidationSeverity.Low }
        };
        Assert.DoesNotContain(((PdfComplianceRule)rule).WithMapping(relaxed).Evaluate(new(profile, new(package.ApplicationId, "0001"), package, null)), item => item.RuleId == "PDF_VERSION_UNQUALIFIED");
        var changedSeverity = mapping with { FindingSeverities = new Dictionary<string, EctdValidationSeverity> { ["PDF_VERSION_UNQUALIFIED"] = EctdValidationSeverity.Low } };
        Assert.Equal(EctdValidationSeverity.Low, Assert.Single(((PdfComplianceRule)rule).WithMapping(changedSeverity).Evaluate(new(profile, new(package.ApplicationId, "0001"), package, null)), item => item.RuleId == "PDF_VERSION_UNQUALIFIED").Severity);
    }

    [Fact]
    public async Task IndependentSharedPdfFindingsUseTheSelectedCatalogSeverity()
    {
        var json = JsonNode.Parse(PackageValidationCatalog.ExportSnapshot().GetRawText())!;
        var rule = json["rules"]!.AsArray().Single(item => item!["internalRuleId"]!.GetValue<string>() == "PDF-READABLE")!;
        rule["severity"] = "Warning";
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var catalog = PackageValidationCatalog.Load(bytes);
        var context = new PackageStandardsContext(catalog, PackageXmlAssets.Current, IchSectionDefinitions.Current);
        var result = await Inspect(Us(), context: context, invalidPdf: true);
        var findings = Assert.Single(result.Checks, item => item.RuleId == "PDF-READABLE").Findings;
        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Equal(ValidationSeverity.Warning, finding.Severity));
    }

    [Fact]
    public async Task PartialRegionalXmlAndMissingRegionalBackboneCannotBePassing()
    {
        var partial = await Inspect(Us()[..^40]);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(partial.Checks, item => item.RuleId == "US-ADMIN-METADATA").Status);
        var missing = await Inspect(null);
        Assert.Equal(CheckStatus.Fail, Assert.Single(missing.Checks, item => item.RuleId == "US-ADMIN-METADATA").Status);
    }

    [Fact]
    public async Task LegacyCapturedCatalogReplaysWithoutInjectingNewRulesAndNewEngineCannotReuseOldIdentity()
    {
        using var bytes = File.OpenRead("tests/RATools.Tests/Fixtures/Publisher/p608-archived-package-rules-e087d3c.json");
        Assert.Equal("f75b4947a988f38c881a7f8d4d34d78f71925eb1f4d87078a1f168dd49a587f2", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        bytes.Position = 0;
        var catalog = PackageValidationCatalog.Load(bytes);
        var context = new PackageStandardsContext(catalog, PackageXmlAssets.Current, IchSectionDefinitions.Current);
        var result = await Inspect(Us(), context: context);
        Assert.DoesNotContain(result.Checks, check => NewIds.Contains(check.RuleId, StringComparer.Ordinal));
        Assert.DoesNotContain(result.CreateReport(PackageValidationMode.Development).Coverage, check => NewIds.Contains(check.RuleId, StringComparer.Ordinal));
        Assert.NotEqual(PackageValidationCatalog.Current.Digest, result.RulesDigest);
        Assert.NotEqual("package-delivery-v3;package-lifecycle-inspector-v1;package-file-inspector-v2;package-pdf-validator-v1;pdfpig-0.1.15", result.EngineVersion);
    }

    [Fact]
    public async Task HistoricalSequenceUsesItsOwnArchivedCatalogWithoutInjectingCurrentRegionalRules()
    {
        using var bytes = File.OpenRead("tests/RATools.Tests/Fixtures/Publisher/p608-archived-package-rules-e087d3c.json");
        var old = new PackageStandardsContext(PackageValidationCatalog.Load(bytes), PackageXmlAssets.Current, IchSectionDefinitions.Current);
        var context = new PackageStandardsContext(PackageValidationCatalog.Current, PackageXmlAssets.Current, IchSectionDefinitions.Current,
            history: new Dictionary<string, PackageStandardsContext> { ["0000"] = old });
        var history = Us().Replace(">0001<", ">0000<", StringComparison.Ordinal).Replace("<id>applicant-id</id>", "<id />", StringComparison.Ordinal);
        var result = await Inspect(Us(), selectHistory: true, context: context, historyXml: history);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == "US-ADMIN-METADATA").Status);
    }

    [Fact]
    public void DuplicateRegionalObservationsAreRejectedInsteadOfCollapsedIntoPassingCoverage()
    {
        Assert.Throws<ArgumentException>(() => new PackageRegionalInspection([new("US-ADMIN-METADATA", CheckStatus.Pass, "Observed"), new("US-ADMIN-METADATA", CheckStatus.Pass, "Duplicate")]));
    }

    [Fact]
    public void FdaNamedFilePolicyIsNotBlindlyRegisteredForEu()
    {
        var provider = new RegionalEctdRuleSetProvider([new FileNamingConventionRule()]);
        var us = provider.GetRuleSet(Profile("us-fda-ectd-3.2.2", "4.5"));
        var eu = provider.GetRuleSet(Profile("eu-ectd-3.2.2", "8.2"));
        Assert.Single(us.Rules);
        Assert.DoesNotContain(eu.Rules, rule => rule.RuleId == "FDA-NAMING-1");
    }

    private static readonly string[] NewIds = ["US-ADMIN-METADATA", "US-FORM-ATTRIBUTES", "US-SUBMISSION-SCOPE", "EU-ENVELOPE-METADATA", "EU-PI-CONTEXT", "EU-RELATED-SEQUENCE"];

    private static StandardsProfile Profile(string key, string criteria) => new(key, key, "Agency", "Region", "3.2.2", "M1", criteria, criteria, [], []);

    private static PackageStandardsContext ArchivedHistoryContext()
    {
        using var bytes = File.OpenRead("tests/RATools.Tests/Fixtures/Publisher/p608-archived-package-rules-e087d3c.json");
        var old = new PackageStandardsContext(PackageValidationCatalog.Load(bytes), PackageXmlAssets.Current, IchSectionDefinitions.Current);
        return new(PackageValidationCatalog.Current, PackageXmlAssets.Current, IchSectionDefinitions.Current,
            history: new Dictionary<string, PackageStandardsContext> { ["0000"] = old });
    }

    internal static async Task<PackageDeliveryInspection> Inspect(string? regional, string profile = PackageValidationCatalog.UsProfile,
        bool selectHistory = false, PackageStandardsContext? context = null, string? historyXml = null, bool invalidPdf = false)
    {
        var app = Guid.Parse("bcb2ea58-2848-4aa7-8882-10c5e5ad5e13");
        var target = new MemoryInput(app, "0001", regional, profile, invalidPdf);
        var old = new MemoryInput(app, "0000", historyXml ?? Eu("initial", "0000").Replace(">0001<", ">0000<", StringComparison.Ordinal), profile);
        var baseline = new HistoryBaselineManifest(app, "0001", selectHistory ? [new("0000", HistorySourceKind.ImportedExternal, old.SourceId, old.InputDigest, profile, HistoryTrustStatus.Unverified)] : []);
        var inputs = new PackageInputSet(target, baseline, selectHistory ? [old] : []);
        return await new PackageDeliveryInspector(new PdfPigPackageInspector(), standards: context).InspectAsync(new(inputs), profile);
    }

    internal static string Us() => """
        <?xml version="1.0"?>
        <!DOCTYPE fda-regional:fda-regional SYSTEM "../../util/dtd/us-regional-v3-3.dtd">
        <fda-regional:fda-regional xmlns:fda-regional="http://www.ich.org/fda" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.3">
        <admin><applicant-info><id>applicant-id</id><company-name>Applicant</company-name><submission-description>Description</submission-description>
        <applicant-contacts><applicant-contact><applicant-contact-name applicant-contact-type="regulatory">First</applicant-contact-name><telephones><telephone telephone-number-type="business">111</telephone><telephone telephone-number-type="fax">222</telephone></telephones><emails><email>a@example.invalid</email><email>b@example.invalid</email></emails></applicant-contact><applicant-contact><applicant-contact-name applicant-contact-type="agent">Second</applicant-contact-name><telephones><telephone telephone-number-type="business">333</telephone></telephones><emails><email>c@example.invalid</email></emails></applicant-contact></applicant-contacts>
        </applicant-info><application-set><application application-containing-files="true"><application-information><application-number application-type="ind">application-id</application-number></application-information><submission-information><submission-id submission-type="original-application">submission-id</submission-id><sequence-number submission-sub-type="original">0001</sequence-number></submission-information></application></application-set></admin>
        <m1-regional><m1-1-forms><form form-type="1571"/><form form-type="356h"/></m1-1-forms></m1-regional>
        </fda-regional:fda-regional>
        """;

    internal static string Eu(string unit = "initial", string related = "0001") => $$"""
        <?xml version="1.0"?>
        <!DOCTYPE eu:eu-backbone SYSTEM "../../util/dtd/eu-regional.dtd">
        <eu:eu-backbone xmlns:eu="http://europa.eu.int" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.1">
        <eu-envelope><envelope country="de"><identifier>business-id</identifier><submission type="maa" mode="single"><procedure-tracking><number>track-one</number><number>track-two</number></procedure-tracking></submission><submission-unit type="{{unit}}"/><applicant>Applicant</applicant><agency code="DE-BFARM"/><procedure type="national"/><invented-name>First</invented-name><invented-name>Second</invented-name><inn>Substance</inn><sequence>0001</sequence><related-sequence>{{related}}</related-sequence><submission-description>Description</submission-description></envelope></eu-envelope>
        <m1-eu><m1-0-cover><specific country="de"/></m1-0-cover><m1-3-pi><m1-3-1-spc-label-pl><pi-doc country="de" xml:lang="de" type="spc"/></m1-3-1-spc-label-pl></m1-3-pi></m1-eu></eu:eu-backbone>
        """;

    private sealed class MemoryInput : ICapturedPackageInput
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        public MemoryInput(Guid app, string sequence, string? regional, string profile, bool invalidPdf = false)
        {
            var region = profile == PackageValidationCatalog.EuProfile ? "eu" : "us";
            var reference = regional is null ? "" : $"<m1-administrative-information-and-prescribing-information><leaf ID=\"regional\" operation=\"new\" xlink:href=\"m1/{region}/{region}-regional.xml\"><title>Regional</title></leaf></m1-administrative-information-and-prescribing-information>";
            Add(sequence + "/index.xml", $"<!DOCTYPE ectd:ectd SYSTEM \"util/dtd/ich-ectd-3-2.dtd\"><ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" xmlns:xlink=\"http://www.w3c.org/1999/xlink\" dtd-version=\"3.2\">{reference}</ectd:ectd>");
            if (regional is not null) Add(sequence + $"/m1/{region}/{region}-regional.xml", regional);
            if (invalidPdf) Add(sequence + "/invalid.pdf", "%PDF-1.4\ninvalid object structure");
            foreach (var asset in PackageXmlAssets.Current.Assets.Values)
                _files[sequence + "/" + asset.LogicalPath] = File.ReadAllBytes(asset.Path);
            Manifest = new(app, sequence, _files.Select(pair => new PackageInputFile(pair.Key, pair.Value.Length, Hash(pair.Value), Md5(pair.Value))), [], [], []);
            SourceId = "regional-input-" + sequence;
        }
        private void Add(string path, string content) => _files[path] = Encoding.UTF8.GetBytes(content);
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        private static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        public string SourceId { get; }
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; }
        public string? ArchiveSha256 => null;
        public string InputDigest => Manifest.Digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string logicalPath) => new MemoryStream(_files[logicalPath], writable: false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PdfFacts : IPdfInspector
    {
        public PdfInspectionResult Inspect(Stream pdfStream, string relativeHref) => new("1.7", false, false, true, true, [], true, [], PageCount: 1);
    }
}
