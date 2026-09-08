using System.Collections.Frozen;
using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Domain.Ctd;

public enum CtdIdentityStatus { Resolved, MissingMetadata, Ambiguous }
public enum NodeMetadataStatus { Complete, NeedsMetadataCompletion, LegacyUnresolved }

public sealed class CtdNodeConstraintException(string code, string message, Guid? nodeInstanceId = null) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public Guid? NodeInstanceId { get; } = nodeInstanceId;
}

public sealed class CtdNodeInstance : Entity
{
    private CtdNodeInstance(Guid id, Guid applicationId, Guid? parentInstanceId, SectionDefinitionSet definitions,
        string definitionKey, IReadOnlyDictionary<string, string> attributes, CtdIdentityStatus identityStatus)
    {
        if (id == Guid.Empty || applicationId == Guid.Empty || parentInstanceId == Guid.Empty)
            throw new ArgumentException("Node and application identities must be nonempty.");
        if (!Enum.IsDefined(identityStatus)) throw new ArgumentOutOfRangeException(nameof(identityStatus));
        var definition = definitions.Get(definitionKey);
        var errors = definition.ValidateAttributes(attributes);
        if (errors.Any(error => identityStatus == CtdIdentityStatus.Resolved || error.Code != "RequiredNodeAttributeMissing"))
            throw new CtdNodeConstraintException("InvalidNodeAttributes", string.Join(" | ", errors.Select(error => error.Message)), id);
        Id = id;
        ApplicationId = applicationId;
        ParentInstanceId = parentInstanceId;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitions.Version;
        Kind = definition.Kind;
        IdentityStatus = identityStatus;
        IdentityComparisonVersion = definitions.IdentityComparisonVersion;
        var names = definition.Attributes.Where(attribute => attribute.Identity).Select(attribute => attribute.Name).ToHashSet(StringComparer.Ordinal);
        IdentityAttributes = attributes.Where(pair => names.Contains(pair.Key)).ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // A null value represents an absent attribute. Extension identity is its explicit ID.
        var identityValues = definition.Attributes.Where(attribute => attribute.Identity).OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Select(attribute => new string?[] { attribute.Name, IdentityAttributes.GetValueOrDefault(attribute.Name) }).ToArray();
        IdentityKey = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new object?[]
        {
            ApplicationId.ToString("D"), ParentInstanceId?.ToString("D"), DefinitionKey,
            IdentityComparisonVersion, identityValues, Kind == CtdNodeKind.Extension ? Id.ToString("D") : null
        }));
    }

    public static CtdNodeInstance Rehydrate(Guid id, Guid applicationId, Guid? parentInstanceId, SectionDefinitionSet definitions,
        string definitionKey, IReadOnlyDictionary<string, string> attributes, CtdIdentityStatus identityStatus = CtdIdentityStatus.Resolved,
        string? expectedIdentityKey = null)
    {
        var instance = new CtdNodeInstance(id, applicationId, parentInstanceId, definitions, definitionKey, attributes, identityStatus);
        if (expectedIdentityKey is not null && expectedIdentityKey != instance.IdentityKey)
            throw new CtdNodeConstraintException("NodeIdentityMismatch", "Persisted node identity differs from its declared attributes.", id);
        return instance;
    }

    public Guid ApplicationId { get; }
    public Guid? ParentInstanceId { get; }
    public string DefinitionKey { get; }
    public string DefinitionVersion { get; }
    public CtdNodeKind Kind { get; }
    public string IdentityKey { get; }
    public string IdentityComparisonVersion { get; }
    public IReadOnlyDictionary<string, string> IdentityAttributes { get; }
    public CtdIdentityStatus IdentityStatus { get; }
}
