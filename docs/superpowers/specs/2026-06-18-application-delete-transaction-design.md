# 应用删除事务化（Application Delete Transaction）

## 概述

让多步应用/序列删除具备事务性。当前删除协调器把 placement、publish job、document、application 的删除拆成多次独立的 `SaveChangesAsync` 调用，而注册的事务实现只是直接调用委托、不开任何数据库事务。一旦中途失败，会在关系型数据库里留下孤儿行。

本设计为关系型 provider 提供基于 EF Core 的 `IApplicationDeletionTransaction` 实现，在一个数据库事务内执行整段删除操作，失败时回滚。InMemory provider 仍使用 passthrough 实现。

## 目标

1. 在 PostgreSQL 模式下，应用与序列的多步删除运行在单个数据库事务中，失败时整体回滚。
2. 为关系型 provider 提供 EF Core 事务实现（Begin/Commit/Rollback），在 Infrastructure 层注册。
3. InMemory provider 继续使用 `PassthroughApplicationDeletionTransaction`。
4. `ApplicationDeletionCoordinator` 的删除逻辑无需改动其编排顺序即可获得事务保护。

## 非目标

- 不改变删除的业务编排顺序或安全校验逻辑。
- 不让 InMemory 仓储模拟数据库事务的回滚语义。
- 不把工作区文件清理（workspace purge）纳入数据库事务，文件操作仍在事务提交后进行。
- 不重新设计已有的级联外键约束。

## 设计

### 当前行为与缺陷

注册侧：`src/RATools.Application/DependencyInjection.cs:20` 把 `IApplicationDeletionTransaction` 唯一注册为 `PassthroughApplicationDeletionTransaction`，即使在 PostgreSQL 模式下也如此。

passthrough 实现并不开事务，只是直接执行委托：`src/RATools.Application/Applications/PassthroughApplicationDeletionTransaction.cs:5-9`

```csharp
public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    => operation(cancellationToken);

public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    => operation(cancellationToken);
```

协调器在 `transaction.ExecuteAsync` 委托内对多类实体分别删除：`src/RATools.Application/Applications/ApplicationDeletionCoordinator.cs:61-73` 中，`DeleteApplicationAsync` 逐个删除 placement、调用 `DeleteByApplicationAsync`、删除孤儿 document、最后删除 application。`DeleteSequenceAsync`（`ApplicationDeletionCoordinator.cs:143-161`）有类似的多步序列。

每个 EF 仓储方法都各自调用 `SaveChangesAsync`，例如 `src/RATools.Infrastructure/Persistence/EfCore/EfCoreDocumentPlacementRepository.cs:40`、`EfCorePublishJobRepository.cs:132/147`、`EfCoreDocumentRepository.cs:41`、`EfCoreApplicationRepository.cs:104`。由于 passthrough 不开事务，这些 `SaveChangesAsync` 各自独立提交，中途任意一步失败都会留下已提交的部分删除，产生孤儿行。

### EF Core 事务实现

在 Infrastructure 层新增基于 EF Core 的实现，例如 `EfCoreApplicationDeletionTransaction`，依赖 `RAToolsDbContext`：

```csharp
public sealed class EfCoreApplicationDeletionTransaction(RAToolsDbContext dbContext)
    : IApplicationDeletionTransaction
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
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

    // 泛型重载同理
}
```

接口契约保持不变（`src/RATools.Application/Applications/IApplicationDeletionTransaction.cs:5-7`），因此 `ApplicationDeletionCoordinator` 不需要改动。委托内各仓储的 `SaveChangesAsync` 仍会执行，但它们的写入都落在同一个 `BeginTransactionAsync` 打开的事务里，直到 `CommitAsync` 才真正提交。

### 注册分流

把事务注册从 Application 层移动到按 provider 分流的位置。当前 `AddApplication()` 无条件注册 passthrough（`DependencyInjection.cs:20`），应改为提供默认 passthrough，再由 Infrastructure 在关系型分支覆盖。

`src/RATools.Infrastructure/DependencyInjection.cs:36-59` 已按 `Persistence:Provider` 分流：

- InMemory 分支（`DependencyInjection.cs:37-45`）保留 `PassthroughApplicationDeletionTransaction`。
- PostgreSQL 分支（`DependencyInjection.cs:53-59`，已注册 `RAToolsDbContext` 与各 EF 仓储为 Scoped）新增 `services.AddScoped<IApplicationDeletionTransaction, EfCoreApplicationDeletionTransaction>()`，覆盖默认注册。

事务实现注册为 Scoped，与 `RAToolsDbContext` 及 EF 仓储的生命周期一致，确保它们共享同一个 `DbContext` 实例，事务才能覆盖所有仓储写入。

### 与级联外键的关系

数据库已有级联外键约束（见 `2026-05-29-database-constraints-design.md`）：placement 到 application、sequence 的外键为 cascade delete，到 document 的外键为 restrict delete。级联约束保证当某行被删除时数据库自身的引用一致性，但它不能把协调器拆分的多次 `SaveChangesAsync` 合并为一个原子单元。

两者职责互补：

- 级联外键解决"删除一行时其依赖行如何处理"的数据库内一致性。
- 本事务解决"协调器的多步删除要么全做要么全不做"的应用层原子性。

协调器仍显式编排删除顺序（先 placement，再 publish job，再孤儿 document，最后 application），以触发其业务安全校验（如 purge 安全检查）并产生友好的业务冲突异常；事务则保证这一整段在失败时回滚。

## 测试策略

- 新增 `EfCoreApplicationDeletionTransaction` 测试（针对本地 PostgreSQL 或 EF 关系型 provider），验证委托内抛异常时事务回滚、无部分删除残留。
- 新增协调器集成测试：在删除中途模拟某一步失败，断言 application、sequence、placement、publish job、document 行保持删除前状态。
- 保留 InMemory 路径下的现有删除测试，确认 passthrough 行为不变。
- 运行完整后端测试套件。

## 风险

**风险：** 事务实现注册为错误的生命周期，导致与仓储不共享同一 `DbContext`，事务无法覆盖写入。

**缓解：** 事务与 `RAToolsDbContext`、EF 仓储统一注册为 Scoped；通过集成测试验证回滚实际生效。

**风险：** 工作区文件清理在事务提交后进行，文件删除失败时数据库已提交，产生数据库与文件系统的不一致。

**缓解：** 这是现状即有的边界，协调器已用 `WorkspacePurgeFailedException`（`ApplicationDeletionCoordinator.cs:339`）显式标记此情况；本设计不扩大文件操作的事务范围，避免长事务持有锁。

**风险：** InMemory 仓储不模拟回滚，单元测试可能无法覆盖事务行为。

**缓解：** 事务语义由针对关系型 provider 的集成测试覆盖；InMemory 仅验证 passthrough 编排不回归。

## 验收标准

应用删除事务化在满足以下条件时视为完成：

- PostgreSQL 模式下注册了基于 EF Core 的 `IApplicationDeletionTransaction` 实现。
- 应用与序列删除运行在单个数据库事务内，中途失败时整体回滚，无孤儿行。
- InMemory 模式仍使用 `PassthroughApplicationDeletionTransaction`。
- `ApplicationDeletionCoordinator` 的编排逻辑无需修改。
- 新增的事务回滚测试通过。
- 现有后端与前端测试套件仍然通过。
