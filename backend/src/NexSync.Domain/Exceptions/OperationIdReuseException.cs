namespace NexSync.Domain.Exceptions;

public class OperationIdReuseException : DomainException
{
    public OperationIdReuseException(string message) : base(message) { }
}
