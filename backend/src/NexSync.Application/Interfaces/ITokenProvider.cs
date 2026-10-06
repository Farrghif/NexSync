namespace NexSync.Application.Interfaces;

public interface ITokenProvider
{
    string GenerateAccessToken(Guid userId, string email);
    string GenerateRefreshToken();
}
