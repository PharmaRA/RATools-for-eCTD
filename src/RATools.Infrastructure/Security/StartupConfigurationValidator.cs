using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Storage;

namespace RATools.Infrastructure.Security;

public sealed class StartupConfigurationValidator
{
    private readonly IWorkspacePathPolicy _policy;
    private readonly IOptions<SecurityOptions> _securityOptions;
    private readonly IOptions<FileStorageOptions> _fileStorageOptions;
    private readonly IOptions<BackboneOutputOptions> _backboneOptions;

    public StartupConfigurationValidator(
        IWorkspacePathPolicy policy,
        IOptions<SecurityOptions> securityOptions,
        IOptions<FileStorageOptions> fileStorageOptions,
        IOptions<BackboneOutputOptions> backboneOptions)
    {
        _policy = policy;
        _securityOptions = securityOptions;
        _fileStorageOptions = fileStorageOptions;
        _backboneOptions = backboneOptions;
    }

    public void Validate()
    {
        // 空 ApiKey 不会导致启动失败，而是让所有请求 401——静默的可用性故障。
        // 在启动阶段快速失败，把配置遗漏变成一条明确的错误信息。
        if (string.IsNullOrWhiteSpace(_securityOptions.Value.ApiKey))
        {
            throw new InvalidOperationException(
                "Security:ApiKey must be configured. An empty key makes every request fail with 401.");
        }

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
