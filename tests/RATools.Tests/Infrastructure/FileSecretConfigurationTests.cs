using Microsoft.Extensions.Configuration;
using Npgsql;
using RATools.Infrastructure.Security;

namespace RATools.Tests.Infrastructure;

public sealed class FileSecretConfigurationTests
{
    [Fact]
    public void Apply_LoadsApiKeyAndPostgresPasswordWithoutStringConcatenation()
    {
        using var secrets = new TempSecrets();
        var apiKeyPath = secrets.Write("api-key", "production-api-key\r\n");
        var passwordPath = secrets.Write("postgres-password", "p@ss;word=value\n");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Security:ApiKey"] = "placeholder",
            ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Database=ratools;Username=ratools",
            ["FileSecrets:ApiKeyPath"] = apiKeyPath,
            ["FileSecrets:PostgreSqlPasswordPath"] = passwordPath,
        });

        FileSecretConfiguration.Apply(configuration);

        Assert.Equal("production-api-key", configuration["Security:ApiKey"]);
        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("PostgreSql"));
        Assert.Equal("p@ss;word=value", connectionString.Password);
        Assert.Equal("127.0.0.1", connectionString.Host);
    }

    [Theory]
    [InlineData("relative-secret")]
    [InlineData("missing/secret")]
    public void Apply_RejectsNonAbsoluteSecretPaths(string path)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["FileSecrets:ApiKeyPath"] = path,
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => FileSecretConfiguration.Apply(configuration));

        Assert.Contains("absolute path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsMissingSecretFile()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["FileSecrets:ApiKeyPath"] = Path.Combine(
                Path.GetTempPath(),
                $"missing-ratools-secret-{Guid.NewGuid():N}"),
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => FileSecretConfiguration.Apply(configuration));

        Assert.Contains("Unable to read", exception.Message);
    }

    [Fact]
    public void Apply_RejectsEmptySecret()
    {
        using var secrets = new TempSecrets();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["FileSecrets:ApiKeyPath"] = secrets.Write("empty", "\r\n"),
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => FileSecretConfiguration.Apply(configuration));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsPostgresPasswordWithoutConnectionString()
    {
        using var secrets = new TempSecrets();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["FileSecrets:PostgreSqlPasswordPath"] = secrets.Write("postgres-password", "secret"),
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => FileSecretConfiguration.Apply(configuration));

        Assert.Contains("ConnectionStrings:PostgreSql", exception.Message);
    }

    private static ConfigurationManager BuildConfiguration(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private sealed class TempSecrets : IDisposable
    {
        public TempSecrets()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"ratools-secrets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        private string DirectoryPath { get; }

        public string Write(string name, string value)
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path, value);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
