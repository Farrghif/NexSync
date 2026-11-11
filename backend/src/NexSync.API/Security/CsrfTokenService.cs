using System.Security.Cryptography;

namespace NexSync.API.Security;

/// <summary>
/// CSRF token generation and validation service.
/// Tokens are cryptographically random and session-bound.
/// </summary>
public class CsrfTokenService
{
    private const int TokenLengthBytes = 32;
    private const string SessionKey = "nexsync-csrf-token";

    /// <summary>
    /// Generate a new CSRF token and store it in session.
    /// </summary>
    public static string GenerateToken(ISession session)
    {
        var token = GenerateRandomToken();
        session.SetString(SessionKey, token);
        return token;
    }

    /// <summary>
    /// Return the session's token, creating one only if absent — regenerating
    /// here would invalidate the token the client already holds.
    /// </summary>
    public static string GetOrCreateToken(ISession session)
    {
        if (session.TryGetValue(SessionKey, out var stored) && stored is { Length: > 0 })
            return System.Text.Encoding.UTF8.GetString(stored);
        return GenerateToken(session);
    }

    /// <summary>
    /// Validate CSRF token from request against session token.
    /// </summary>
    public static bool ValidateToken(ISession session, string? providedToken)
    {
        if (string.IsNullOrWhiteSpace(providedToken))
            return false;

        session.TryGetValue(SessionKey, out var storedBytes);
        if (storedBytes == null)
            return false;

        var storedToken = System.Text.Encoding.UTF8.GetString(storedBytes);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(storedToken),
            System.Text.Encoding.UTF8.GetBytes(providedToken));
    }

    private static string GenerateRandomToken()
    {
        var randomBytes = new byte[TokenLengthBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
