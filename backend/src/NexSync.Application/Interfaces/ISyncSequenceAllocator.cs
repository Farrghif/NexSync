namespace NexSync.Application.Interfaces;

public interface ISyncSequenceAllocator
{
    Task<long> AllocateAsync(Guid userId, CancellationToken cancellationToken = default);
}
