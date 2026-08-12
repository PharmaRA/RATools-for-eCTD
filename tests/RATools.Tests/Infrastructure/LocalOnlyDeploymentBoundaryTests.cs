using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RATools.Infrastructure.Security;

namespace RATools.Tests.Infrastructure;

public sealed class LocalOnlyDeploymentBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Shared")]
    public void Validate_RejectsMissingOrUnsupportedDeploymentMode(string deploymentMode)
    {
        var validator = CreateValidator(
            deploymentMode: deploymentMode,
            urls: "http://127.0.0.1:5000");

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("LocalOnly", exception.Message);
    }

    [Theory]
    [InlineData("http://0.0.0.0:5000")]
    [InlineData("http://*:5000")]
    [InlineData("http://+:5000")]
    [InlineData("http://192.168.1.10:5000")]
    [InlineData("https://ratools.example.test:5443")]
    public void Validate_RejectsNonLoopbackUrls(string urls)
    {
        var validator = CreateValidator(urls: urls);

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsOnlyLoopbackUrls()
    {
        var validator = CreateValidator(
            urls: "http://localhost:5000;https://127.0.0.1:5443;http://[::1]:5001");

        validator.Validate();
    }

    [Fact]
    public void Validate_AcceptsWildcardListenerOnlyForContainerizedDeployment()
    {
        var validator = CreateValidator(
            urls: "http://0.0.0.0:8080;http://[::]:8081",
            containerized: true);

        validator.Validate();
    }

    [Fact]
    public void Validate_ContainerizedDeploymentStillRejectsLanListener()
    {
        var validator = CreateValidator(
            urls: "http://192.168.1.10:8080",
            containerized: true);

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsNonLoopbackKestrelEndpoint()
    {
        var validator = CreateValidator(
            urls: "http://127.0.0.1:5000",
            additionalConfiguration: new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Public:Url"] = "http://0.0.0.0:6000",
            });

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("Kestrel:Endpoints:Public:Url", exception.Message);
    }

    [Fact]
    public void Validate_ProductionRejectsWeakApiKey()
    {
        var validator = CreateValidator(
            environmentName: Environments.Production,
            apiKey: "short-key",
            provider: "PostgreSql",
            connectionString: "Host=localhost;Database=ratools;Username=postgres;Password=strong-database-password-2026");

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("Security:ApiKey", exception.Message);
    }

    [Fact]
    public void Validate_ProductionRejectsDefaultPostgresPassword()
    {
        var validator = CreateValidator(
            environmentName: Environments.Production,
            apiKey: "production-api-key-with-at-least-32-characters",
            provider: "PostgreSql",
            connectionString: "Host=localhost;Database=ratools;Username=postgres;Password=postgres");

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("PostgreSQL password", exception.Message);
    }

    [Fact]
    public void Validate_RejectsRemotePostgresHost()
    {
        var validator = CreateValidator(
            provider: "PostgreSql",
            connectionString: "Host=database.example.test;Database=ratools;Username=postgres;Password=postgres");

        var exception = Assert.Throws<InvalidOperationException>(validator.Validate);

        Assert.Contains("PostgreSQL host", exception.Message);
    }

    [Fact]
    public void Validate_ProductionAcceptsStrongLocalOnlyConfiguration()
    {
        var validator = CreateValidator(
            environmentName: Environments.Production,
            apiKey: "production-api-key-with-at-least-32-characters",
            provider: "PostgreSql",
            connectionString: "Host=127.0.0.1;Database=ratools;Username=postgres;Password=strong-database-password-2026");

        validator.Validate();
    }

    [Fact]
    public void InstanceLock_PreventsSecondProcessBoundaryUntilReleased()
    {
        using var directory = new TempDir();
        var options = Options.Create(new DeploymentOptions
        {
            InstanceLockPath = "locks/ratools-api.lock",
        });
        var environment = new TestHostEnvironment(Environments.Production, directory.Path);
        using var first = new LocalOnlyInstanceLock(options, environment);
        using var second = new LocalOnlyInstanceLock(options, environment);

        first.Acquire();
        var exception = Assert.Throws<InvalidOperationException>(second.Acquire);
        Assert.Contains("one API/worker process", exception.Message);

        first.Dispose();
        second.Acquire();
    }

    [Fact]
    public void InstanceLock_RejectsPathOutsideContentRootWithoutChangingTarget()
    {
        using var directory = new TempDir();
        var outsidePath = Path.GetFullPath(Path.Combine(directory.Path, "..", $"outside-{Guid.NewGuid():N}.lock"));
        File.WriteAllText(outsidePath, "unchanged");
        try
        {
            var options = Options.Create(new DeploymentOptions
            {
                InstanceLockPath = Path.Combine("..", Path.GetFileName(outsidePath)),
            });
            using var instanceLock = new LocalOnlyInstanceLock(
                options,
                new TestHostEnvironment(Environments.Production, directory.Path));

            var exception = Assert.Throws<InvalidOperationException>(instanceLock.Acquire);

            Assert.Contains("content root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(outsidePath));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    private static LocalOnlyDeploymentValidator CreateValidator(
        string deploymentMode = DeploymentOptions.LocalOnlyMode,
        string urls = "http://127.0.0.1:5000",
        string environmentName = "Development",
        string apiKey = "dev-api-key-do-not-use-in-production",
        string provider = "InMemory",
        string? connectionString = null,
        bool containerized = false,
        IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["urls"] = urls,
            ["Persistence:Provider"] = provider,
            ["Security:ApiKey"] = apiKey,
            ["ConnectionStrings:PostgreSql"] = connectionString,
        };
        if (additionalConfiguration is not null)
        {
            foreach (var item in additionalConfiguration)
            {
                values[item.Key] = item.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new LocalOnlyDeploymentValidator(
            configuration,
            new TestHostEnvironment(environmentName, AppContext.BaseDirectory),
            Options.Create(new DeploymentOptions
            {
                Mode = deploymentMode,
                Containerized = containerized,
            }));
    }

    private sealed class TestHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "RATools.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-local-only-{Guid.NewGuid():N}");
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
