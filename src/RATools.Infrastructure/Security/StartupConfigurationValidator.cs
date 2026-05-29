using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Storage;

namespace RATools.Infrastructure.Security;

public sealed class StartupConfigurationValidator
{
    private readonly IWorkspacePathPolicy _policy;
    private readonly IOptions<FileStorageOptions> _fileStorageOptions;
    private readonly IOptions<BackboneOutputOptions> _backboneOptions;

    public StartupConfigurationValidator(
        IWorkspacePathPolicy policy,
        IOptions<FileStorageOptions> fileStorageOptions,
        IOptions<BackboneOutputOptions> backboneOptions)
    {
        _policy = policy;
        _fileStorageOptions = fileStorageOptions;
        _backboneOptions = backboneOptions;
    }

    public void Validate()
    {
        if (_policy.GetAllowedRoots().Count == 0)
        {
            throw new InvalidOperationException("Security:AllowedWorkspaceRoots must not be empty.");
        }

        EnsureAllowedConfigurationPath("FileStorage:RootPath", _fileStorageOptions.Value.RootPath);
        EnsureAllowedConfigurationPath("BackboneOutput:RootPath", _backboneOptions.Value.RootPath);
    }

    private void EnsureAllowedConfigurationPath(string configurationKey, string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            _policy.EnsureAllowed(fullPath);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"{configurationKey} '{fullPath}' is not inside Security:AllowedWorkspaceRoots: {ex.Message}",
                ex);
        }
    }
}
