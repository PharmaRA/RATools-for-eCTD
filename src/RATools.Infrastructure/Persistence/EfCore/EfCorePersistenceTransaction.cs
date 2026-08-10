using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCorePersistenceTransaction(RAToolsDbContext dbContext) : IPersistenceTransaction
{
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(30);

    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction, exception);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction, exception);
            throw;
        }
    }

    private static async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Exception originalException)
    {
        using var rollbackCts = new CancellationTokenSource(RollbackTimeout);
        try
        {
            await transaction.RollbackAsync(rollbackCts.Token);
        }
        catch (Exception rollbackException)
        {
            throw new InvalidOperationException(
                "The persistence operation failed and its transaction could not be rolled back.",
                new AggregateException(originalException, rollbackException));
        }
    }
}
