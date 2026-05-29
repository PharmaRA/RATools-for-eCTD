namespace RATools.Application.Applications;

public sealed class ApplicationNumberAlreadyExistsException : Exception
{
    public ApplicationNumberAlreadyExistsException(string message) : base(message) { }

    public ApplicationNumberAlreadyExistsException(string message, Exception innerException) : base(message, innerException) { }
}
