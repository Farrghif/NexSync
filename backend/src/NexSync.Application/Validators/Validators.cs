using System.Text.RegularExpressions;
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
    /// <summary>
    /// RFC 5322 compliant email validation regex (simplified but practical).
    /// Rejects: "test@", "@test", "test@@test", "test @test", etc.
    /// </summary>
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled);

    public static void ValidateRegister(string email, string password, string fullName)
    {
        ValidateEmail(email);
        ValidatePassword(password);
        ValidateFullName(fullName);
    }

    public static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email address is required.");
        if (email.Length > 254) // RFC 5321
            throw new DomainException("Email address exceeds maximum length.");
        if (!EmailRegex.IsMatch(email))
            throw new DomainException("Invalid email address format.");
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new DomainException("Password is required.");
        if (password.Length < 8)
            throw new DomainException("Password must be at least 8 characters.");
        if (password.Length > 72)
            throw new DomainException("Password must not exceed 72 characters.");
        
        // Password complexity: at least 3 of 4 categories
        bool hasLower = password.Any(char.IsLower);
        bool hasUpper = password.Any(char.IsUpper);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));
        
        int complexityCount = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) 
            + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
        
        if (complexityCount < 3)
            throw new DomainException(
                "Password must contain at least 3 of: lowercase letters, uppercase letters, digits, special characters, and must be at least 8 characters.");
    }

    public static void ValidateFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (fullName.Length > 100)
            throw new DomainException("Full name exceeds 100 characters.");
        if (fullName.Length < 2)
            throw new DomainException("Full name must be at least 2 characters.");
    }
}
