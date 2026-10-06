namespace NexSync.Domain.Exceptions;

public class UnauthorizedAccessException : DomainException
{
    public UnauthorizedAccessException(string message) : base(message) { }
}
