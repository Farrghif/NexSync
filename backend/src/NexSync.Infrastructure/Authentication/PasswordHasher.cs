using NexSync.Application.Interfaces;

namespace NexSync.Infrastructure.Authentication;

public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;
    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    public bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
