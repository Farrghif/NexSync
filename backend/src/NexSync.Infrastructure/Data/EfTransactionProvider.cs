using Microsoft.EntityFrameworkCore.Storage;
using NexSync.Application.Interfaces;

namespace NexSync.Infrastructure.Data;

public class EfTransactionProvider(AppDbContext context) : ITransactionProvider
{
    public async Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        return new EfDbTransactionScope(tx);
    }

    private sealed class EfDbTransactionScope(IDbContextTransaction transaction) : IDbTransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync()
            => transaction.DisposeAsync();
    }
}
