using RATools.Domain.RegionalMetadata;

namespace RATools.Tests.RegionalMetadata;

public sealed class RegionalMetadataModelTests
{
    [Fact]
    public void OrderedEnvelopesAndNestedValuesAreCopiedIntoImmutableEvidence()
    {
        var names = new List<RegionalTextEntry> { new(Guid.NewGuid(), "First"), new(Guid.NewGuid(), "Second") };
        var envelope = Envelope() with { InventedNames = names };
        var envelopes = new List<EuEnvelope> { envelope, Envelope() with { Country = "de" } };
        var revision = Revision(new(1, "EU", Eu: new(envelopes)));
        names.Clear(); envelopes.Clear();
        Assert.Equal(2, revision.Content!.Eu!.Envelopes.Count);
        Assert.Equal(new[] { "First", "Second" }, revision.Content.Eu.Envelopes[0].InventedNames.Select(value => value.Value));
        Assert.Equal("de", revision.Content.Eu.Envelopes[1].Country); revision.Verify();
    }

    [Theory]
    [InlineData(2, "EU")]
    [InlineData(1, "XX")]
    [InlineData(1, "US")]
    public void UnknownSchemaRegionOrMismatchedPayloadIsRejected(int schema, string region)
    {
        Assert.Throws<ArgumentException>(() => Revision(new(schema, region, Eu: new([Envelope()]))));
    }

    [Fact]
    public void RepeatedEntryIdentityIsRejectedButEqualBusinessValuesRemainOrdered()
    {
        var first = new RegionalTextEntry(Guid.NewGuid(), "same"); var second = new RegionalTextEntry(Guid.NewGuid(), "same");
        var valid = Revision(new(1, "EU", Eu: new([Envelope() with { InventedNames = [first, second] }])));
        Assert.Equal(2, valid.Content!.Eu!.Envelopes[0].InventedNames.Count);
        Assert.Throws<ArgumentException>(() => Revision(new(1, "EU", Eu: new([Envelope() with { InventedNames = [first, first] }]))));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"regionKey\":\"EU\",\"eu\":{\"envelopes\":[]}}")]
    [InlineData("{\"schemaVersion\":1,\"regionKey\":\"EU\",\"eu\":{\"envelopes\":[],\"unknown\":true}}")]
    [InlineData("{\"schemaVersion\":1,\"SchemaVersion\":1,\"regionKey\":\"EU\",\"eu\":{\"envelopes\":[]}}")]
    [InlineData("{\"schemaVersion\":1,\"regionKey\":\"US\",\"us\":{\"contacts\":[],\"Contacts\":[]}}")]
    public void DuplicateOrUnknownJsonFieldsCannotBypassTypedSchema(string json)
    { Assert.ThrowsAny<Exception>(() => RegionalMetadataJson.ReadSequence(json)); }

    [Fact]
    public void APartialDraftRetainsMissingValuesAndDoesNotClaimRegulatoryReadiness()
    {
        var revision = Revision(new(1, "EU", Eu: new([Envelope() with { Applicant = null, Country = null }])));
        Assert.Null(revision.Content!.Eu!.Envelopes[0].Applicant); Assert.False(revision.IsReady);
    }

    [Fact]
    public void NestedTypedObjectsCannotBypassTheGlobalPayloadBudget()
    {
        var names = Enumerable.Range(0, 600).Select(_ => new RegionalTextEntry(Guid.NewGuid(), new string('x', 4096))).ToArray();
        Assert.Throws<ArgumentException>(() => Revision(new(1, "EU", Eu: new([Envelope() with { InventedNames = names }]))));
    }

    internal static EuEnvelope Envelope() => new(Guid.NewGuid(), "identifier", "fr", "agency", "procedure", "submission", null, null,
        "initial", "Applicant", [new(Guid.NewGuid(), "track-1")], [new(Guid.NewGuid(), "Name")], [], [new(Guid.NewGuid(), "0001")], "Description");
    internal static RegionalMetadataRevision Revision(RegionalMetadataContent content) => RegionalMetadataRevision.Create(Guid.NewGuid(), Guid.NewGuid(), "0001", 1, 1,
        "sha256:" + new string('a', 64), content, null, [], [], null, null, [], [], null, null, DateTime.UtcNow);
}
