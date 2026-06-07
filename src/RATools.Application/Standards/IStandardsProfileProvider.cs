namespace RATools.Application.Standards;

public sealed class StandardsProfileNotFoundException(string message) : Exception(message);

public sealed class StandardsAssetMissingException(string message) : Exception(message);

public interface IStandardsProfileProvider
{
    StandardsProfile GetProfile(string templateKey);
}
