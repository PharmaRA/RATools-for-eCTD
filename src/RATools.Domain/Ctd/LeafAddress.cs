using System.Xml;
using RATools.Domain.Common;

namespace RATools.Domain.Ctd;

public sealed record LeafAddress
{
    public LeafAddress(Guid applicationId, string sequenceNumber, string backboneRelativePath, string leafId)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        if (sequenceNumber is null || sequenceNumber.Length != 4 || sequenceNumber.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("A sequence number must contain exactly four ASCII digits.", nameof(sequenceNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(backboneRelativePath);
        // This is a decoded physical file component. A literal '#' in a file
        // name is escaped by BackboneUri; the leaf fragment is stored separately.
        if (!backboneRelativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A backbone must be a logical XML file path.", nameof(backboneRelativePath));
        foreach (var segment in backboneRelativePath.Split('/'))
            if (PortablePathSegment.NormalizeAndValidate(segment, nameof(backboneRelativePath)) != segment)
                throw new ArgumentException("A backbone path must retain its exact valid segments.", nameof(backboneRelativePath));
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber;
        BackboneRelativePath = backboneRelativePath;
        LeafId = XmlConvert.VerifyName(leafId);
    }

    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public string BackboneRelativePath { get; }
    public string LeafId { get; }

    public string BuildModifiedFileHref(LeafAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.ApplicationId != ApplicationId)
            throw new CtdNodeConstraintException("LifecycleApplicationMismatch", "Lifecycle addresses must belong to the same application.");
        if (string.CompareOrdinal(SequenceNumber, source.SequenceNumber) > 0)
            throw new CtdNodeConstraintException("LifecycleTargetInFuture", "A lifecycle target cannot be in a later sequence.");
        var sourceDirectory = new Uri(source.BackboneUri(), ".");
        return sourceDirectory.MakeRelativeUri(new Uri(BackboneUri().AbsoluteUri + "#" + Uri.EscapeDataString(LeafId))).ToString();
    }

    private Uri BackboneUri() => new("https://ectd.invalid/" + SequenceNumber + "/" +
        string.Join('/', BackboneRelativePath.Split('/').Select(Uri.EscapeDataString)));
}
