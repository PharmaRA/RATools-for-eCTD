namespace RATools.Application.Applications;

public sealed class SequenceDeleteConflictException(string message) : Exception(message);
