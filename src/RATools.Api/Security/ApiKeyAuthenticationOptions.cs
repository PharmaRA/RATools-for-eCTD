using Microsoft.AspNetCore.Authentication;

namespace RATools.Api.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
