namespace RATools.Application.Publishing.EuRegional;

public sealed class EuRegionalXmlWriterException(string message, string? fieldPath = null) : Exception(message)
{
    public string? FieldPath { get; } = fieldPath;
}
