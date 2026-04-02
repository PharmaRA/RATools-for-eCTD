namespace RATools.Application.Publishing;

public sealed class PublishJobNotReadyException(string message) : Exception(message);
