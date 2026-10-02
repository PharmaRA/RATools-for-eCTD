using RATools.Application.Abstractions.Persistence;
using RATools.Application.Validation;
using RATools.Domain.Ctd;
using RATools.Domain.Common;
using System.Text.RegularExpressions;

namespace RATools.Application.Ctd;

/// <summary>Allocates new workspace paths. Imported hrefs are never rewritten here.</summary>
public sealed partial class CtdNodePathResolver(IEctdWorkspacePathResolver sections)
{
    public string Resolve(string templateKey, CtdSequenceWorkspace workspace, Guid nodeId)
    {
        _ = RATools.Application.Applications.EctdTemplates.EctdTemplateRegistry.Resolve(templateKey);
        var nodes = workspace.Nodes.ToDictionary(node => node.NodeInstanceId);
        var chain = new Stack<CtdNodeInstance>();
        var current = workspace.Graph.Get(nodeId);
        while (true)
        {
            chain.Push(current);
            if (current.ParentInstanceId is not { } parent) break;
            current = workspace.Graph.Get(parent);
        }
        var canonical = "";
        var result = "";
        foreach (var instance in chain)
        {
            if (!nodes.TryGetValue(instance.Id, out var node))
                throw Error("SequenceNodeParentMissing", "The selected sequence does not contain the complete ancestor chain.", instance.Id);
            if (node.ApplicationId != workspace.Graph.ApplicationId || node.SequenceNumber != workspace.SequenceNumber ||
                node.DefinitionVersion != workspace.Graph.Definitions.Version)
                throw Error("SequenceNodeScopeMismatch", "The node must belong to the selected sequence and definition version.", instance.Id);
            var definition = workspace.Graph.Definitions.Get(instance.DefinitionKey);
            if (node.MetadataStatus != NodeMetadataStatus.Complete)
                throw Error("NodeMetadataIncomplete", "Complete node identity and required attributes before allocating a path.", instance.Id);
            if (definition.Kind != CtdNodeKind.Extension)
            {
                var next = IchNodePathProfile.Paths.TryGetValue(instance.DefinitionKey, out var ichPath)
                    ? ichPath : sections.Resolve(templateKey, node.CtdSection).RelativeFolderPath.Replace('\\', '/').Trim('/');
                if (canonical.Length > 0 && next != canonical && !next.StartsWith(canonical + "/", StringComparison.Ordinal))
                    throw Error("NodePathTemplateMismatch", "The canonical directory must extend its ancestor directory.", instance.Id);
                result += next[canonical.Length..];
                canonical = next;
            }
            if (definition.Repeatable || definition.Kind == CtdNodeKind.Extension)
            {
                var segment = node.StorageSegment;
                if (string.IsNullOrEmpty(segment) || segment.Length > 64 || !SegmentPattern().IsMatch(segment))
                    throw Error("NodeStorageSegmentRequired", "Repeatable nodes require a stable lowercase ASCII directory segment (letters, digits, hyphen; at most 64 characters).", instance.Id);
                _ = PortablePathSegment.NormalizeAndValidate(segment, nameof(node.StorageSegment));
                result += "/" + segment;
            }
            else if (node.StorageSegment is not null)
                throw Error("NodeStorageSegmentForbidden", "A singleton section uses its canonical directory without an instance segment.", instance.Id);
        }
        if (result.Length > 230) throw Error("NodePathTooLong", "The relative delivery path exceeds 230 characters.", nodeId);
        return result;
    }

    public string ResolveFile(string templateKey, CtdSequenceWorkspace workspace, Guid nodeId, string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length > 64 || !FileNamePattern().IsMatch(fileName))
            throw Error("NodeFileNameInvalid", "Use a lowercase eCTD file name of at most 64 characters, including its extension, without directory components.", nodeId);
        _ = PortablePathSegment.NormalizeAndValidate(fileName, nameof(fileName));
        var path = Resolve(templateKey, workspace, nodeId) + "/" + fileName;
        if (path.Length > 230) throw Error("NodePathTooLong", "The relative delivery path exceeds 230 characters.", nodeId);
        return path;
    }

    public void ValidateAllocation(string templateKey, CtdSequenceWorkspace workspace, bool ignoreIncomplete = false)
    {
        var allocated = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in workspace.Nodes)
        {
            var instance = workspace.Graph.Get(node.NodeInstanceId);
            var definition = workspace.Graph.Definitions.Get(instance.DefinitionKey);
            if (!definition.Repeatable && definition.Kind != CtdNodeKind.Extension) continue;
            string path;
            try { path = Resolve(templateKey, workspace, node.NodeInstanceId); }
            catch (CtdNodeConstraintException error) when (ignoreIncomplete && error.Code is "NodeMetadataIncomplete" or "NodeStorageSegmentRequired") { continue; }
            if (!allocated.TryAdd(path, node.NodeInstanceId))
                throw Error("NodeDirectoryConflict", "Two business instances would use the same directory. Choose a distinct stable segment.", node.NodeInstanceId);
            if (path.Length > 230) throw Error("NodePathTooLong", "The relative delivery path exceeds 230 characters.", node.NodeInstanceId);
        }
    }

    private static CtdNodeConstraintException Error(string code, string message, Guid id) => new(code, message, id);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SegmentPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+)+$")]
    private static partial Regex FileNamePattern();
}
