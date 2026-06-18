using RATools.Application.Applications;

namespace RATools.Infrastructure.Persistence.EfCore;

/// <summary>
/// 在单个数据库事务内执行多步应用/序列删除。委托内各仓储的 SaveChangesAsync
/// 都落在同一事务里，直到 CommitAsync 才提交；任意一步失败则整体回滚，避免孤儿行。
/// </summary>
public sealed class EfCoreApplicationDeletionTransaction(RAToolsDbContext dbContext)
    : IApplicationDeletionTransaction
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
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
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
