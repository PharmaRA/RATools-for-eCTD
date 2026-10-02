using System.Text.Json;
using RATools.Application.Abstractions.Security;
using RATools.Domain.Applications;

namespace RATools.Application.Ctd;

public sealed record NodeFileMoveIntent(Guid PlacementId, Guid DocumentId, Guid ApplicationId, string SequenceNumber,
    long ExpectedRevision, Guid? SourceNodeId, string SourceSection, int SourceSortOrder, Guid TargetNodeId,
    string TargetSection, int TargetSortOrder, string SourcePath, string TargetPath, long Length, string Sha256);

/// <summary>Durable prepare record, outside sequence payloads; all access stays in the approved workspace.</summary>
public sealed class NodeFileMoveJournal(IWorkspacePathPolicy policy)
{
    private string DirectoryPath(SubmissionApplication application) => policy.EnsureAllowed(
        Path.Combine(application.WorkingDirectoryPath, ".ratools", "node-moves"));

    public void Prepare(SubmissionApplication application, NodeFileMoveIntent intent)
    {
        var directory = DirectoryPath(application);
        Directory.CreateDirectory(directory);
        var path = policy.EnsureAllowed(Path.Combine(directory, intent.PlacementId.ToString("N") + ".json"));
        // Write through a temporary file: recovery never mistakes a partial JSON write for a move.
        var temporary = policy.EnsureAllowed(path + ".preparing");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, intent);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: false);
    }

    public IReadOnlyList<NodeFileMoveIntent> Read(SubmissionApplication application)
    {
        var directory = DirectoryPath(application);
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json").Select(path =>
        {
            var intent = JsonSerializer.Deserialize<NodeFileMoveIntent>(File.ReadAllText(policy.EnsureAllowed(path)))
                ?? throw new InvalidOperationException("Invalid pending node move record.");
            if (intent.ApplicationId != application.Id || Path.GetFileName(path) != intent.PlacementId.ToString("N") + ".json")
                throw new InvalidOperationException("Pending node move record has an invalid workspace identity.");
            return intent;
        }).ToArray();
    }

    public void Complete(SubmissionApplication application, Guid placementId) => File.Delete(policy.EnsureAllowed(
        Path.Combine(DirectoryPath(application), placementId.ToString("N") + ".json")));
}
