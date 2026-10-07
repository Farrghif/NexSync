namespace NexSync.Application.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public interface IAccessTokenProvider
{
    string GenerateAccessToken(Guid userId, string email);
    string GenerateRefreshToken();
}
