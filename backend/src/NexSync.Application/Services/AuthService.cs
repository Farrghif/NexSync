using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;

namespace NexSync.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly ITokenProvider _tokenProvider;
    private readonly IPasswordHasher _passwordHasher;
    
    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        ITokenProvider tokenProvider,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
        _tokenProvider = tokenProvider;
        _passwordHasher = passwordHasher;
    }
    
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _userRepository.EmailExistsAsync(request.Email))
            throw new InvalidOperationException("Email already registered");
        
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLower(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = request.FullName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        await _userRepository.AddAsync(user);
        
        var refreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, refreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email.ToLower());
        if (user is null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new InvalidOperationException("Invalid credentials");
        
        var refreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, refreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var token = await _tokenService.ValidateRefreshTokenAsync(tokenHash);
        
        if (token is null)
        {
            var revokedToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash);
            if (revokedToken is not null && revokedToken.RevokedAt.HasValue)
            {
                await _tokenService.RevokeTokenFamilyAsync(revokedToken.Id);
            }
            throw new InvalidOperationException("Invalid or expired refresh token");
        }
        
        var user = await _userRepository.GetByIdAsync(token.UserId);
        if (user is null)
            throw new InvalidOperationException("User not found");
        
        token.RevokedAt = DateTime.UtcNow;
        var newRefreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        token.ReplacedByTokenId = newRefreshToken.Id;
        await _refreshTokenRepository.UpdateAsync(token);
        
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, newRefreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task LogoutAsync(LogoutRequest request)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var token = await _tokenService.ValidateRefreshTokenAsync(tokenHash);
        
        if (token is not null)
        {
            await _tokenService.RevokeRefreshTokenAsync(token.Id);
        }
    }
}

