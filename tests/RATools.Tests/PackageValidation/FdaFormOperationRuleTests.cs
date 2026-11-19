using System.Xml.Linq;
using RATools.Domain.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class FdaFormOperationRuleTests
{
    [Theory]
    [InlineData("form", "new", "valid", "2013-04-30", CheckStatus.Pass)]
    [InlineData("form", "delete", "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("admin-form", "append", "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("cover-extension", "delete", "valid", "2026-11-18", CheckStatus.Fail)]
    [InlineData("cover", "replace", "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("form", null, "valid", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("other", "append", "valid", "2026-11-18", CheckStatus.Pass)]
    [InlineData("cover", "append", "unread", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("cover", "append", "missing-source", "2026-11-18", CheckStatus.NotEvaluated)]
    [InlineData("cover", "append", "valid", null, CheckStatus.NotEvaluated)]
    [InlineData("cover", "append", "valid", "2013-04-29", CheckStatus.NotApplicable)]
    [InlineData("cover", "append", "budget", "2026-11-18", CheckStatus.NotEvaluated)]
    public async Task ClassifiesTargetRegionalOperations(string section, string? operation, string variant, string? date, CheckStatus expected)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date, editRegional: xml =>
        {
            var module = xml.Descendants("m1-regional").Single();
            var leaf = new XElement("leaf", new XAttribute("ID", "operation-leaf"), new XElement("title", "Test"));
            leaf.SetAttributeValue("operation", operation);
            if (section == "form") xml.Descendants("form").First().Add(leaf);
            else if (section == "admin-form") xml.Descendants("submission-information").First().Add(new XElement("form", new XAttribute("form-type", "fdaft1"), leaf));
            else if (section == "other") module.Add(new XElement("m1-3-administrative-information", new XElement("m1-3-6-tropical-disease-priority-review-voucher", leaf)));
            else module.Add(new XElement("m1-2-cover-letters", section == "cover-extension"
                ? new XElement("node-extension", new XAttribute("ID", "ext"), new XElement("title", "Documents"), leaf) : leaf));
        });
        var check = Assert.Single(inspection.Files.Checks, item => item.RuleId == "FDA47-1038");
        Assert.Equal(expected, check.Status);
        if (expected == CheckStatus.Fail || operation == "replace")
        {
            var finding = Assert.Single(check.Findings);
            Assert.Equal(expected, finding.CheckStatus);
            Assert.Equal(ValidationSeverity.Info, finding.Severity);
            Assert.Equal("operation-leaf", finding.Location.LeafId);
            Assert.Equal("@operation", finding.Location.FieldPath);
            Assert.Equal(operation, finding.Actual);
        }
    }
}
