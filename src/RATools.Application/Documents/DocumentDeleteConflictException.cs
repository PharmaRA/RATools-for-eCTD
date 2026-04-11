namespace RATools.Application.Documents;

public sealed class DocumentDeleteConflictException(string message) : Exception(message);
