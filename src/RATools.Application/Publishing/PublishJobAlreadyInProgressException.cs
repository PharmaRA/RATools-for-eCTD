namespace RATools.Application.Publishing;

public sealed class PublishJobAlreadyInProgressException(string message) : Exception(message);
