using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Tests.PackageValidation;

public sealed class FdaPromotionalContactRuleTests
{
    [Theory]
    [InlineData("missing", CheckStatus.Fail)]
    [InlineData("present", CheckStatus.Pass)]
    [InlineData("nonpromo", CheckStatus.NotApplicable)]
    [InlineData("unknown-submission", CheckStatus.NotEvaluated)]
    [InlineData("missing-submission", CheckStatus.NotEvaluated)]
    [InlineData("foreign-submission", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-submission", CheckStatus.NotEvaluated)]
    [InlineData("no-contacts", CheckStatus.Fail)]
    [InlineData("no-name", CheckStatus.Fail)]
    [InlineData("empty-name", CheckStatus.Fail)]
    [InlineData("blank-name", CheckStatus.Fail)]
    [InlineData("unknown-contact", CheckStatus.NotEvaluated)]
    [InlineData("missing-contact-type", CheckStatus.NotEvaluated)]
    [InlineData("foreign-contact-type", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-name", CheckStatus.NotEvaluated)]
    [InlineData("complex-name", CheckStatus.NotEvaluated)]
    [InlineData("match-and-unknown", CheckStatus.Pass)]
    [InlineData("unknown-and-match", CheckStatus.Pass)]
    [InlineData("no-phone-email", CheckStatus.Pass)]
    [InlineData("duplicate-admin", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-applicant", CheckStatus.NotEvaluated)]
    [InlineData("duplicate-contacts", CheckStatus.NotEvaluated)]
    [InlineData("group-trigger", CheckStatus.Fail)]
    [InlineData("known-trigger-unknown-other", CheckStatus.Fail)]
    [InlineData("unknown-first-known-trigger", CheckStatus.Fail)]
    [InlineData("nonpromo-and-unknown", CheckStatus.NotEvaluated)]
    public async Task DistinguishesKnownTriggerMissingNameAndUnknownEvidence(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var regional = Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.UsRegional);
        Assert.True(regional.ReadComplete);
        if (scenario is "missing" or "present" or "nonpromo" or "group-trigger" or "known-trigger-unknown-other")
            Assert.Equal(CheckStatus.Pass, regional.DtdStatus);
        AssertCheck(result, expected);
    }

    [Theory]
    [InlineData("valid", "2015-06-14", CheckStatus.NotApplicable)]
    [InlineData("valid", "2015-06-15", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task DateSourceAndBudgetRemainExplicit(string variant, string? date, CheckStatus expected)
        => AssertCheck(await Inspect("missing", variant, date), expected);

    [Theory]
    [InlineData("fda-m1-attribute-policy")]
    [InlineData("official-fda-m1-specification")]
    [InlineData("official-fda-submission-types")]
    [InlineData("official-fda-applicant-contact-types")]
    public async Task MissingCapturedMappingOrSourcesCannotAuthorizeTheRule(string key)
        => AssertCheck(await Inspect("missing", removedResource: key), CheckStatus.NotEvaluated);

    [Theory]
    [InlineData("other-regional", CheckStatus.Fail)]
    [InlineData("history", CheckStatus.Fail)]
    [InlineData("broken", CheckStatus.NotEvaluated)]
    [InlineData("invalid-reference", CheckStatus.Fail)]
    public async Task OtherDocumentsAndIncompleteSelectionsDoNotSupplyTheContact(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        if (scenario == "other-regional")
        {
            Assert.Equal(2, Target(result).Backbones.Count(doc => doc.Kind == BackboneKind.UsRegional));
            Assert.Contains(Target(result).Backbones.Single(doc => doc.LogicalPath.EndsWith("/alternate.xml", StringComparison.Ordinal)).Elements,
                element => element.Attribute("applicant-contact-type") == "fdaact4");
        }
        if (scenario == "history")
        {
            var previous = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.Backbones), doc => doc.LogicalPath == "0000/m1/us/us-regional.xml");
            Assert.True(previous.ReadComplete);
            Assert.Contains(previous.Elements, element => element.Attribute("applicant-contact-type") == "fdaact4");
        }
        if (scenario == "broken") Assert.False(Assert.Single(Target(result).Backbones, doc => doc.Kind == BackboneKind.UsRegional).ReadComplete);
        if (scenario == "invalid-reference") Assert.Contains(Target(result).Checks.SelectMany(check => check.Findings), finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE");
        AssertCheck(result, expected);
        if (expected == CheckStatus.Fail)
            Assert.Equal("0001/m1/us/us-regional.xml", Assert.Single(Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1545").Findings).Location.LogicalPath);
    }

    [Fact]
    public async Task IchOnlyProfileDoesNotAcquireFdaRule()
        => Assert.DoesNotContain((await Inspect("missing", "ich-profile")).Files.Checks, check => check.RuleId == "FDA47-1545");

    private static PackageXmlInspection Target(PackageDeliveryInspection result)
        => Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Files.PrimaryInputDigest);

    private static void AssertCheck(PackageDeliveryInspection result, CheckStatus expected)
    {
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1545");
        Assert.Equal(expected, check.Status);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage, item => item.RuleId == "FDA47-1545").CheckStatus);
        if (expected != CheckStatus.Fail) { Assert.Empty(check.Findings); return; }
        var finding = Assert.Single(check.Findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
        Assert.Equal("applicant-contacts", finding.Location.FieldPath);
        Assert.Equal(finding.Location.LogicalPath, Assert.Single(finding.Location.Files!).LogicalPath);
    }

    private static Task<PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-20", string? removedResource = null)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", scenario == "other-regional" ? "m1/us/alternate.xml" : "m1/us/us-regional.xml", null,
            scenario == "other-regional" ? "skipped-regional-with-default" : scenario == "history" ? "history-1545" : variant, date,
            editIndex: xml =>
            {
                var leaf = xml.Descendants("leaf").Single(); leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5");
                if (scenario == "invalid-reference") { var extra = new XElement(leaf); extra.SetAttributeValue("ID", "invalid-regional"); extra.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "index.xml"); leaf.AddAfterSelf(extra); }
            },
            editRegional: xml => Edit(xml, scenario),
            editSource: source =>
            {
                if (scenario == "broken") File.AppendAllText(Path.Combine(source, "0001/m1/us/us-regional.xml"), "<broken");
                if (scenario is "other-regional" or "history")
                {
                    var path = Path.Combine(source, scenario == "history" ? "0000/m1/us/us-regional.xml" : "0001/m1/us/alternate.xml");
                    var xml = XDocument.Load(path); Edit(xml, "present"); xml.Save(path);
                }
            },
            editSnapshot: removedResource is null ? null : snapshot =>
            {
                var original = snapshot.Resources.Single(resource => resource.Key == removedResource);
                return StandardsProfileSnapshot.Create(snapshot.Descriptor with { Provenance = snapshot.Descriptor.Provenance.Where(source => source.Sha256 != original.Sha256).ToArray() },
                    snapshot.Resources.Where(resource => resource.Key != removedResource), snapshot.AcquiredUtc);
            });

    private static void Edit(XDocument xml, string scenario)
    {
        var admin = xml.Root!.Element("admin")!;
        var applicant = admin.Element("applicant-info")!;
        var applications = admin.Element("application-set")!;
        var application = applications.Element("application")!;
        var submission = application.Element("submission-information")!.Element("submission-id")!;
        submission.SetAttributeValue("submission-type", scenario == "nonpromo" ? "fdast1" : "fdast8");
        var contacts = applicant.Element("applicant-contacts")!;
        var first = contacts.Elements("applicant-contact").First();
        var name = first.Element("applicant-contact-name")!;
        if (scenario is "present" or "empty-name" or "blank-name" or "duplicate-name" or "complex-name" or "match-and-unknown" or "no-phone-email") name.SetAttributeValue("applicant-contact-type", "fdaact4");
        if (scenario == "unknown-submission") submission.SetAttributeValue("submission-type", "unknown");
        if (scenario == "missing-submission") submission.SetAttributeValue("submission-type", null);
        if (scenario == "foreign-submission") { submission.SetAttributeValue("submission-type", null); submission.SetAttributeValue(XName.Get("submission-type", "urn:other"), "fdast8"); }
        if (scenario == "duplicate-submission") submission.AddAfterSelf(new XElement(submission));
        if (scenario == "no-contacts") contacts.Remove();
        if (scenario == "no-name") foreach (var contact in contacts.Elements("applicant-contact")) contact.Element("applicant-contact-name")!.Remove();
        if (scenario is "empty-name" or "blank-name") name.Value = scenario == "empty-name" ? "" : " \t ";
        if (scenario == "unknown-contact") name.SetAttributeValue("applicant-contact-type", "unknown");
        if (scenario == "missing-contact-type") name.SetAttributeValue("applicant-contact-type", null);
        if (scenario == "foreign-contact-type") { name.SetAttributeValue("applicant-contact-type", null); name.SetAttributeValue(XName.Get("applicant-contact-type", "urn:other"), "fdaact4"); }
        if (scenario == "duplicate-name") name.AddAfterSelf(new XElement(name));
        if (scenario == "complex-name") { name.Value = ""; name.Add(new XElement("b", "Name")); }
        if (scenario == "match-and-unknown") contacts.Elements("applicant-contact").Last().Element("applicant-contact-name")!.SetAttributeValue("applicant-contact-type", "unknown");
        if (scenario == "unknown-and-match")
        {
            name.SetAttributeValue("applicant-contact-type", "unknown");
            contacts.Elements("applicant-contact").Last().Element("applicant-contact-name")!.SetAttributeValue("applicant-contact-type", "fdaact4");
        }
        if (scenario == "no-phone-email") { first.Element("telephones")!.Remove(); first.Element("emails")!.Remove(); }
        if (scenario == "duplicate-admin") admin.AddAfterSelf(new XElement(admin));
        if (scenario == "duplicate-applicant") applicant.AddAfterSelf(new XElement(applicant));
        if (scenario == "duplicate-contacts") contacts.AddAfterSelf(new XElement(contacts));
        if (scenario is "group-trigger" or "known-trigger-unknown-other" or "unknown-first-known-trigger" or "nonpromo-and-unknown")
        {
            var other = new XElement(application); other.SetAttributeValue("application-containing-files", "false");
            other.Element("application-information")!.Element("application-number")!.Value = "654321";
            if (scenario == "group-trigger") submission.SetAttributeValue("submission-type", "fdast1");
            else if (scenario == "unknown-first-known-trigger") submission.SetAttributeValue("submission-type", "unknown");
            else
            {
                other.Element("submission-information")!.Element("submission-id")!.SetAttributeValue("submission-type", "unknown");
                if (scenario == "nonpromo-and-unknown") submission.SetAttributeValue("submission-type", "fdast1");
            }
            application.AddAfterSelf(other);
        }
    }
}
