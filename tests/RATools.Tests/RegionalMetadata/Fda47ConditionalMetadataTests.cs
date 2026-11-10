using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RATools.Application.Applications;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.UsRegional;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.RegionalMetadata;

public sealed class Fda47ConditionalMetadataTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExpectedEffectiveTypes = ["fdasedt1", "fdasedt2", "fdasedt3"];
    private static RegionalMetadataContent Content(string? effective, string type = "fdast4", string subtype = "fdasst3")
    {
        var original = ApplicantIdentifierTests.Content("001234567");
        var content = original with { Us = original.Us! with { ApplicationType = "fdaat1", SubmissionType = type,
            SubmissionSubtype = subtype, FormType = "fdaft3", Contacts = [new(Guid.NewGuid(), "Contact", "fdaact1",
                [new(Guid.NewGuid(), "5550100", "fdatnt1")], [new(Guid.NewGuid(), "synthetic@example.test")])] } };
        if (effective is null) return content;
        var node = JsonSerializer.SerializeToNode(content, Json)!;
        node["us"]!["supplementEffectiveDateType"] = effective;
        return RegionalMetadataJson.ReadSequence(node.ToJsonString());
    }

    [Theory]
    [InlineData("fdast2")]
    [InlineData("fdast3")]
    [InlineData("fdast4")]
    [InlineData("fdast11")]
    public async Task MissingSupplementTypeIsLocatedInDraftAndPreventsXmlGeneration(string type)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var content = Content(null, type);
        var preview = await rig.Service.PreflightAsync(rig.App.Id, "0001", new(rig.Snapshot.Id, content));
        Assert.Contains(preview.Diagnostics, issue => issue.Code == "FDA47-2041" && issue.FieldPath == "us.supplementEffectiveDateType");
        var saved = await rig.Save(content);
        Assert.Contains(saved.Diagnostics, issue => issue.Code == "FDA47-2041" && issue.FieldPath == "us.supplementEffectiveDateType");
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("us.supplementEffectiveDateType", error.FieldName);
    }

    [Theory]
    [InlineData("fdasedt1")]
    [InlineData("fdasedt2")]
    public async Task ExplicitSupplementTypeSurvivesSaveXmlIndependentValidationAndReimport(string value)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create();
        var saved = await rig.Save(Content(value));
        Assert.DoesNotContain(saved.Diagnostics, issue => issue.FieldPath == "us.supplementEffectiveDateType");
        var reloaded = await rig.Service.GetRevisionAsync(saved.Id);
        Assert.Equal(value, JsonSerializer.SerializeToNode(reloaded.Content, Json)!["us"]!["supplementEffectiveDateType"]!.GetValue<string>());
        var output = new UsRegionalXmlWriter().Write(await rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal(value, output.Document.Descendants("submission-id").Single().Attribute("supplement-effective-date-type")!.Value);
        var inspected = await PackageRegionalInspectionTests.Inspect(output.XmlContent,
            context: PackageStandardsContext.FromSnapshot(rig.Snapshot, selectionDate: new(2026, 11, 10)));
        foreach (var id in new[] { "2013", "2041", "2045" })
            Assert.Equal(CheckStatus.Pass, Assert.Single(inspected.Checks, item => item.RuleId == "FDA47-" + id).Status);
        Assert.False(inspected.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
        var imported = RegionalImportParser.Parse(Encoding.UTF8.GetBytes(output.XmlContent), false, "0001", rig.Snapshot);
        Assert.Equal(value, JsonSerializer.SerializeToNode(imported.Content, Json)!["us"]!["supplementEffectiveDateType"]!.GetValue<string>());
        Assert.DoesNotContain(imported.Diagnostics, issue => issue.Code == "RegionalUnmappedValue" && issue.FieldPath.EndsWith("@supplement-effective-date-type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CBE-0")]
    [InlineData("fdasedt2 ")]
    public async Task InvalidSupplementInputIsPreservedForCorrectionAndCannotPublish(string value)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var saved = await rig.Save(Content(value));
        Assert.Equal(value, JsonSerializer.SerializeToNode(saved.Content, Json)!["us"]!["supplementEffectiveDateType"]!.GetValue<string>());
        Assert.Contains(saved.Diagnostics, issue => issue.FieldPath == "us.supplementEffectiveDateType");
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("us.supplementEffectiveDateType", error.FieldName);
    }

    [Theory]
    [InlineData("fdast1", "fdasst3")]
    [InlineData("fdast4", "fdasst4")]
    public async Task NonApplicableEmptySupplementFieldDoesNotChangeHistoricalJsonOrBlockGeneration(string type, string subtype)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var original = Content(null, type, subtype);
        var before = JsonSerializer.Serialize(original, Json);
        Assert.DoesNotContain("supplementEffectiveDateType", before, StringComparison.Ordinal);
        var saved = await rig.Save(original);
        Assert.DoesNotContain(saved.Diagnostics, issue => issue.FieldPath == "us.supplementEffectiveDateType");
        var output = new UsRegionalXmlWriter().Write(await rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Null(output.Document.Descendants("submission-id").Single().Attribute("supplement-effective-date-type"));
        Assert.Equal(before, JsonSerializer.Serialize((await rig.Service.GetRevisionAsync(saved.Id)).Content, Json));
    }

    [Fact]
    public async Task SchemaOffersArchivedOfficialCodesAndCombinedSupplementCondition()
    {
        var rig = await ApplicantIdentifierTests.Rig.Create();
        var schema = await rig.Service.GetSchemaAsync(rig.App.Id, rig.Snapshot.Id);
        var fields = Assert.Single(schema.SequenceFields, field => field.Key == "us").Fields;
        var supplement = Assert.Single(fields, field => field.Key == "supplementEffectiveDateType");
        Assert.Equal(ExpectedEffectiveTypes, supplement.Options);
        Assert.Equal("official-fda-supplement-effective-types", supplement.Source!.ResourceKey);
        Assert.Contains(supplement.Conditions!, condition => condition.FieldPath == "us.submissionType" && condition.Effect == "required-all" && condition.Values.Contains("fdast11"));
        Assert.Contains(supplement.Conditions!, condition => condition.FieldPath == "us.submissionSubtype" && condition.Effect == "required-all" && condition.Values.Count == 1 && condition.Values[0] == "fdasst3");
        Assert.Contains("fdast4", Assert.Single(fields, field => field.Key == "submissionType").Options);
        Assert.Contains("fdasst3", Assert.Single(fields, field => field.Key == "submissionSubtype").Options);
        Assert.Contains("fdaft3", Assert.Single(fields, field => field.Key == "formType").Options);
    }

    [Theory]
    [InlineData("fdast1", "fdasst3", "SupplementEffectiveTypeNotApplicable")]
    [InlineData("fdast4", "fdasst4", "SupplementEffectiveTypeNotApplicable")]
    [InlineData("unknown", "fdasst3", "SupplementApplicabilityUnverified")]
    [InlineData("fdast4", "unknown", "SupplementApplicabilityUnverified")]
    public async Task SwitchingAwayFromAnApplicableSupplementRetainsDraftButCannotEmitTheAttribute(string type, string subtype, string diagnostic)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var content = Content("fdasedt2");
        var original = await rig.Save(content);
        var changed = content with { Us = content.Us! with { SubmissionType = type, SubmissionSubtype = subtype } };
        var preview = await rig.Service.PreflightAsync(rig.App.Id, "0001", new(rig.Snapshot.Id, changed));
        Assert.Contains(preview.Diagnostics, issue => issue.Code == diagnostic && issue.FieldPath == "us.supplementEffectiveDateType");
        var saved = await rig.Save(changed);
        Assert.Equal("fdasedt2", saved.Content!.Us!.SupplementEffectiveDateType);
        Assert.Contains(saved.Diagnostics, issue => issue.Code == diagnostic && issue.FieldPath == "us.supplementEffectiveDateType");
        var error = await Assert.ThrowsAsync<UsRegionalXmlMetadataException>(() => rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("us.supplementEffectiveDateType", error.FieldName);
        Assert.Equal("fdast4", (await rig.Service.GetRevisionAsync(original.Id)).Content!.Us!.SubmissionType);
    }
}
