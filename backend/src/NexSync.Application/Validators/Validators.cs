using NexSync.Domain.Exceptions;

namespace NexSync.Application.Validators;

public static class FileNameValidator
{
    private static readonly char[] ForbiddenCharacters = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FileNameInvalidException("Name cannot be empty.");
        if (name.Length > 255)
            throw new FileNameInvalidException("Name exceeds 255 characters.");
        if (name.EndsWith(' ') || name.EndsWith('.'))
            throw new FileNameInvalidException("Name cannot end with space or dot.");
        if (name.IndexOfAny(ForbiddenCharacters) >= 0)
            throw new FileNameInvalidException("Name contains forbidden characters.");
        var baseName = name.Contains('.') ? name[..name.LastIndexOf('.')] : name;
        if (WindowsReservedNames.Contains(baseName))
            throw new FileNameInvalidException("Name is a Windows reserved name.");
    }
}

public static class AuthValidator
{
    public static void ValidateRegister(string email, string password, string fullName)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new DomainException("Invalid email address.");
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new DomainException("Password must be at least 8 characters.");
        if (password.Length > 72)
            throw new DomainException("Password must not exceed 72 characters.");
    }
}
