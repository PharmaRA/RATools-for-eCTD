using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using RATools.Domain.Common;

namespace RATools.Domain.RegionalMetadata;

public sealed record RegionalTextEntry(Guid Id, string? Value);
public sealed record RegionalRelatedSequence(Guid Id, string SequenceNumber);
public sealed record RegionalTelephone(Guid Id, string? Number, string? Type);
public sealed record RegionalEmail(Guid Id, string? Value);
public sealed record RegionalContact(Guid Id, string? Name, string? Type, IReadOnlyList<RegionalTelephone> Phones, IReadOnlyList<RegionalEmail> Emails);
public sealed record UsRegionalMetadata(string? ApplicationIdentifier, string? ApplicationType, string? ApplicantName,
    IReadOnlyList<RegionalContact> Contacts, string? SubmissionType, string? SubmissionSubtype, string? FormType, string? Description);
public sealed record EuEnvelope(Guid Id, string? Identifier, string? Country, string? Agency, string? Procedure, string? SubmissionType,
    string? SubmissionMode, string? SubmissionNumber, string? SubmissionUnit, string? Applicant,
    IReadOnlyList<RegionalTextEntry> ProcedureTrackingNumbers, IReadOnlyList<RegionalTextEntry> InventedNames,
    IReadOnlyList<RegionalTextEntry> Inns, IReadOnlyList<RegionalRelatedSequence> RelatedSequences, string? Description);
public sealed record EuRegionalMetadata(IReadOnlyList<EuEnvelope> Envelopes);
public sealed record RegionalMetadataContent(int SchemaVersion, string RegionKey, UsRegionalMetadata? Us = null, EuRegionalMetadata? Eu = null);
public sealed record UsApplicationDefaults(string? ApplicationIdentifier, string? ApplicationType, string? ApplicantName, IReadOnlyList<RegionalContact> Contacts);
public sealed record EuApplicationDefaults(string? Applicant, IReadOnlyList<RegionalTextEntry> ProcedureTrackingNumbers,
    IReadOnlyList<RegionalTextEntry> InventedNames, IReadOnlyList<RegionalTextEntry> Inns);
public sealed record RegionalApplicationDefaultsContent(int SchemaVersion, string RegionKey, UsApplicationDefaults? Us = null, EuApplicationDefaults? Eu = null);
public sealed record RegionalNodeContext(Guid NodeInstanceId, string? Country, string? Language, string? ProductInformationType, string? FormType);
public sealed record RegionalDocumentContext(Guid PlacementId, Guid? NodeInstanceId, string? Country, string? Language, string? ProductInformationType, string? FormType);
public sealed record RegionalMetadataDiagnostic(string Code, string FieldPath, string Message);
public sealed record RegionalMetadataProvenance(string FieldPath, string Origin, Guid? SourceRevisionId = null, string? SourceDigest = null, string Status = "Unconfirmed");

public static class RegionalMetadataJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false };
    public static RegionalMetadataContent ReadSequence(string json) => Read<RegionalMetadataContent>(json);
    public static RegionalApplicationDefaultsContent ReadDefaults(string json) => Read<RegionalApplicationDefaultsContent>(json);
    private static T Read<T>(string json)
    {
        if (json.Length > 2 * 1024 * 1024) throw new ArgumentException("Regional metadata exceeds its input limit.", nameof(json));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        _ = CanonicalJson.Digest(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new ArgumentException("Regional metadata cannot be null.", nameof(json));
    }
    public static void VerifyBudget<T>(T value)
    { using var sink = new BudgetStream(); JsonSerializer.Serialize(sink, value, Options); }
    private sealed class BudgetStream : Stream
    {
        private long _count;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _count; public override long Position { get => _count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) { _count += count; if (_count > 2 * 1024 * 1024) throw new ArgumentException("Regional metadata exceeds its global 2 MiB byte budget."); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}

internal static class RegionalMetadataRules
{
    internal static void Text(string? value)
    {
        if (value is null) return;
        if (value.Length > 4096) throw new ArgumentException("Regional metadata text exceeds 4096 characters.");
        try { XmlConvert.VerifyXmlChars(value); }
        catch (XmlException error) { throw new ArgumentException("Regional metadata text contains characters forbidden in XML.", nameof(value), error); }
    }
    internal static void Id(Guid id) { if (id == Guid.Empty) throw new ArgumentException("Regional metadata identities cannot be empty."); }
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> items, Func<T, Guid> identity, Func<T, T>? copy = null)
    {
        if (items is null || items.Count > 1000 || items.Any(item => item is null)) throw new ArgumentException("Regional collections must be bounded and cannot contain null values.");
        var values = items.Select(item => copy is null ? item : copy(item)).ToArray();
        foreach (var value in values) Id(identity(value));
        if (values.Select(identity).Distinct().Count() != values.Length) throw new ArgumentException("Regional collection entry identities must be unique.");
        return Array.AsReadOnly(values);
    }
    internal static IReadOnlyList<RegionalTextEntry> Texts(IReadOnlyList<RegionalTextEntry> items) => Copy(items, item => item.Id, item => { Text(item.Value); return item with { }; });
    internal static IReadOnlyList<RegionalContact> Contacts(IReadOnlyList<RegionalContact> items) => Copy(items, item => item.Id, item =>
    {
        Text(item.Name); Text(item.Type);
        return item with { Phones = Copy(item.Phones, phone => phone.Id, phone => { Text(phone.Number); Text(phone.Type); return phone with { }; }),
            Emails = Copy(item.Emails, email => email.Id, email => { Text(email.Value); return email with { }; }) };
    });
    internal static void Tag(int schema, string region, bool us, bool eu)
    { if (schema != 1 || region is not ("US" or "EU") || us == eu || region == "US" != us) throw new ArgumentException("Regional metadata must use a supported schema and exactly its region's typed payload."); }
    internal static RegionalMetadataContent Freeze(RegionalMetadataContent content)
    {
        Tag(content.SchemaVersion, content.RegionKey, content.Us is not null, content.Eu is not null);
        if (content.Us is { } us)
        {
            foreach (var value in new[] { us.ApplicationIdentifier, us.ApplicationType, us.ApplicantName, us.SubmissionType, us.SubmissionSubtype, us.FormType, us.Description }) Text(value);
            return content with { Us = us with { Contacts = Contacts(us.Contacts) } };
        }
        return content with { Eu = new(Copy(content.Eu!.Envelopes, envelope => envelope.Id, envelope =>
        {
            foreach (var value in new[] { envelope.Identifier, envelope.Country, envelope.Agency, envelope.Procedure, envelope.SubmissionType, envelope.SubmissionMode,
                envelope.SubmissionNumber, envelope.SubmissionUnit, envelope.Applicant, envelope.Description }) Text(value);
            return envelope with { ProcedureTrackingNumbers = Texts(envelope.ProcedureTrackingNumbers), InventedNames = Texts(envelope.InventedNames), Inns = Texts(envelope.Inns),
                RelatedSequences = Copy(envelope.RelatedSequences, related => related.Id, related => { RATools.Domain.PackageValidation.PackageInputManifest.RequireSequenceNumber(related.SequenceNumber); return related with { }; }) };
        })) };
    }
    internal static RegionalApplicationDefaultsContent Freeze(RegionalApplicationDefaultsContent content)
    {
        Tag(content.SchemaVersion, content.RegionKey, content.Us is not null, content.Eu is not null);
        if (content.Us is { } us)
        { foreach (var value in new[] { us.ApplicationIdentifier, us.ApplicationType, us.ApplicantName }) Text(value); return content with { Us = us with { Contacts = Contacts(us.Contacts) } }; }
        var eu = content.Eu!; Text(eu.Applicant);
        return content with { Eu = eu with { ProcedureTrackingNumbers = Texts(eu.ProcedureTrackingNumbers), InventedNames = Texts(eu.InventedNames), Inns = Texts(eu.Inns) } };
    }
}
