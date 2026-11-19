using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaChecksumMatchRuleTests
{
    [Theory]
    [InlineData("MD5", false, false)]
    [InlineData("Md5", false, false)]
    [InlineData("MD5", true, false)]
    [InlineData("Md5", true, false)]
    [InlineData("MD5", false, true)]
    [InlineData("Md5", true, true)]
    [InlineData("md5", false, true)]
    [InlineData("md5", true, true)]
    public async Task AcceptedAlgorithmNamesCompareActualBytesIncludingStf(string algorithm, bool mismatch, bool stf)
    {
        var result = await Inspect(mismatch ? "mismatch" : "match", algorithm: algorithm, stf: stf);
        Assert.Equal(CheckStatus.Pass, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1408").Status);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374");
        Assert.Equal(mismatch ? CheckStatus.Fail : CheckStatus.Pass, check.Status);
        Assert.DoesNotContain(result.Files.Checks.SelectMany(item => item.Findings), item => item.Code == "INVALID_FILE_CHECKSUM");
        if (mismatch)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(Md5(stf ? Encoding.UTF8.GetBytes(StfContent) : "content"u8.ToArray()), finding.Expected);
            Assert.Contains(result.Files.Checks.SelectMany(item => item.Findings), item => item.Code == "FILE_CHECKSUM_MISMATCH");
        }
        if (stf)
        {
            var document = Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.Documents);
            Assert.True(document.ReadComplete);
            Assert.Equal(CheckStatus.Pass, document.DtdStatus);
            Assert.DoesNotContain(document.Elements.SelectMany(element => element.Attributes.Values), attribute => attribute.Name is "checksum" or "checksum-type");
        }
    }

    [Theory]
    [InlineData(" MD5")]
    [InlineData("MD5 ")]
    [InlineData("sha256")]
    public async Task AlgorithmComparisonDoesNotTrimOrAcceptAnotherAlgorithm(string algorithm)
    {
        var result = await Inspect("mismatch", algorithm: algorithm);
        Assert.Equal(CheckStatus.Fail, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1408").Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.Files.Checks, check => check.RuleId == "FDA47-1374").Status);
        Assert.Contains(result.Files.Checks.SelectMany(item => item.Findings), item => item.Code == "INVALID_FILE_CHECKSUM");
    }

    private const string StfContent = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
        "<ectd:study xmlns:ectd='http://www.ich.org/ectd'><study-identifier><title>A</title><study-id>A</study-id></study-identifier><study-document/></ectd:study>";

    [Theory]
    [InlineData("mismatch", CheckStatus.Fail, "FILE_CHECKSUM_MISMATCH")]
    [InlineData("match", CheckStatus.Pass, null)]
    [InlineData("upper", CheckStatus.Pass, null)]
    [InlineData("format", CheckStatus.NotEvaluated, "INVALID_FILE_CHECKSUM")]
    [InlineData("type", CheckStatus.NotEvaluated, "INVALID_FILE_CHECKSUM")]
    [InlineData("missing", CheckStatus.NotEvaluated, "REFERENCED_FILE_MISSING")]
    [InlineData("history-absent", CheckStatus.NotEvaluated, "FILE_HISTORY_NOT_AVAILABLE")]
    [InlineData("history-present", CheckStatus.Pass, null)]
    [InlineData("history-mismatch", CheckStatus.Fail, "FILE_CHECKSUM_MISMATCH")]
    [InlineData("history-bad-leaf", CheckStatus.Pass, "FILE_CHECKSUM_MISMATCH")]
    [InlineData("delete", CheckStatus.Pass, null)]
    [InlineData("query", CheckStatus.NotEvaluated, "FILE_REFERENCE_QUERY_UNSUPPORTED")]
    [InlineData("empty-query", CheckStatus.NotEvaluated, "FILE_REFERENCE_QUERY_UNSUPPORTED")]
    [InlineData("unknown-operation", CheckStatus.NotEvaluated, null)]
    [InlineData("missing-operation", CheckStatus.NotEvaluated, null)]
    [InlineData("missing-checksum", CheckStatus.NotEvaluated, "INVALID_FILE_CHECKSUM")]
    [InlineData("history-file-missing", CheckStatus.NotEvaluated, "REFERENCED_FILE_MISSING")]
    [InlineData("mixed", CheckStatus.Fail, "FILE_CHECKSUM_MISMATCH")]
    [InlineData("external", CheckStatus.NotEvaluated, "EXTERNAL_CONTENT_REFERENCE")]
    [InlineData("future", CheckStatus.NotEvaluated, "FILE_REFERENCE_IN_FUTURE")]
    [InlineData("fragment", CheckStatus.Pass, null)]
    [InlineData("malformed-path", CheckStatus.NotEvaluated, "INVALID_FILE_REFERENCE")]
    public async Task DistinguishesMismatchFromMissingComparisonEvidence(string scenario, CheckStatus expected, string? genericCode)
    {
        var result = await Inspect(scenario);
        if (genericCode is not null) Assert.Contains(result.Files.Checks.SelectMany(item => item.Findings), item => item.Code == genericCode);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail)
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("@checksum", finding.Location.FieldPath);
            Assert.Equal("regional", finding.Location.LeafId);
            Assert.Equal(new string('0', 32), finding.Actual);
            Assert.Equal(Md5(scenario == "history-mismatch" ? "historical content"u8.ToArray() : "content"u8.ToArray()), finding.Expected);
            Assert.True(finding.Location.Line > 0);
            Assert.Equal("0001/index.xml", Assert.Single(finding.Location.Files!).LogicalPath);
        }
        else Assert.Empty(check.Findings);
    }

    [Theory]
    [InlineData("index-mismatch", CheckStatus.Fail)]
    [InlineData("index-upper", CheckStatus.Pass)]
    [InlineData("index-newline", CheckStatus.NotEvaluated)]
    [InlineData("index-space", CheckStatus.NotEvaluated)]
    [InlineData("index-bom", CheckStatus.NotEvaluated)]
    [InlineData("index-nonascii", CheckStatus.NotEvaluated)]
    [InlineData("index-format", CheckStatus.NotEvaluated)]
    [InlineData("index-missing", CheckStatus.NotEvaluated)]
    public async Task ComparesOnlyAValidIndexChecksum(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        var check = Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail) Assert.Equal("0001/index-md5.txt", Assert.Single(check.Findings).Location.LogicalPath);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("missing-ich-spec", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("truncated", "2026-11-19", CheckStatus.NotEvaluated)]
    [InlineData("unread", "2026-11-19", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-19", CheckStatus.NotEvaluated)]
    public async Task HonorsSourceDateAndCoverage(string variant, string? date, CheckStatus expected)
        => Assert.Equal(expected, Assert.Single((await Inspect("mismatch", variant, date)).Files.Checks, item => item.RuleId == "FDA47-1374").Status);

    [Fact]
    public async Task RegionalChecksumUsesItsOwnRelativeFile()
    {
        var result = await Inspect("regional");
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374").Findings);
        Assert.Equal("0001/m1/us/us-regional.xml", finding.Location.LogicalPath);
        Assert.Equal("form-checksum", finding.Location.LeafId);
        Assert.Equal(Md5("content"u8.ToArray()), finding.Expected);
    }

    [Fact]
    public async Task IndexLeafComparesTheRegionalXmlBytes()
    {
        var result = await Inspect("regional-reference");
        var finding = Assert.Single(Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1374").Findings);
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.NotEqual(Md5("content"u8.ToArray()), finding.Expected);
        Assert.Equal(new string('0', 32), finding.Actual);
    }

    [Theory]
    [InlineData("match", CheckStatus.Pass)]
    [InlineData("mismatch", CheckStatus.Fail)]
    [InlineData("missing", CheckStatus.NotEvaluated)]
    public async Task CompletedRuleRetainsActualCoverageStatus(string scenario, CheckStatus expected)
    {
        var result = await Inspect(scenario);
        Assert.Equal(expected, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
            item => item.RuleId == "FDA47-1374").CheckStatus);
    }

    [Fact]
    public async Task IchProfileDoesNotAcquireFdaCoverage()
        => Assert.DoesNotContain((await Inspect("match", "ich-profile")).Checks, item => item.RuleId == "FDA47-1374");

    private static Task<PackageDeliveryInspection> Inspect(string scenario, string variant = "valid", string? date = "2026-11-19",
        string? algorithm = null, bool stf = false)
        => FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            scenario is "history-present" or "history-mismatch" or "history-bad-leaf" or "history-file-missing" ? "history-1374" : variant, date,
            editSource: source =>
            {
                if (variant == "truncated") return;
                var contentName = stf ? "m4/study/stf-a.xml" : "m2/content.txt";
                var content = stf ? Encoding.UTF8.GetBytes(StfContent) : "content"u8.ToArray();
                var target = Path.Combine(source, "0001");
                Write(Path.Combine(target, contentName), content);
                if (scenario is "history-present" or "history-mismatch" or "history-bad-leaf") Write(Path.Combine(source, "0000", contentName), "historical content"u8.ToArray());
                var indexPath = Path.Combine(target, "index.xml");
                var xml = XDocument.Load(indexPath);
                var leaf = xml.Descendants("leaf").Single();
                leaf.Parent!.Name = stf ? "m4-nonclinical-study-reports" : "m2-common-technical-document-summaries";
                var href = scenario switch
                {
                    "missing" => "m2/absent.txt", "history-absent" or "history-present" or "history-mismatch" or "history-file-missing" => "../0000/m2/content.txt",
                    "future" => "../0002/m2/content.txt", "external" => "https://example.invalid/content.txt", "query" => contentName + "?download",
                    "empty-query" => contentName + "?", "regional-reference" => "m1/us/us-regional.xml",
                    "fragment" => "m2/content%2Etxt#part", "malformed-path" => "m2/file%ZZ.txt", _ => contentName
                };
                leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), href);
                leaf.SetAttributeValue("checksum", scenario is "mismatch" or "history-mismatch" or "mixed" or "regional-reference" ? new string('0', 32) : scenario == "format" ? "wrong" :
                    scenario == "history-present" ? Md5("historical content"u8.ToArray()) :
                    scenario == "upper" ? Md5(content).ToUpperInvariant() : Md5(content));
                leaf.SetAttributeValue("checksum-type", algorithm ?? (scenario == "type" ? "sha256" : "md5"));
                if (scenario == "regional-reference") leaf.Parent!.Name = "m1-administrative-information-and-prescribing-information";
                if (scenario == "missing-checksum") leaf.SetAttributeValue("checksum", null);
                if (scenario == "missing-operation") leaf.SetAttributeValue("operation", null);
                if (scenario == "unknown-operation") leaf.SetAttributeValue("operation", "unknown");
                if (scenario == "mixed")
                {
                    var other = new XElement(leaf); other.SetAttributeValue("ID", "unresolved");
                    other.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m2/absent.txt");
                    leaf.AddBeforeSelf(other);
                }
                if (scenario == "delete") { leaf.SetAttributeValue("operation", "delete"); leaf.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), null); }
                if (scenario == "history-bad-leaf")
                {
                    var previousPath = Path.Combine(source, "0000/index.xml");
                    var previous = XDocument.Load(previousPath);
                    var old = previous.Descendants("leaf").Single();
                    old.SetAttributeValue("checksum", new string('0', 32)); old.SetAttributeValue("checksum-type", "md5");
                    previous.Save(previousPath);
                }
                if (scenario == "regional")
                {
                    var path = Path.Combine(target, "m1/us/us-regional.xml");
                    var regional = XDocument.Load(path);
                    regional.Descendants("form").First().Add(new XElement("leaf", new XAttribute("ID", "form-checksum"),
                        new XAttribute("operation", "new"), new XAttribute(regional.Root!.GetNamespaceOfPrefix("xlink")! + "href", "../../m2/content.txt"),
                        new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"), new XElement("title", "Form")));
                    regional.Save(path);
                }
                xml.Save(indexPath);
                var hash = Md5(File.ReadAllBytes(indexPath));
                var indexValue = scenario switch { "index-mismatch" => new string('0', 32), "index-format" => "bad", "index-newline" => hash + "\r\n", "index-space" => hash + " ", "index-upper" => hash.ToUpperInvariant(), _ => hash };
                if (scenario != "index-missing") Write(Path.Combine(target, "index-md5.txt"), scenario switch
                {
                    "index-bom" => [0xef, 0xbb, 0xbf, .. Encoding.ASCII.GetBytes(indexValue)],
                    "index-nonascii" => Enumerable.Repeat((byte)0xff, 32).ToArray(),
                    _ => Encoding.ASCII.GetBytes(indexValue)
                });
            });

    private static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
}
