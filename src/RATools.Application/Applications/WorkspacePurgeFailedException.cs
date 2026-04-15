namespace RATools.Application.Applications;

public sealed class WorkspacePurgeFailedException(string message, Exception innerException) : Exception(message, innerException);
