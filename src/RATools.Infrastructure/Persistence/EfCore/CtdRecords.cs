namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class CtdDefinitionRecord
{
    public string Version { get; set; } = string.Empty;
    public string DefinitionKey { get; set; } = string.Empty;
    public string? ParentDefinitionKey { get; set; }
    public string? SectionPath { get; set; }
    public bool Repeatable { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool AllowsLeaves { get; set; }
    public string ExtensionPolicy { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
}

public sealed class CtdNodeInstanceRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? ParentInstanceId { get; set; }
    public string DefinitionVersion { get; set; } = string.Empty;
    public string DefinitionKey { get; set; } = string.Empty;
    public bool Repeatable { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public string IdentityComparisonVersion { get; set; } = string.Empty;
    public string IdentityStatus { get; set; } = string.Empty;
    public string IdentityAttributesJson { get; set; } = "{}";
}

public sealed class SequenceNodeRecord
{
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = string.Empty;
    public Guid NodeInstanceId { get; set; }
    public Guid? ParentNodeInstanceId { get; set; }
    public string DefinitionVersion { get; set; } = string.Empty;
    public string CtdSection { get; set; } = string.Empty;
    public string AttributesJson { get; set; } = "{}";
    public string? Title { get; set; }
    public int SortOrder { get; set; }
    public string? StorageSegment { get; set; }
    public string MetadataStatus { get; set; } = string.Empty;
}

public sealed class NodeBackfillCheckpointRecord
{
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InputDigest { get; set; } = string.Empty;
    public int PlacementCount { get; set; }
    public int UnresolvedCount { get; set; }
    public DateTime CompletedUtc { get; set; }
}

public sealed class NodeBackfillDiagnosticRecord
{
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = string.Empty;
    public Guid PlacementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
