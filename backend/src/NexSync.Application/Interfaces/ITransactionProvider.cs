namespace NexSync.Application.Interfaces;

public interface ITransactionProvider
{
    Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IDbTransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
