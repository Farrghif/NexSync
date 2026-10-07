using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;

namespace NexSync.Infrastructure.Data;

public class SyncSequenceAllocator(AppDbContext dbContext) : ISyncSequenceAllocator
{
    public async Task<long> AllocateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("SyncSequenceAllocator.AllocateAsync must be called inside an active database transaction.");
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var tx = dbContext.Database.CurrentTransaction.GetDbTransaction();

        var current = await TrySelectForUpdateAsync(connection, tx, userId, cancellationToken);
        if (!current.HasValue)
        {
            await using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = tx;
                insertCmd.CommandText = """
                    INSERT INTO "SyncCounters" ("UserId", "NextSequence")
                    VALUES (@userId, 1)
                    ON CONFLICT ("UserId") DO NOTHING;
                    """;
                var p = insertCmd.CreateParameter();
                p.ParameterName = "@userId";
                p.Value = userId;
                insertCmd.Parameters.Add(p);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            current = await TrySelectForUpdateAsync(connection, tx, userId, cancellationToken)
                ?? throw new InvalidOperationException($"Failed to acquire sync counter for user '{userId}'.");
        }

        var allocated = current.Value;

        await using (var updateCmd = connection.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE "SyncCounters"
                SET "NextSequence" = @nextSequence
                WHERE "UserId" = @userId;
                """;

            var pNext = updateCmd.CreateParameter();
            pNext.ParameterName = "@nextSequence";
            pNext.Value = allocated + 1;
            updateCmd.Parameters.Add(pNext);

            var pUser = updateCmd.CreateParameter();
            pUser.ParameterName = "@userId";
            pUser.Value = userId;
            updateCmd.Parameters.Add(pUser);

            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var tracked = dbContext.ChangeTracker.Entries<SyncCounter>().FirstOrDefault(e => e.Entity.UserId == userId);
        if (tracked is not null)
        {
            tracked.Entity.NextSequence = allocated + 1;
            tracked.State = EntityState.Unchanged;
        }

        return allocated;
    }

    private static async Task<long?> TrySelectForUpdateAsync(
        DbConnection connection,
        DbTransaction tx,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var selectCmd = connection.CreateCommand();
        selectCmd.Transaction = tx;
        selectCmd.CommandText = """
            SELECT "NextSequence"
            FROM "SyncCounters"
            WHERE "UserId" = @userId
            FOR UPDATE;
            """;

        var p = selectCmd.CreateParameter();
        p.ParameterName = "@userId";
        p.Value = userId;
        selectCmd.Parameters.Add(p);

        var result = await selectCmd.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
        {
            return null;
        }

        return Convert.ToInt64(result);
    }
}
