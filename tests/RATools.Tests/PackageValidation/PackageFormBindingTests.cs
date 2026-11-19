using System.Xml.Linq;
using RATools.Application.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageFormBindingTests
{
    [Fact]
    public async Task AdminFormsBindExactApplicationNotStorageApplicationAndKeepAttachments()
    {
        var inspection = await Inspect(xml =>
        {
            var first = xml.Descendants("application").Single();
            var second = new XElement(first);
            second.SetAttributeValue("application-containing-files", "false");
            second.Descendants("application-number").Single().Value = "654321";
            second.Descendants("submission-information").Single().Add(Form("fdaft1", "main", "attachment"));
            first.AddAfterSelf(second);
        });
        Assert.True(inspection.Files.Forms.InventoryComplete);
        var group = Assert.Single(inspection.Files.Forms.Groups, item => item.Scope == "ApplicationAdmin");
        Assert.Equal("fdaft1", group.FormType?.Value);
        var application = Assert.Single(group.Applications);
        Assert.Equal("false", application.ContainingFiles?.Value);
        Assert.Equal("654321", Assert.Single(application.Numbers).Number);
        Assert.Equal("main,attachment", string.Join(',', group.Leaves.Select(leaf => leaf.Id)));
        Assert.All(group.Leaves, leaf => Assert.Contains(inspection.Files.References,
            reference => reference.SourceLogicalPath == group.SourceLogicalPath && reference.Location.NodePath == leaf.NodePath));
    }

    [Fact]
    public async Task SharedM1FormKeepsEveryApplicationWithoutChoosingStorageOwner()
    {
        var inspection = await Inspect(xml =>
        {
            var first = xml.Descendants("application").Single();
            var second = new XElement(first);
            second.SetAttributeValue("application-containing-files", "false");
            second.Descendants("application-number").Single().Value = "000123";
            first.AddAfterSelf(second);
            xml.Descendants("m1-1-forms").Single().Add(Form("fdaft5", "promotional"));
        });
        var group = Assert.Single(inspection.Files.Forms.Groups, item => item.FormType?.Value == "fdaft5");
        Assert.Equal("SharedM1", group.Scope);
        Assert.Equal("123456,000123", string.Join(',', group.Applications.SelectMany(app => app.Numbers).Select(number => number.Number)));
    }

    [Fact]
    public async Task DuplicateNumbersAndMissingTypeRemainRawCandidatesNotNormalizedIdentity()
    {
        var inspection = await Inspect(xml =>
        {
            var info = xml.Descendants("application-information").Single();
            info.Element("application-number")!.Value = " 123 ";
            info.Add(new XElement("application-number", "999999"));
            xml.Descendants("submission-information").Single().Add(Form("fdaft1", "raw"));
        });
        var group = Assert.Single(inspection.Files.Forms.Groups, item => item.Scope == "ApplicationAdmin");
        var numbers = Assert.Single(group.Applications).Numbers;
        Assert.Equal(" 123 ,999999", string.Join(',', numbers.Select(number => number.Number)));
        Assert.Null(numbers[1].Type);
    }

    [Fact]
    public async Task HistoricalFormsAreNotTargetGroups()
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, "history-1051");
        Assert.True(inspection.Files.Forms.InventoryComplete);
        Assert.NotEmpty(inspection.Files.Forms.Groups);
        Assert.All(inspection.Files.Forms.Groups, group => Assert.StartsWith("0001/", group.SourceLogicalPath));
    }

    [Fact]
    public async Task EmptyGroupsAndDeletedOrExtendedLeavesRemainCandidates()
    {
        var inspection = await Inspect(xml =>
        {
            var form = Form("fdaft1", "deleted", "attachment", "missing-href");
            form.Elements("leaf").First().SetAttributeValue("operation", "delete");
            form.Elements("leaf").Last().SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), null);
            var attachment = form.Elements("leaf").ElementAt(1);
            attachment.ReplaceWith(new XElement("node-extension", new XAttribute("ID", "extension"),
                new XElement("title", "Attachment"), new XElement(attachment)));
            xml.Descendants("submission-information").Single().Add(form);
        });
        Assert.Contains(inspection.Files.Forms.Groups, group => group.Leaves.Count == 0);
        var admin = Assert.Single(inspection.Files.Forms.Groups, group => group.Scope == "ApplicationAdmin");
        Assert.Equal("deleted,attachment,missing-href", string.Join(',', admin.Leaves.Select(leaf => leaf.Id)));
        Assert.Equal("delete", admin.Leaves[0].Operation);
        Assert.DoesNotContain(inspection.Files.References, reference => reference.Location.LeafId == "deleted");
        Assert.DoesNotContain(inspection.Files.References, reference => reference.Location.LeafId == "missing-href");
    }

    [Fact]
    public async Task WrongNamespaceApplicationNeverSuppliesAnOwner()
    {
        var inspection = await Inspect(xml =>
        {
            var application = xml.Descendants("application").Single();
            application.Descendants("submission-information").Single().Add(Form("fdaft1", "bad-owner"));
            application.Name = XName.Get("application", "urn:invalid");
        });
        Assert.False(inspection.Files.Forms.InventoryComplete);
        Assert.DoesNotContain(inspection.Files.Forms.Groups, group => group.Scope == "ApplicationAdmin");
    }

    [Theory]
    [InlineData("missing-regional")]
    [InlineData("wrong-location")]
    [InlineData("wrong-namespace")]
    public async Task UnavailableOrMisplacedFormsCannotProveCompleteInventory(string variant)
    {
        var inspection = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null,
            omittedTargetFile: variant == "missing-regional" ? "m1/us/us-regional.xml" : null,
            editRegional: variant == "missing-regional" ? null : xml =>
            {
                var form = Form("fdaft1", "invalid");
                if (variant == "wrong-namespace") form.Name = XName.Get("form", "urn:invalid");
                xml.Descendants(variant == "wrong-namespace" ? "m1-1-forms" : "m1-regional").Single().Add(form);
            });
        Assert.False(inspection.Files.Forms.InventoryComplete);
        Assert.DoesNotContain(inspection.Files.Forms.Groups, group => group.Leaves.Any(leaf => leaf.Id == "invalid"));
    }

    private static Task<PackageDeliveryInspection> Inspect(Action<XDocument> edit) =>
        FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, editRegional: edit);

    private static XElement Form(string code, params string[] ids) => new("form", new XAttribute("form-type", code),
        ids.Select(id => new XElement("leaf", new XAttribute("ID", id), new XAttribute("operation", "new"),
            new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), id + ".pdf"), new XElement("title", id))));
}
