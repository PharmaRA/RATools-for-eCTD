using RATools.Application.Ctd;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class Fda47GroupedRulesTests
{
    private static Task<PackageDeliveryInspection> Inspect(string xml, DateOnly? date = null) =>
        PackageRegionalInspectionTests.Inspect(xml, context: new(PackageValidationCatalog.Current, PackageXmlAssets.Current,
            IchSectionDefinitions.Current, selectionDate: date ?? new(2026, 11, 10)));
    private static string Grouped(string attribute, string value)
    {
        var xml = PackageRegionalInspectionTests.Us();
        var start = xml.IndexOf("<application application-containing-files", StringComparison.Ordinal);
        var end = xml.IndexOf("</application>", start, StringComparison.Ordinal) + "</application>".Length;
        var second = xml[start..end].Replace("application-containing-files=\"true\"", "application-containing-files=\"false\"", StringComparison.Ordinal);
        var original = attribute switch { "application-type" => "ind", "submission-type" => "original-application", _ => "original" };
        second = second.Replace(attribute + "=\"" + original + "\"", attribute + "=\"" + value + "\"", StringComparison.Ordinal);
        return xml.Replace("</application-set>", second + "</application-set>", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2023", "submission-sub-type", "original", CheckStatus.Pass)]
    [InlineData("2023", "submission-sub-type", "amendment", CheckStatus.Fail)]
    [InlineData("2031", "application-type", "ind", CheckStatus.Pass)]
    [InlineData("2031", "application-type", "nda", CheckStatus.Fail)]
    [InlineData("2032", "submission-type", "original-application", CheckStatus.Pass)]
    [InlineData("2032", "submission-type", "annual-report", CheckStatus.Fail)]
    public async Task AllApplicationsMustAgreeOnTheDeclaredGroupedValue(string id, string attribute, string value, CheckStatus expected)
    {
        var result = await Inspect(Grouped(attribute, value));
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            Assert.Contains(check.Findings, finding => finding.Location.NodePath!.Contains("application[2]", StringComparison.Ordinal) && finding.Location.FieldPath == "@" + attribute && finding.Actual == value);
            Assert.All(check.Findings, finding => Assert.Equal(ValidationSeverity.Error, finding.Severity));
        }
    }

    [Theory]
    [InlineData("2023", "submission-sub-type")]
    [InlineData("2031", "application-type")]
    [InlineData("2032", "submission-type")]
    public async Task MissingGroupedValuesCannotEstablishConsistency(string id, string attribute)
    {
        var result = await Inspect(Grouped(attribute, ""));
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Contains(check.Findings, finding => finding.Location.FieldPath == "@" + attribute && finding.Actual == "");
    }

    [Theory]
    [InlineData("Applicant", CheckStatus.Pass)]
    [InlineData("", CheckStatus.Fail)]
    [InlineData(" ", CheckStatus.Fail)]
    public async Task CompanyNameMustBeExplicit(string value, CheckStatus expected)
    {
        var result = await Inspect(PackageRegionalInspectionTests.Us().Replace("<company-name>Applicant</company-name>", "<company-name>" + value + "</company-name>", StringComparison.Ordinal));
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-1454");
        Assert.Equal(expected, check.Status);
        Assert.All(check.Findings, finding => Assert.Equal(ValidationSeverity.Info, finding.Severity));
    }

    [Theory]
    [InlineData(2013, 4, 29, CheckStatus.NotApplicable)]
    [InlineData(2013, 4, 30, CheckStatus.Fail)]
    public async Task CompanyNameUsesItsOwnOfficialEffectiveDate(int year, int month, int day, CheckStatus expected)
    {
        var result = await Inspect(PackageRegionalInspectionTests.Us().Replace("<company-name>Applicant</company-name>", "<company-name />", StringComparison.Ordinal), new(year, month, day));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-1454").Status);
    }

    [Theory]
    [InlineData(2015, 6, 14, CheckStatus.NotApplicable)]
    [InlineData(2015, 6, 15, CheckStatus.Fail)]
    public async Task GroupedRuleUsesItsOwnOfficialEffectiveDate(int year, int month, int day, CheckStatus expected)
    {
        var result = await Inspect(Grouped("application-type", "nda"), new(year, month, day));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2031").Status);
    }

    [Fact]
    public async Task SingleApplicationDoesNotInventGroupedConflicts()
    {
        var result = await Inspect(PackageRegionalInspectionTests.Us());
        foreach (var id in new[] { "2023", "2031", "2032" })
            Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
    }

    [Fact]
    public async Task MissingCompanyElementIsAnObservedFailure()
    {
        var result = await Inspect(PackageRegionalInspectionTests.Us().Replace("<company-name>Applicant</company-name>", "", StringComparison.Ordinal));
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, check => check.RuleId == "FDA47-1454").Status);
    }

    [Theory]
    [InlineData("2023", "submission-sub-type", "amendment")]
    [InlineData("2031", "application-type", "nda")]
    [InlineData("2032", "submission-type", "annual-report")]
    public async Task MissingMiddleValueDoesNotHideAThirdApplicationConflict(string id, string attribute, string conflict)
    {
        var xml = Grouped(attribute, ""); var different = Grouped(attribute, conflict);
        var start = different.LastIndexOf("<application application-containing-files", StringComparison.Ordinal);
        var end = different.IndexOf("</application>", start, StringComparison.Ordinal) + "</application>".Length;
        xml = xml.Replace("</application-set>", different[start..end] + "</application-set>", StringComparison.Ordinal);
        var result = await Inspect(xml);
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.CheckStatus == CheckStatus.Fail && finding.Location.NodePath!.Contains("application[3]", StringComparison.Ordinal));
        Assert.Contains(check.Findings, finding => finding.CheckStatus == CheckStatus.NotEvaluated && finding.Location.NodePath!.Contains("application[2]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CrossReferenceApplicationTypeDoesNotParticipateInGroupedApplicationComparison()
    {
        var xml = Grouped("application-type", "ind").Replace("</application-information>",
            "<cross-reference-application-number application-type=\"nda\">654321</cross-reference-application-number></application-information>", StringComparison.Ordinal);
        var result = await Inspect(xml);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2031").Status);
    }

    [Theory]
    [InlineData("2023", "submission-information", "sequence-number", "submission-sub-type", "amendment")]
    [InlineData("2031", "application-information", "application-number", "application-type", "nda")]
    [InlineData("2032", "submission-information", "submission-id", "submission-type", "annual-report")]
    public async Task DuplicateFieldsInsideOneApplicationAreAmbiguousNotGroupedConflicts(string id, string parent, string element, string attribute, string value)
    {
        var xml = PackageRegionalInspectionTests.Us().Replace("</" + parent + ">", "<" + element + " " + attribute + "=\"" + value + "\">0001</" + element + "></" + parent + ">", StringComparison.Ordinal);
        var result = await Inspect(xml);
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.DoesNotContain(check.Findings, finding => finding.CheckStatus == CheckStatus.Fail);
    }

    [Theory]
    [InlineData("2023", "submission-information", "sequence-number", "submission-sub-type", "amendment")]
    [InlineData("2031", "application-information", "application-number", "application-type", "nda")]
    [InlineData("2032", "submission-information", "submission-id", "submission-type", "annual-report")]
    public async Task AmbiguousApplicationDoesNotHideConflictBetweenOtherApplications(string id, string parent, string element, string attribute, string value)
    {
        var xml = Grouped(attribute, value);
        var start = xml.LastIndexOf("<application application-containing-files", StringComparison.Ordinal);
        var end = xml.IndexOf("</application>", start, StringComparison.Ordinal) + "</application>".Length;
        var third = xml[start..end];
        var ambiguous = third.Replace("</" + parent + ">", "<" + element + " " + attribute + "=\"ambiguous\">0001</" + element + "></" + parent + ">", StringComparison.Ordinal);
        xml = xml[..start] + ambiguous + third + xml[end..];
        var result = await Inspect(xml);
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.CheckStatus == CheckStatus.Fail && finding.Location.NodePath!.Contains("application[3]", StringComparison.Ordinal) && finding.Actual == value);
        Assert.DoesNotContain(check.Findings, finding => finding.CheckStatus == CheckStatus.Fail && finding.Location.NodePath!.Contains("application[2]", StringComparison.Ordinal));
    }
}
