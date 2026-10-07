using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Tests;

[TestClass]
public sealed class SyncSequenceAllocatorTests
{
    private readonly NexSyncApiFactory _factory = new();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> CreateUserAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hash",
            FullName = "Test User",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [TestMethod]
    public async Task AllocateAsync_WithoutActiveTransaction_ThrowsInvalidOperationException()
    {
        var userId = await CreateUserAsync("no-tx@nexsync.dev");
        await using var scope = _factory.Services.CreateAsyncScope();
        var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await allocator.AllocateAsync(userId));
    }

    [TestMethod]
    public async Task Concurrency_TwentyParallelTransactions_StrictlyIncreasing_ZeroDuplicates_ExactlyOneToTwenty()
    {
        var userId = await CreateUserAsync("concurrent@nexsync.dev");

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

            await using var tx = await db.Database.BeginTransactionAsync();
            var seq = await allocator.AllocateAsync(userId);
            await Task.Yield();
            await tx.CommitAsync();
            return seq;
        });

        var results = await Task.WhenAll(tasks);
        var sorted = results.OrderBy(x => x).ToList();

        var expected = Enumerable.Range(1, 20).Select(i => (long)i).ToList();
        CollectionAssert.AreEqual(expected, sorted);
    }

    [TestMethod]
    public async Task Rollback_TransactionAllocatesAndRollsBack_SubsequentTransactionAllocatesAndCommits()
    {
        var userId = await CreateUserAsync("rollback@nexsync.dev");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

            await using var tx1 = await db.Database.BeginTransactionAsync();
            var seq1 = await allocator.AllocateAsync(userId);
            Assert.AreEqual(1L, seq1);
            await tx1.CommitAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

            await using var tx2 = await db.Database.BeginTransactionAsync();
            var seq2 = await allocator.AllocateAsync(userId);
            Assert.AreEqual(2L, seq2);
            await tx2.RollbackAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

            await using var tx3 = await db.Database.BeginTransactionAsync();
            var seq3 = await allocator.AllocateAsync(userId);
            await tx3.CommitAsync();
            Assert.AreEqual(2L, seq3);
        }
    }

    [TestMethod]
    public async Task GapsInSequence_ArePreservedAndCommitOrdered()
    {
        var userId = await CreateUserAsync("gap@nexsync.dev");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SyncCounters.Add(new SyncCounter { UserId = userId, NextSequence = 10 });
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

            await using var tx1 = await db.Database.BeginTransactionAsync();
            var seq1 = await allocator.AllocateAsync(userId);
            await tx1.CommitAsync();
            Assert.AreEqual(10L, seq1);

            await using var tx2 = await db.Database.BeginTransactionAsync();
            var seq2 = await allocator.AllocateAsync(userId);
            await tx2.CommitAsync();
            Assert.AreEqual(11L, seq2);
        }
    }

    [TestMethod]
    public async Task AllocateAsync_DifferentUsers_HaveIndependentSequences()
    {
        var user1 = await CreateUserAsync("user1@nexsync.dev");
        var user2 = await CreateUserAsync("user2@nexsync.dev");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var allocator = scope.ServiceProvider.GetRequiredService<ISyncSequenceAllocator>();

        await using var tx1 = await db.Database.BeginTransactionAsync();
        var seqU1_1 = await allocator.AllocateAsync(user1);
        var seqU1_2 = await allocator.AllocateAsync(user1);
        await tx1.CommitAsync();

        await using var tx2 = await db.Database.BeginTransactionAsync();
        var seqU2_1 = await allocator.AllocateAsync(user2);
        await tx2.CommitAsync();

        Assert.AreEqual(1L, seqU1_1);
        Assert.AreEqual(2L, seqU1_2);
        Assert.AreEqual(1L, seqU2_1);
    }
}
