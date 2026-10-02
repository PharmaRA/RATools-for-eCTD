using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using RATools.Application.Ctd;

namespace RATools.Infrastructure.Storage;

public sealed class NodeFileMoveRecoveryService(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // In-memory workspaces do not survive a restart. Resolve the provider at
        // runtime, as host/test configuration can override registration-time values.
        if (string.Equals(configuration["Persistence:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase)) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NodeFileMoveRecovery>().RecoverAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
