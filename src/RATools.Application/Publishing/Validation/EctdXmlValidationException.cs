namespace RATools.Application.Publishing.Validation;

public sealed class EctdXmlValidationException(string relativePath, string reason, Exception? innerException = null)
    : Exception($"eCTD XML '{relativePath}' failed DTD validation: {reason}", innerException)
{
    public string RelativePath { get; } = relativePath;

    public string Reason { get; } = reason;
}
