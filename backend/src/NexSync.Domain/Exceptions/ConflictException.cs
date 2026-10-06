namespace NexSync.Domain.Exceptions;

public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}
