namespace RATools.Application.Validation;

public interface IValidationProfileProvider
{
    string ProfileName { get; }

    ValidationMode Mode { get; }
}
