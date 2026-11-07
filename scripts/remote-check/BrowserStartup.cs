using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
[assembly: HostingStartup(typeof(BrowserStartup))]
public sealed class BrowserStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.AddDataProtection().UseEphemeralDataProtectionProvider());
}
