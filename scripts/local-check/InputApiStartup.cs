using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(RATools.Qa.InputApiStartup))]
namespace RATools.Qa;

public sealed class InputApiStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.AddDataProtection().UseEphemeralDataProtectionProvider());
}
