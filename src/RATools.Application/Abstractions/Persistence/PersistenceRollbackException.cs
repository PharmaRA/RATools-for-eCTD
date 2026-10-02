namespace RATools.Application.Abstractions.Persistence;

public sealed class PersistenceRollbackException(Exception originalFailure, Exception rollbackFailure)
    : InvalidOperationException("The persistence operation failed and its transaction could not be rolled back.",
        new AggregateException(originalFailure, rollbackFailure));
