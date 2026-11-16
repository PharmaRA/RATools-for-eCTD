using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.UsRegional;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Tests.PackageValidation;
using System.Text;
using System.Text.Json.Nodes;
using RATools.Domain.Standards;

namespace RATools.Tests.RegionalMetadata;

public sealed class FdaSupplementCombinationTests
{
    private const string Rule = "US-SUPPLEMENT-EFFECTIVE-COMBINATION";
    private static RegionalMetadataContent Content(string type, string effective) => new(1, "US", Us: new("000001", "fdaat1", "Applicant",
        [new(Guid.NewGuid(), "Contact", "fdaact1", [new(Guid.NewGuid(), "5550100", "fdatnt1")], [new(Guid.NewGuid(), "synthetic@example.test")])],
        type, "fdasst3", null, "Submission", "001234567", "0001", effective));

    // Literal expectations independently transcribed from M1 2.6 Tables 2 and 3.
    [Theory]
    [InlineData("fdast2", "fdasedt1", true)]
    [InlineData("fdast2", "fdasedt2", false)]
    [InlineData("fdast2", "fdasedt3", false)]
    [InlineData("fdast3", "fdasedt1", true)]
    [InlineData("fdast3", "fdasedt2", true)]
    [InlineData("fdast3", "fdasedt3", true)]
    [InlineData("fdast4", "fdasedt1", true)]
    [InlineData("fdast4", "fdasedt2", true)]
    [InlineData("fdast4", "fdasedt3", false)]
    [InlineData("fdast11", "fdasedt1", true)]
    [InlineData("fdast11", "fdasedt2", false)]
    [InlineData("fdast11", "fdasedt3", true)]
    public async Task EveryCombinationAgreesAcrossDraftWriterAndIndependentInspection(string type, string effective, bool allowed)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var content = Content(type, effective);
        var preview = await rig.Service.PreflightAsync(rig.App.Id, "0001", new(rig.Snapshot.Id, content));
        Assert.Equal(!allowed, preview.Diagnostics.Any(item => item.Code == "SupplementEffectiveTypeCombinationInvalid"));
        var saved = await rig.Save(content);
        Assert.Equal(effective, saved.Content!.Us!.SupplementEffectiveDateType);
        if (allowed)
        {
            var output = new UsRegionalXmlWriter().Write(await rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
            Assert.Equal(effective, output.Document.Descendants("submission-id").Single().Attribute("supplement-effective-date-type")!.Value);
        }
        else
        {
            var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
            Assert.Equal("us.supplementEffectiveDateType", error.FieldName);
        }
        var xml = XDocument.Parse(Fda47AttributeRulesTests.Valid());
        xml.Descendants("application-number").Single().SetAttributeValue("application-type", "fdaat1");
        xml.Descendants("submission-id").Single().SetAttributeValue("submission-type", type);
        xml.Descendants("submission-id").Single().SetAttributeValue("supplement-effective-date-type", effective);
        var inspected = await PackageRegionalInspectionTests.Inspect(xml.ToString(), context: PackageStandardsContext.FromSnapshot(rig.Snapshot, selectionDate: new(2026, 11, 17)));
        var check = Assert.Single(inspected.Checks, item => item.RuleId == Rule);
        Assert.Equal(allowed ? CheckStatus.Pass : CheckStatus.Fail, check.Status);
        if (!allowed) Assert.Contains(check.Findings, item => item.Actual == effective && item.Location.FieldPath == "@supplement-effective-date-type");
    }

    [Theory]
    [InlineData("missing-resource")]
    [InlineData("source-status")]
    [InlineData("rule-version")]
    [InlineData("old-catalog")]
    public async Task ArchivedAuthorizationCannotBeReplacedByCurrentMappings(string change)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var snapshot = rig.Snapshot;
        var rulesResource = snapshot.Resources.Single(item => item.Key == "rules");
        var json = JsonNode.Parse(rulesResource.ReadBytes())!;
        if (change == "source-status") json["sources"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "ratools-fda-supplement-combinations-v1")!["verificationStatus"] = "Unverified";
        if (change == "rule-version") json["rules"]!.AsArray().Single(item => item!["internalRuleId"]!.GetValue<string>() == Rule)!["ruleVersion"] = "unknown";
        if (change == "old-catalog")
        {
            var rules = json["rules"]!.AsArray(); rules.Remove(rules.Single(item => item!["internalRuleId"]!.GetValue<string>() == Rule));
        }
        var resources = snapshot.Resources.Where(item => item.Key != "rules" && !(change == "missing-resource" && item.Key == "fda-supplement-combinations"))
            .Append(StandardsSnapshotResource.Create("rules", rulesResource.Category, rulesResource.RelativePath, rulesResource.MediaType, Encoding.UTF8.GetBytes(json.ToJsonString())));
        var old = StandardsProfileSnapshot.Create(snapshot.Descriptor, resources, snapshot.AcquiredUtc);
        var inspected = await PackageRegionalInspectionTests.Inspect(Fda47AttributeRulesTests.Valid(), context: PackageStandardsContext.FromSnapshot(old, selectionDate: new(2026, 11, 17)));
        if (change == "old-catalog") Assert.DoesNotContain(inspected.Checks, item => item.RuleId == Rule);
        else Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(inspected.Checks, item => item.RuleId == Rule).Status);
    }
}
