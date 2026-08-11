using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace RATools.Infrastructure.Security;

public sealed class LocalOnlyDeploymentValidator(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<DeploymentOptions> deploymentOptions)
{
    private static readonly string[] DevelopmentDatabasePasswords =
    [
        "postgres",
        "password",
        "changeme",
        "ratools-local-dev-password",
    ];

    public void Validate()
    {
        if (!string.Equals(
                deploymentOptions.Value.Mode,
                DeploymentOptions.LocalOnlyMode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Deployment:Mode must be '{DeploymentOptions.LocalOnlyMode}'. Shared deployment is not supported.");
        }

        ValidateListenerUrls();

        var provider = configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
        if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            ValidatePostgresConnection(environment.IsDevelopment());
        }

        if (!environment.IsDevelopment())
        {
            ValidateProductionApiKey();
        }
    }

    private void ValidateListenerUrls()
    {
        var listeners = new List<(string Key, string Url)>();
        var urls = configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            listeners.AddRange(urls
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(url => ("Urls", url)));
        }

        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var endpointUrl = endpoint["Url"];
            if (!string.IsNullOrWhiteSpace(endpointUrl))
            {
                listeners.Add(($"Kestrel:Endpoints:{endpoint.Key}:Url", endpointUrl));
            }
        }

        if (listeners.Count == 0)
        {
            // Kestrel's unconfigured default is localhost; TestServer also has no network listener.
            return;
        }

        foreach (var listener in listeners)
        {
            if (!Uri.TryCreate(listener.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !IsLoopbackHost(uri.Host))
            {
                throw new InvalidOperationException(
                    $"{listener.Key} '{listener.Url}' must use an HTTP(S) loopback host (localhost, 127.0.0.0/8, or ::1) in LocalOnly mode.");
            }
        }
    }

    private void ValidatePostgresConnection(bool allowDevelopmentPassword)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'PostgreSql' is not configured.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var hosts = (builder.Host ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => !IsLoopbackHost(host)))
        {
            throw new InvalidOperationException(
                "The PostgreSQL host must be localhost or a loopback IP address in LocalOnly mode.");
        }

        if (allowDevelopmentPassword)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(builder.Password)
            || DevelopmentDatabasePasswords.Contains(builder.Password, StringComparer.OrdinalIgnoreCase)
            || string.Equals(builder.Username, builder.Password, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The PostgreSQL password must be non-empty and must not use a default or development credential outside Development.");
        }
    }

    private void ValidateProductionApiKey()
    {
        var apiKey = configuration.GetValue<string>("Security:ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Trim().Length < 32
            || string.Equals(apiKey.Trim(), "dev-api-key-do-not-use-in-production", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Security:ApiKey must be a non-development value with at least 32 characters outside Development.");
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        var normalized = host.Trim().TrimStart('[').TrimEnd(']');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address);
    }
}
