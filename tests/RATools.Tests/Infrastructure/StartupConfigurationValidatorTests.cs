using Microsoft.Extensions.Options;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Infrastructure;

public sealed class StartupConfigurationValidatorTests
{
    [Fact]
    public void Validate_Throws_WhenApiKeyIsEmpty()
    {
        using var allowed = new TempDir();
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = [allowed.Path]
        }));
        var fileStorage = Options.Create(new FileStorageOptions { RootPath = allowed.Path });
        var backbone = Options.Create(new BackboneOutputOptions { RootPath = allowed.Path });

        var validator = new StartupConfigurationValidator(
            policy, Options.Create(new SecurityOptions { ApiKey = "   " }), fileStorage, backbone);

        var ex = Assert.Throws<InvalidOperationException>(() => { validator.Validate(); });
        Assert.Contains("Security:ApiKey", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenAllowedRootsAreEmpty()
    {
        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions()));
        var fileStorage = Options.Create(new FileStorageOptions { RootPath = Path.GetTempPath() });
        var backbone = Options.Create(new BackboneOutputOptions { RootPath = Path.GetTempPath() });

        var validator = new StartupConfigurationValidator(policy, Options.Create(new SecurityOptions { ApiKey = "test-key" }), fileStorage, backbone);

        var ex = Assert.Throws<InvalidOperationException>(() => { validator.Validate(); });
        Assert.Contains("AllowedWorkspaceRoots", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenFileStorageRootIsOutsideAllowedRoots()
    {
        using var allowed = new TempDir();
        using var outside = new TempDir();

        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = [allowed.Path]
        }));
        var fileStorage = Options.Create(new FileStorageOptions { RootPath = outside.Path });
        var backbone = Options.Create(new BackboneOutputOptions { RootPath = allowed.Path });

        var validator = new StartupConfigurationValidator(policy, Options.Create(new SecurityOptions { ApiKey = "test-key" }), fileStorage, backbone);

        var ex = Assert.Throws<InvalidOperationException>(() => { validator.Validate(); });
        Assert.Contains("FileStorage:RootPath", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenBackboneRootIsOutsideAllowedRoots()
    {
        using var allowed = new TempDir();
        using var outside = new TempDir();

        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = [allowed.Path]
        }));
        var fileStorage = Options.Create(new FileStorageOptions { RootPath = allowed.Path });
        var backbone = Options.Create(new BackboneOutputOptions { RootPath = outside.Path });

        var validator = new StartupConfigurationValidator(policy, Options.Create(new SecurityOptions { ApiKey = "test-key" }), fileStorage, backbone);

        var ex = Assert.Throws<InvalidOperationException>(() => { validator.Validate(); });
        Assert.Contains("BackboneOutput:RootPath", ex.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenBothRootsAreInsideAllowedRoots()
    {
        using var allowed = new TempDir();
        var fileStoragePath = Path.Combine(allowed.Path, "uploads");
        var backbonePath = Path.Combine(allowed.Path, "publish");
        Directory.CreateDirectory(fileStoragePath);
        Directory.CreateDirectory(backbonePath);

        var policy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions
        {
            AllowedWorkspaceRoots = [allowed.Path]
        }));
        var fileStorage = Options.Create(new FileStorageOptions { RootPath = fileStoragePath });
        var backbone = Options.Create(new BackboneOutputOptions { RootPath = backbonePath });

        var validator = new StartupConfigurationValidator(policy, Options.Create(new SecurityOptions { ApiKey = "test-key" }), fileStorage, backbone);

        validator.Validate();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-startup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
