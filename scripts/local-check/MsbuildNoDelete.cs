using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

// Task-only fail-closed substitutes. No filesystem delete API is called.
namespace Microsoft.Build.Tasks;

public sealed class Delete : Task
{
    public ITaskItem[] Files { get; set; } = [];
    public bool TreatErrorsAsWarnings { get; set; }
    [Output] public ITaskItem[] DeletedFiles { get; set; } = [];
    public override bool Execute()
    {
        if (Files.Length == 0) return true;
        Log.LogError("QA_NO_DELETE: MSBuild requested file deletion; retain the target and review the owning task.");
        return false;
    }
}

public sealed class RemoveDir : Task
{
    public ITaskItem[] Directories { get; set; } = [];
    [Output] public ITaskItem[] RemovedDirectories { get; set; } = [];
    public override bool Execute()
    {
        if (Directories.Length == 0) return true;
        Log.LogError("QA_NO_DELETE: MSBuild requested directory deletion; retain the target and review the owning task.");
        return false;
    }
}
