using System.Text.Json.Serialization;
using RATools.Domain.RegionalMetadata;

namespace RATools.Api.Contracts;

[JsonConverter(typeof(StrictRegionalRequestConverter<RegionalDefaultsRequestBody>))]
public sealed class RegionalDefaultsRequestBody
{
    public long? ExpectedDefaultsRevision { get; set; }
    public string StandardsSnapshotId { get; set; } = "";
    public RegionalApplicationDefaultsContent Defaults { get; set; } = null!;
}
[JsonConverter(typeof(StrictRegionalRequestConverter<RegionalPreflightRequestBody>))]
public sealed class RegionalPreflightRequestBody
{
    public string StandardsSnapshotId { get; set; } = "";
    public RegionalMetadataContent Content { get; set; } = null!;
    public IReadOnlyList<RegionalNodeContext>? NodeContexts { get; set; }
    public IReadOnlyList<RegionalDocumentContext>? DocumentContexts { get; set; }
}
[JsonConverter(typeof(StrictRegionalRequestConverter<RegionalInheritanceRequestBody>))]
public sealed class RegionalInheritanceRequestBody
{
    public long? ExpectedRevision { get; set; }
    public Guid DefaultsRevisionId { get; set; }
}
[JsonConverter(typeof(StrictRegionalRequestConverter<RegionalNodeContextCopyRequestBody>))]
public sealed class RegionalNodeContextCopyRequestBody
{
    public long? ExpectedRevision { get; set; }
    public Guid SourceRevisionId { get; set; }
    public Guid NodeInstanceId { get; set; }
    public IReadOnlyList<Guid> PlacementIds { get; set; } = [];
}
[JsonConverter(typeof(StrictRegionalRequestConverter<RegionalLegacyUpgradeRequestBody>))]
public sealed class RegionalLegacyUpgradeRequestBody
{
    public long? ExpectedRevision { get; set; }
}
