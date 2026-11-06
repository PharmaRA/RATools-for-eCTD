using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Finalization;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresReleaseCaptureBindingTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task CompletedRunCannotAuthorizeChangedRegionalOutputAtANewRevision()
    {
        await using var context = fixture.CreateDbContext();
        await ReleaseCaptureBindingIntegrationTests.VerifyAsync(true, new EfCorePackageValidationStore(context));
    }

    [RequiresPostgresFact]
    public async Task ExactDeliveryIdentityCanReuseItsPersistedCompletedRun()
    {
        await using var context = fixture.CreateDbContext();
        await ReleaseCaptureBindingIntegrationTests.VerifyAsync(false, new EfCorePackageValidationStore(context));
    }
}
