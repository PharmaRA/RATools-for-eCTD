using System.Collections.ObjectModel;

namespace RATools.Domain.Ctd;

public sealed class CtdNodeGraph
{
    private readonly Dictionary<Guid, CtdNodeInstance> _nodes;

    public CtdNodeGraph(Guid applicationId, SectionDefinitionSet definitions, IEnumerable<CtdNodeInstance>? nodes = null)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        ApplicationId = applicationId;
        Definitions = definitions;
        _nodes = (nodes ?? []).ToDictionary(node => node.Id);
        Nodes = new ReadOnlyDictionary<Guid, CtdNodeInstance>(_nodes);
        foreach (var node in _nodes.Values) ValidateParentChain(node);
        foreach (var siblings in _nodes.Values.GroupBy(node => (node.ParentInstanceId, node.DefinitionKey)))
            ValidateSiblings(siblings.ToArray());
    }

    public Guid ApplicationId { get; }
    public SectionDefinitionSet Definitions { get; }
    public IReadOnlyDictionary<Guid, CtdNodeInstance> Nodes { get; }

    public CtdNodeInstance Create(string definitionKey, Guid? parentInstanceId, IReadOnlyDictionary<string, string> attributes)
    {
        var node = CtdNodeInstance.Rehydrate(Guid.NewGuid(), ApplicationId, parentInstanceId, Definitions, definitionKey, attributes);
        Add(node);
        return node;
    }

    public void Add(CtdNodeInstance node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_nodes.ContainsKey(node.Id))
            throw new CtdNodeConstraintException("DuplicateNodeId", "The node identity is already present in the graph.", node.Id);
        ValidateParentChain(node);
        ValidateSiblings(_nodes.Values.Where(sibling => sibling.ParentInstanceId == node.ParentInstanceId && sibling.DefinitionKey == node.DefinitionKey)
            .Append(node).ToArray());
        _nodes.Add(node.Id, node);
    }

    public CtdNodeInstance Get(Guid id) => _nodes.TryGetValue(id, out var node) ? node
        : throw new CtdNodeConstraintException("NodeNotFound", "The node is not present in this application graph.", id);

    public string GetSectionPath(Guid id)
    {
        var node = Get(id);
        while (Definitions.Get(node.DefinitionKey).SectionPath is null) node = Get(node.ParentInstanceId!.Value);
        return Definitions.Get(node.DefinitionKey).SectionPath!;
    }

    public bool HasUnresolvedIdentity(Guid id)
    {
        var node = Get(id);
        while (true)
        {
            if (node.IdentityStatus != CtdIdentityStatus.Resolved) return true;
            if (node.ParentInstanceId is not { } parent) return false;
            node = Get(parent);
        }
    }

    private void ValidateParentChain(CtdNodeInstance node)
    {
        var visited = new HashSet<Guid>();
        while (true)
        {
            if (!visited.Add(node.Id))
                throw new CtdNodeConstraintException("NodeParentCycle", "The node parent chain contains a cycle.", node.Id);
            if (node.ApplicationId != ApplicationId)
                throw new CtdNodeConstraintException("NodeApplicationMismatch", "Parent and child nodes must belong to the same application.", node.Id);
            var definition = Definitions.Get(node.DefinitionKey);
            if (node.DefinitionVersion != Definitions.Version || node.IdentityComparisonVersion != Definitions.IdentityComparisonVersion || node.Kind != definition.Kind)
                throw new CtdNodeConstraintException("NodeDefinitionMismatch", "The node was created against a different identity or definition schema.", node.Id);
            if (node.ParentInstanceId is not { } parentId)
            {
                if (definition.ParentDefinitionKey is not null || definition.Kind == CtdNodeKind.Extension)
                    throw new CtdNodeConstraintException("NodeParentRequired", "This node definition requires a parent instance.", node.Id);
                return;
            }
            var parent = Get(parentId);
            var parentDefinition = Definitions.Get(parent.DefinitionKey);
            var allowed = node.Kind == CtdNodeKind.Extension
                ? parentDefinition.ExtensionPolicy == NodeExtensionPolicy.Allowed
                : definition.ParentDefinitionKey == parent.DefinitionKey;
            if (!allowed)
                throw new CtdNodeConstraintException("InvalidNodeParent", "The parent/child relationship is not allowed by the definition schema.", node.Id);
            node = parent;
        }
    }

    private void ValidateSiblings(IReadOnlyList<CtdNodeInstance> siblings)
    {
        if (siblings.Count < 2) return;
        if (!Definitions.Get(siblings[0].DefinitionKey).Repeatable)
            throw new CtdNodeConstraintException("NonRepeatableNodeConflict", "Only one instance of this definition is allowed under its parent.", siblings[0].Id);
        foreach (var duplicate in siblings.GroupBy(node => node.IdentityKey, StringComparer.Ordinal).Where(group => group.Count() > 1))
            if (duplicate.Any(node => node.IdentityStatus != CtdIdentityStatus.Ambiguous))
                throw new CtdNodeConstraintException("NodeIdentityConflict", "Equal sibling identities require an explicit ambiguous import mapping.", duplicate.First().Id);
    }
}
