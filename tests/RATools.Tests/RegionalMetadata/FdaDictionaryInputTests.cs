using RATools.Application.PackageValidation;
using RATools.Application.Publishing.UsRegional;
using RATools.Domain.PackageValidation;
using RATools.Domain.RegionalMetadata;
using RATools.Tests.PackageValidation;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.RegionalMetadata;

public sealed class FdaDictionaryInputTests
{
    private static RegionalMetadataContent Content(string application, string contact, string phone) => new(1, "US", Us: new("000001", application, "Applicant",
        [new(Guid.NewGuid(), "Contact", contact, [new(Guid.NewGuid(), "5550100", phone)], [new(Guid.NewGuid(), "synthetic@example.test")])],
        "fdast1", "fdasst3", null, "Submission", "001234567", "0001"));
    [Fact]
    public async Task SequenceAndDefaultsOfferIdenticalCapturedOfficialCodes()
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var schema = await rig.Service.GetSchemaAsync(rig.App.Id, rig.Snapshot.Id);
        foreach (var fields in new[] { schema.SequenceFields.Single().Fields, schema.DefaultsFields.Single().Fields })
        {
            var application = fields.Single(item => item.Key == "applicationType");
            Assert.Equal(6, application.Options.Count); Assert.Contains("fdaat1", application.Options); Assert.Contains("fdaat6", application.Options);
            Assert.DoesNotContain("fdaat8", application.Options); Assert.Equal("official-fda-application-types", application.Source!.ResourceKey);
            var contacts = fields.Single(item => item.Key == "contacts").Fields;
            var contact = contacts.Single(item => item.Key == "type");
            Assert.Contains("fdaact1", contact.Options); Assert.DoesNotContain("regulatory", contact.Options);
            Assert.Equal("official-fda-applicant-contact-types", contact.Source!.ResourceKey);
            var phone = contacts.Single(item => item.Key == "phones").Fields.Single(item => item.Key == "type");
            Assert.Contains("fdatnt1", phone.Options); Assert.Equal("official-fda-telephone-types", phone.Source!.ResourceKey);
        }
    }

    [Theory]
    [InlineData("nda", "fdaact1", "fdatnt1", "us.applicationType")]
    [InlineData("fdaat8", "fdaact1", "fdatnt1", "us.applicationType")]
    [InlineData("fdaat1 ", "fdaact1", "fdatnt1", "us.applicationType")]
    [InlineData("fdaat1", "regulatory", "fdatnt1", "us.contacts[0].type")]
    [InlineData("fdaat1", "FDAACT1", "fdatnt1", "us.contacts[0].type")]
    [InlineData("fdaat1", "fdaact1", "business", "us.contacts[0].phones[0].type")]
    [InlineData("fdaat1", "fdaact1", "fdatnt1 ", "us.contacts[0].phones[0].type")]
    public async Task InvalidCodesAreRetainedAndLocatedInSequenceAndDefaults(string application, string contact, string phone, string path)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var content = Content(application, contact, phone);
        var saved = await rig.Save(content);
        Assert.Contains(saved.Diagnostics, item => item.Code == "OfficialDictionaryValueInvalid" && item.FieldPath == path);
        Assert.Equal(application, saved.Content!.Us!.ApplicationType);
        Assert.Equal(contact, saved.Content.Us.Contacts[0].Type);
        Assert.Equal(phone, saved.Content.Us.Contacts[0].Phones[0].Type);
        var defaults = new RegionalApplicationDefaultsContent(1, "US", Us: new("000001", application, "Applicant", content.Us!.Contacts));
        var preview = await rig.Service.PreflightDefaultsAsync(rig.App.Id, rig.Snapshot.Id, defaults);
        Assert.Contains(preview.Diagnostics, item => item.Code == "OfficialDictionaryValueInvalid" && item.FieldPath == path);
    }

    [Fact]
    public async Task OfficialSelectionsSurviveSaveGenerationAndIndependentValidation()
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var saved = await rig.Save(Content("fdaat1", "fdaact1", "fdatnt1"));
        Assert.DoesNotContain(saved.Diagnostics, item => item.Code == "OfficialDictionaryValueInvalid");
        var xml = new UsRegionalXmlWriter().Write(await rig.Builder().BuildAsync(new(rig.App.Id, "0001")));
        Assert.Equal("fdaat1", xml.Document.Descendants("application-number").Single().Attribute("application-type")!.Value);
        var result = await PackageRegionalInspectionTests.Inspect(xml.XmlContent, context: PackageStandardsContext.FromSnapshot(rig.Snapshot, selectionDate: new(2026, 11, 17)));
        foreach (var id in new[] { "FDA47-1496", "FDA47-2013" }) Assert.Equal(CheckStatus.Pass, Assert.Single(result.Checks, item => item.RuleId == id).Status);
    }

    [Theory]
    [InlineData("fda-m1-attribute-policy")]
    [InlineData("official-fda-application-types")]
    [InlineData("official-fda-applicant-contact-types")]
    [InlineData("official-fda-telephone-types")]
    public async Task MissingArchivedSourcesNeverBorrowTodaysDictionaryOptions(string missing)
    {
        var rig = await ApplicantIdentifierTests.Rig.Create(); var original = rig.Snapshot.Resources.Single(item => item.Key == missing);
        var descriptor = rig.Snapshot.Descriptor with { Provenance = rig.Snapshot.Descriptor.Provenance.Where(item => item.Sha256 != original.Sha256).ToArray() };
        var legacy = StandardsProfileSnapshot.Create(descriptor, rig.Snapshot.Resources.Where(item => item.Key != missing), rig.Snapshot.AcquiredUtc);
        var store = new InMemoryStandardsSnapshotStore(); await store.AddAsync(legacy);
        var transaction = new InMemoryPersistenceTransaction();
        var service = new RegionalMetadataService(rig.Store, rig.Apps, rig.Nodes, rig.Placements,
            new StandardsSnapshotService(store, new FdaEctd322StandardsProfileProvider()),
            new WorkspaceMutationCoordinator(rig.Revisions, transaction), rig.Revisions, transaction);
        var schema = await service.GetSchemaAsync(rig.App.Id, legacy.Id);
        var field = schema.SequenceFields.Single().Fields.Single(item => item.Key == "applicationType");
        Assert.Equal("text", field.Kind); Assert.Empty(field.Options);
        var preview = await service.PreflightAsync(rig.App.Id, "0001", new(legacy.Id, Content("fdaat1", "fdaact1", "fdatnt1")));
        Assert.Contains(preview.Diagnostics, item => item.Code == "VocabularyUnverified" && item.FieldPath == "us.applicationType");
        Assert.DoesNotContain(preview.Diagnostics, item => item.Code == "OfficialDictionaryValueInvalid");
    }
}
