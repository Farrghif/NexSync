namespace NexSync.Domain.Exceptions;

public class FileNameInvalidException : DomainException
{
    public FileNameInvalidException(string message) : base(message) { }
}
