using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfUtilityTests
{
    [Theory]
    [InlineData("wrong-root", "1119")]
    [InlineData("wrong-namespace", "1119")]
    [InlineData("wrong-root", "1130")]
    [InlineData("wrong-namespace", "1130")]
    public async Task DeclaredCandidateWithoutActualStudyIdentityCannotAuthorizeConditionalFailure(string scenario, string authority)
    {
        var result = await Inspect(omitted: authority == "1119" ? "dtd" : null,
            changed: authority == "1130" ? "dtd" : null, editStf: text => scenario == "wrong-root"
                ? text.Replace("<ectd:study ", "<ectd:other ", StringComparison.Ordinal).Replace("</ectd:study>", "</ectd:other>", StringComparison.Ordinal)
                : text.Replace("xmlns:ectd='http://www.ich.org/ectd'", "xmlns:ectd='urn:not-stf'", StringComparison.Ordinal));
        var candidate = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.StudyTaggingFiles.Documents));
        Assert.Equal("../../util/dtd/ich-stf-v2-2.dtd", candidate.SystemIdentifier);
        Assert.False(candidate.ReadComplete);
        Assert.Empty(candidate.Elements);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-" + authority);
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("1119")]
    [InlineData("1130")]
    public async Task ActualStudyRootWithLaterTruncationRetainsIndependentUtilityFailure(string authority)
    {
        var result = await Inspect(omitted: authority == "1119" ? "dtd" : null,
            changed: authority == "1130" ? "dtd" : null, editStf: text => text[..^12]);
        var candidate = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.StudyTaggingFiles.Documents));
        Assert.False(candidate.ReadComplete);
        var root = Assert.Single(candidate.Elements, item => item.ParentPath is null);
        Assert.Equal("study", root.LocalName); Assert.Equal("http://www.ich.org/ectd", root.NamespaceUri);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-" + authority).Status);
    }

    [Theory]
    [InlineData("dtd", "1119", CheckStatus.Fail)]
    [InlineData("style", "1119", CheckStatus.Fail)]
    [InlineData("values", "1119", CheckStatus.Fail)]
    [InlineData("dtd", "1130", CheckStatus.NotEvaluated)]
    [InlineData("style", "1130", CheckStatus.NotEvaluated)]
    [InlineData("values", "1130", CheckStatus.NotEvaluated)]
    public async Task MissingConditionalUtilityIsNotSatisfiedByBundledParsingAssets(string omitted, string authority, CheckStatus expected)
    {
        var result = await Inspect(omitted: omitted);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-" + authority);
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Contains(check.Findings, finding => finding.Location.LogicalPath!.EndsWith(FileName(omitted), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dtd")]
    [InlineData("style")]
    public async Task AlteredSupportedUtilityFailsPublishedChecksum(string changed)
    {
        var result = await Inspect(changed: changed);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130");
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal(changed == "dtd" ? "0972c10a4dadf3df5d2f41b2026a4a5c" : "92bfeb4cb358a4af786317a0da2309c5", finding.Expected);
        Assert.NotEqual(finding.Expected, finding.Actual);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("alias")]
    public async Task ValidConditionalFilesAndDeclaredStyleAliasAreObserved(string variant)
    {
        var result = await Inspect(alias: variant == "alias");
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1119").Status);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
            item => item.RuleId == "FDA47-1130").CheckStatus); // creation-time ICH styles and legacy variants remain Partial.
    }

    [Theory]
    [InlineData("values")]
    [InlineData("truncated-stf")]
    [InlineData("no-style-pi")]
    [InlineData("remote-style")]
    public async Task UnsupportedVersionOrIncompleteRoleDoesNotBecomePass(string variant)
    {
        var result = await Inspect(changed: variant);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130").Status);
    }

    [Fact]
    public async Task KnownDtdMismatchSurvivesMissingStyleEvidence()
    {
        var result = await Inspect(changed: "dtd", omitted: "style");
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130").Status);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1119").Status);
    }

    [Theory]
    [InlineData("legacy-style")]
    [InlineData("unknown-alias")]
    public async Task ReviewUnknownStylesheetVersionIsNotAutomaticallyCorruptVersion23(string variant)
    {
        var result = await Inspect(changed: variant, alias: variant == "unknown-alias");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task ReviewMissingDependencySourceCannotProveValuesRequired()
    {
        var result = await Inspect(omitted: "values", withoutSource: "official-ich-stf-page");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1119");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("official-fda-stf-specification", "dtd")]
    [InlineData("official-ich-stf-dtd-md5", "dtd")]
    [InlineData("official-ich-stf-style-md5", "style")]
    public async Task MissingExactSourceCannotAuthorizeChecksumFailure(string source, string changed)
    {
        var result = await Inspect(changed: changed, withoutSource: source);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130");
        Assert.Equal(CheckStatus.NotEvaluated, check.Status);
        Assert.Empty(check.Findings);
    }

    [Fact]
    public async Task MissingDependencySourceDoesNotEraseIndependentDtdMismatch()
    {
        var result = await Inspect(omitted: "values", changed: "dtd", withoutSource: "official-ich-stf-page");
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130");
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.EndsWith("ich-stf-v2-2.dtd", Assert.Single(check.Findings).Location.LogicalPath);
    }

    [Theory]
    [InlineData(null, CheckStatus.NotEvaluated)]
    [InlineData("2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("2017-05-31", CheckStatus.NotEvaluated)]
    [InlineData("2017-06-01", CheckStatus.Fail)]
    public async Task ExplicitDateAndStyleVersionStartAreRequired(string? date, CheckStatus expected)
    {
        var result = await Inspect(changed: "style", date: date);
        Assert.Equal(expected, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130").Status);
    }

    [Fact]
    public async Task SharedFindingBudgetIsNotSpentAgainByConditionalRules()
    {
        var result = await Inspect(changed: "dtd", omitted: "style", variant: "budget");
        Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1130").Status);
    }

    private static string FileName(string kind) => kind switch
    {
        "dtd" => "ich-stf-v2-2.dtd", "style" => "ich-stf-stylesheet-2-3.xsl", "values" => "valid-values.xml", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Task<RATools.Application.PackageValidation.PackageDeliveryInspection> Inspect(string? omitted = null, string? changed = null, bool alias = false,
        string? withoutSource = null, string? date = "2026-11-20", string variant = "valid", Func<string, string>? editStf = null)
    {
        var styleName = changed == "legacy-style" ? "ich-stf-stylesheet-2-2.xsl" : alias ? "study-view.xsl" : FileName("style");
        var pi = changed == "no-style-pi" ? "" : "<?xml-stylesheet type=\"text/xsl\" href=\"" +
            (changed == "remote-style" ? "https://not-contacted.invalid/style.xsl" : "../../util/style/" + styleName) + "\"?>";
        var xml = pi + "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
            "<ectd:study xmlns:ectd='http://www.ich.org/ectd' dtd-version='2.2'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";
        if (changed == "truncated-stf") xml = xml[..^12];
        if (editStf is not null) xml = editStf(xml);
        return FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date: date,
            editSnapshot: withoutSource is null ? null : snapshot =>
            {
                var original = snapshot.Resources.Single(resource => resource.Key == withoutSource);
                return StandardsProfileSnapshot.Create(snapshot.Descriptor with
                { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != original.Sha256).ToArray() },
                    snapshot.Resources.Where(resource => resource.Key != withoutSource), snapshot.AcquiredUtc);
            },
            omittedTargetFile: omitted == "dtd" ? "util/dtd/" + FileName("dtd") : omitted == "style" ? "util/style/" + FileName("style") : null,
            capturedName: "m4/study/stf-a.xml", capturedBytes: Encoding.UTF8.GetBytes(xml),
            editIndex: index => index.Root!.Add(new XElement("m4-nonclinical-study-reports", new XElement("m4-3-literature-references",
                new XElement("leaf", new XAttribute("ID", "stf"), new XAttribute("operation", "new"),
                    new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml"), new XElement("title", "Study"))))),
            editSource: root =>
            {
                var official = Path.Combine(PackageRuleCatalogTests.Root, "reference/publisher/official-sources/2026-11-20-stf");
                if (omitted != "dtd") Copy("dtd", "util/dtd/" + FileName("dtd"), File.ReadAllBytes(Path.Combine(official, FileName("dtd"))));
                if (omitted != "style") Copy("style", "util/style/" + styleName, File.ReadAllBytes(Path.Combine(official, FileName("style"))));
                if (omitted != "values")
                {
                    using var archive = ZipFile.OpenRead(Path.Combine(official, "valid-values-v6.zip"));
                    using var input = archive.GetEntry("eCTD_v.3.2_Valid_Values_v6.0/valid-values.xml")!.Open();
                    using var output = new MemoryStream(); input.CopyTo(output);
                    Copy("values", "util/style/valid-values.xml", output.ToArray());
                }
                void Copy(string kind, string relative, byte[] bytes)
                {
                    var target = Path.Combine(root, "0001", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, kind == "style" && changed is "legacy-style" or "unknown-alias"
                        ? Encoding.UTF8.GetBytes("<xsl:stylesheet xmlns:xsl='http://www.w3.org/1999/XSL/Transform' version='1.0'/>")
                        : changed == kind ? [.. bytes, 32] : bytes);
                }
            });
    }
}
