using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RATools.Api.Security;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key is not configured."));
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var providedValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key is missing."));
        }

        if (providedValues.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.Fail("Exactly one API key is required."));
        }

        var provided = providedValues[0];
        if (string.IsNullOrWhiteSpace(provided) || !KeysMatch(Options.ApiKey, provided))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key is invalid."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "api-key-client")],
            ApiKeyAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool KeysMatch(string configured, string provided)
    {
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return configuredBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
    }
}
