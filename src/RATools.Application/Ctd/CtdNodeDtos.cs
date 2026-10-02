using System.ComponentModel.DataAnnotations;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Ctd;

namespace RATools.Application.Ctd;

public sealed record CtdNodeDto(Guid NodeInstanceId, Guid? ParentInstanceId, string DefinitionKey,
    string CtdSection, IReadOnlyDictionary<string, string> Attributes, string? Title, int SortOrder,
    string? StorageSegment, string MetadataStatus, string IdentityStatus, bool AllowsLeaves,
    IReadOnlyList<NodeSchemaIssue> Issues);

public sealed record CtdNodeTreeDto(Guid ApplicationId, string SequenceNumber, long WorkspaceRevision,
    string DefinitionVersion, IReadOnlyList<SectionDefinition> Definitions, IReadOnlyList<CtdNodeDto> Nodes,
    IReadOnlyList<CtdBackfillDiagnostic> Diagnostics);

public sealed class CreateCtdNodeRequest
{
    public long? ExpectedRevision { get; init; }
    [Required] public string DefinitionKey { get; init; } = "";
    public Guid? ParentInstanceId { get; init; }
    [Required] public Dictionary<string, string> Attributes { get; init; } = [];
    [StringLength(512)] public string? Title { get; init; }
    [Range(0, int.MaxValue)] public int SortOrder { get; init; }
    [StringLength(64), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] public string? StorageSegment { get; init; }
}

public sealed class UpdateCtdNodeRequest
{
    public long? ExpectedRevision { get; init; }
    [Required] public Dictionary<string, string> Attributes { get; init; } = [];
    [StringLength(512)] public string? Title { get; init; }
    [Range(0, int.MaxValue)] public int SortOrder { get; init; }
    [StringLength(64), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] public string? StorageSegment { get; init; }
}

public sealed record InheritCtdNodesRequest(string SourceSequenceNumber, long? ExpectedRevision);
public sealed record CtdNodeClonePreview(long WorkspaceRevision, IReadOnlyList<CtdNodeDto> Nodes,
    IReadOnlyDictionary<Guid, string> Directories);
