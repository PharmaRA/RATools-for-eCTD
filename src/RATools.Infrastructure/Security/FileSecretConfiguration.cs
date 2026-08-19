using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RATools.Infrastructure.Security;

public static class FileSecretConfiguration
{
    private const string ApiKeyPathKey = "FileSecrets:ApiKeyPath";
    private const string PostgreSqlPasswordPathKey = "FileSecrets:PostgreSqlPasswordPath";

    public static void Apply(ConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var overrides = new Dictionary<string, string?>();
        var apiKeyPath = configuration[ApiKeyPathKey];
        if (!string.IsNullOrWhiteSpace(apiKeyPath))
        {
            overrides["Security:ApiKey"] = ReadSecret(apiKeyPath, ApiKeyPathKey);
        }

        var passwordPath = configuration[PostgreSqlPasswordPathKey];
        if (!string.IsNullOrWhiteSpace(passwordPath))
        {
            var connectionString = configuration.GetConnectionString("PostgreSql");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:PostgreSql must be configured before applying the PostgreSQL password file.");
            }

            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Password = ReadSecret(passwordPath, PostgreSqlPasswordPathKey),
            };
            overrides["ConnectionStrings:PostgreSql"] = connectionStringBuilder.ConnectionString;
        }

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }
    }

    private static string ReadSecret(string path, string configurationKey)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{configurationKey} must be an absolute path.");
        }

        string value;
        try
        {
            value = File.ReadAllText(path).TrimEnd('\r', '\n');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Unable to read the secret configured by {configurationKey}.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The secret configured by {configurationKey} is empty.");
        }

        return value;
    }
}
