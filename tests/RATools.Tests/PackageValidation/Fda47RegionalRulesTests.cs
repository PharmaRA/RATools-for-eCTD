using RATools.Domain.PackageValidation;
using RATools.Application.PackageValidation;
using RATools.Application.Ctd;
using System.Text;
using System.Text.Json.Nodes;

namespace RATools.Tests.PackageValidation;

public sealed class Fda47RegionalRulesTests
{
    private static readonly string[] FirstBatch = ["2035", "2036", "2037", "2038", "2042", "2044", "3036", "3050", "3065"];

    private static string Valid() => PackageRegionalInspectionTests.Us()
        .Replace("<id>applicant-id</id>", "<id>123456789</id>", StringComparison.Ordinal)
        .Replace(">application-id<", ">123456<", StringComparison.Ordinal)
        .Replace(">submission-id<", ">0001<", StringComparison.Ordinal);

    private static PackageStandardsContext Context(DateOnly? date) => new(PackageValidationCatalog.Current,
        PackageXmlAssets.Current, IchSectionDefinitions.Current, selectionDate: date);

    private static Task<PackageDeliveryInspection> Inspect(string? xml) =>
        PackageRegionalInspectionTests.Inspect(xml, context: Context(new(2026, 11, 9)));

    [Fact]
    public async Task CurrentFdaRulesObserveValidAdminWithoutClaimingCompleteQualification()
    {
        var result = await Inspect(Valid());
        foreach (var id in FirstBatch)
            Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
        Assert.False(result.CreateReport(PackageValidationMode.Formal).IsReadyForFinalization);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Formal).Coverage,
            check => check.RuleId == "US-CRITERIA-INVENTORY").CheckStatus);
    }

    [Theory]
    [InlineData("2044", "<id>123456789</id>", "<id>12345678</id>")]
    [InlineData("2044", "<id>123456789</id>", "<id>12345678a</id>")]
    [InlineData("2044", "<id>123456789</id>", "<id>１２３４５６７８９</id>")]
    [InlineData("3036", ">123456<", ">12345<")]
    [InlineData("3036", ">123456<", ">12345x<")]
    [InlineData("3036", ">123456<", ">１２３４５６<")]
    [InlineData("3050", ">0001<", ">001<")]
    [InlineData("3050", ">0001<", ">00a1<")]
    [InlineData("3050", ">0001<", ">０００１<")]
    [InlineData("3065", "submission-type=\"original-application\">0001", "submission-type=\"original-application\">001")]
    [InlineData("3065", "submission-type=\"original-application\">0001", "submission-type=\"original-application\">00a1")]
    [InlineData("3065", "submission-type=\"original-application\">0001", "submission-type=\"original-application\">０００１")]
    public async Task NumericFieldsRequireExactAsciiDigitCounts(string id, string before, string after)
    {
        var result = await Inspect(Valid().Replace(before, after, StringComparison.Ordinal));
        var check = Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(check.Findings, finding => finding.Location.LogicalPath == "0001/m1/us/us-regional.xml" &&
            finding.Location.Line > 0 && finding.Location.NodePath is not null && finding.Location.Files?.Count > 0);
    }

    [Theory]
    [InlineData("2035", "Description", 128, CheckStatus.Pass)]
    [InlineData("2035", "Description", 129, CheckStatus.Fail)]
    [InlineData("2042", "a@example.invalid", 64, CheckStatus.Pass)]
    [InlineData("2042", "a@example.invalid", 65, CheckStatus.Fail)]
    public async Task DeclaredCharacterLimitsIncludeTheBoundary(string id, string original, int length, CheckStatus expected)
    {
        var result = await Inspect(Valid().Replace(original, new string('a', length), StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
    }

    [Fact]
    public async Task NoApplicationContainingFilesIsASeparateHighSeverityFailure()
    {
        var result = await Inspect(Valid().Replace("application-containing-files=\"true\"", "application-containing-files=\"false\"", StringComparison.Ordinal));
        var missing = Assert.Single(result.Checks, check => check.RuleId == "FDA47-2036");
        Assert.Equal(CheckStatus.Fail, missing.Status);
        Assert.All(missing.Findings, finding => Assert.Equal(ValidationSeverity.Error, finding.Severity));
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2037").Status);
    }

    [Theory]
    [InlineData("true", CheckStatus.Fail)]
    [InlineData("false", CheckStatus.Pass)]
    public async Task GroupedApplicationsMayHaveOnlyOneFileOwner(string secondValue, CheckStatus expected)
    {
        var xml = Valid();
        var start = xml.IndexOf("<application application-containing-files", StringComparison.Ordinal);
        var end = xml.IndexOf("</application>", start, StringComparison.Ordinal) + "</application>".Length;
        var second = xml[start..end].Replace("application-containing-files=\"true\"", "application-containing-files=\"" + secondValue + "\"", StringComparison.Ordinal)
            .Replace(">123456<", ">654321<", StringComparison.Ordinal);
        var result = await Inspect(xml.Replace("</application-set>", second + "</application-set>", StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2037").Status);
    }

    [Fact]
    public async Task MissingAndTruncatedRegionalInputsCannotPassOfficialRules()
    {
        foreach (var xml in new string?[] { null, Valid()[..^45] })
        {
            var result = await Inspect(xml);
            foreach (var id in FirstBatch)
                Assert.NotEqual(CheckStatus.Pass, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
        }
    }

    [Fact]
    public async Task EuProfileDoesNotAcquireFdaRules()
    {
        var result = await PackageRegionalInspectionTests.Inspect(PackageRegionalInspectionTests.Eu(),
            RATools.Application.PackageValidation.PackageValidationCatalog.EuProfile);
        Assert.DoesNotContain(result.Checks, check => check.RuleId.StartsWith("FDA47-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("654321", CheckStatus.Pass)]
    [InlineData("65432", CheckStatus.Fail)]
    [InlineData("65432a", CheckStatus.Fail)]
    [InlineData("６５４３２１", CheckStatus.Fail)]
    [InlineData(" 654321 ", CheckStatus.Fail)]
    public async Task EachOptionalCrossReferenceRequiresSixAsciiDigits(string value, CheckStatus expected)
    {
        var xml = Valid().Replace("</application-information>",
            "<cross-reference-application-number application-type=\"ind\">123456</cross-reference-application-number>" +
            "<cross-reference-application-number application-type=\"ind\">" + value + "</cross-reference-application-number></application-information>", StringComparison.Ordinal);
        var result = await Inspect(xml);
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2038").Status);
    }

    [Theory]
    [InlineData(2015, 6, 14, CheckStatus.NotApplicable)]
    [InlineData(2015, 6, 15, CheckStatus.Fail)]
    [InlineData(2026, 11, 9, CheckStatus.Fail)]
    public async Task BoundSelectionDateControlsOfficialEffectiveDate(int year, int month, int day, CheckStatus expected)
    {
        var result = await PackageRegionalInspectionTests.Inspect(Valid().Replace("Description", new string('a', 129), StringComparison.Ordinal),
            context: Context(new(year, month, day)));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2035").Status);
    }

    [Fact]
    public async Task MissingSelectionDateDoesNotInventOfficialCoverage()
    {
        var result = await PackageRegionalInspectionTests.Inspect(Valid(), context: Context(null));
        foreach (var id in FirstBatch)
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
    }

    [Fact]
    public async Task ArchivedCatalogDoesNotAcquireCurrentOfficialChecks()
    {
        using var bytes = File.OpenRead("tests/RATools.Tests/Fixtures/Publisher/fda47-before-package-rules-a176396.json");
        var context = new PackageStandardsContext(PackageValidationCatalog.Load(bytes), PackageXmlAssets.Current,
            IchSectionDefinitions.Current, selectionDate: new(2026, 11, 9));
        var result = await PackageRegionalInspectionTests.Inspect(PackageRegionalInspectionTests.Us(), context: context);
        Assert.DoesNotContain(result.Checks, check => check.RuleId.StartsWith("FDA47-", StringComparison.Ordinal));
        Assert.DoesNotContain(result.CreateReport(PackageValidationMode.Development).Coverage, check => check.RuleId.StartsWith("FDA47-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(128, CheckStatus.Pass)]
    [InlineData(129, CheckStatus.Fail)]
    public async Task CharacterLimitCountsUnicodeScalarsWithoutTrimming(int count, CheckStatus expected)
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600", count));
        var result = await Inspect(Valid().Replace("Description", text, StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2035").Status);
        var spaces = await Inspect(Valid().Replace("Description", new string('a', 128) + " ", StringComparison.Ordinal));
        Assert.Equal(CheckStatus.Fail, Assert.Single(spaces.Checks, check => check.RuleId == "FDA47-2035").Status);
    }

    [Fact]
    public async Task LaterContactAndGroupedApplicationAreNotSkipped()
    {
        var xml = Valid().Replace("c@example.invalid", new string('a', 65), StringComparison.Ordinal);
        var start = xml.IndexOf("<application application-containing-files", StringComparison.Ordinal);
        var end = xml.IndexOf("</application>", start, StringComparison.Ordinal) + "</application>".Length;
        var second = xml[start..end].Replace("application-containing-files=\"true\"", "application-containing-files=\"false\"", StringComparison.Ordinal)
            .Replace(">123456<", ">bad<", StringComparison.Ordinal);
        var result = await Inspect(xml.Replace("</application-set>", second + "</application-set>", StringComparison.Ordinal));
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2042").Status);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Checks, check => check.RuleId == "FDA47-3036").Status);
    }

    [Fact]
    public async Task ReplacedOfficialSourceCannotAuthorizeChecksWithTheSameIds()
    {
        var json = JsonNode.Parse(PackageValidationCatalog.ExportSnapshot().GetRawText())!;
        json["sources"]!.AsArray().Single(source => source!["id"]!.GetValue<string>() == "fda-validation-criteria-4.7")!["sha256"] = new string('a', 64);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var context = new PackageStandardsContext(PackageValidationCatalog.Load(bytes), PackageXmlAssets.Current,
            IchSectionDefinitions.Current, selectionDate: new(2026, 11, 9));
        var result = await PackageRegionalInspectionTests.Inspect(Valid(), context: context);
        foreach (var id in FirstBatch)
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
    }

    [Fact]
    public async Task CompleteXmlWithoutDoctypeCannotAcquireOfficialCoverage()
    {
        var result = await Inspect(Valid().Replace("<!DOCTYPE fda-regional:fda-regional SYSTEM \"../../util/dtd/us-regional-v3-3.dtd\">", "", StringComparison.Ordinal));
        foreach (var id in FirstBatch)
            Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Checks, check => check.RuleId == "FDA47-" + id).Status);
    }

    [Theory]
    [InlineData(true, 2026, CheckStatus.Pass)]
    [InlineData(false, 2014, CheckStatus.Pass)]
    [InlineData(false, 2026, CheckStatus.Fail)]
    public async Task SelectedHistoryKeepsItsOwnCatalogAndEffectiveDate(bool archived, int historyYear, CheckStatus expected)
    {
        using var bytes = File.OpenRead("tests/RATools.Tests/Fixtures/Publisher/fda47-before-package-rules-a176396.json");
        var old = new PackageStandardsContext(archived ? PackageValidationCatalog.Load(bytes) : PackageValidationCatalog.Current,
            PackageXmlAssets.Current, IchSectionDefinitions.Current, selectionDate: new(historyYear, 11, 9));
        var context = new PackageStandardsContext(PackageValidationCatalog.Current, PackageXmlAssets.Current,
            IchSectionDefinitions.Current, history: new Dictionary<string, PackageStandardsContext> { ["0000"] = old }, selectionDate: new(2026, 11, 9));
        var history = Valid().Replace(">0001<", ">0000<", StringComparison.Ordinal)
            .Replace("Description", new string('a', 129), StringComparison.Ordinal);
        var result = await PackageRegionalInspectionTests.Inspect(Valid(), selectHistory: true, context: context, historyXml: history);
        Assert.Equal(expected, Assert.Single(result.Checks, check => check.RuleId == "FDA47-2035").Status);
        if (expected == CheckStatus.Fail)
            Assert.All(Assert.Single(result.Checks, check => check.RuleId == "FDA47-2035").Findings,
                finding => Assert.Equal("0000", finding.Location.SequenceNumber));
    }
}
