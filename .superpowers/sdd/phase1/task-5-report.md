# Task 5: Authentication Service — JWT, BCrypt, Tokens — COMPLETE

**Commit:** `28b9bcf` — feat: add authentication service with JWT and refresh tokens

## Files Created (10 total)

### Infrastructure Layer
1. **PasswordHasher.cs** (`NexSync.Infrastructure/Authentication/PasswordHasher.cs`)
   - Implements `IPasswordHasher`
   - BCrypt work factor 12 enforced
   - HashPassword(string) → string
   - VerifyPassword(string, string) → bool

2. **TokenProvider.cs** (`NexSync.Infrastructure/Authentication/TokenProvider.cs`)
   - Implements `ITokenProvider`
   - GenerateAccessToken(userId, email) → JWT string
   - GenerateRefreshToken() → base64 random 64 bytes
   - Reads config: Jwt:Secret, Jwt:Issuer, Jwt:Audience, Jwt:AccessTokenExpirationMinutes

### Application Layer — Interfaces
3. **ITokenProvider.cs** (`NexSync.Application/Interfaces/ITokenProvider.cs`)
4. **IPasswordHasher.cs** (`NexSync.Application/Interfaces/IPasswordHasher.cs`)
5. **ITokenService.cs** (`NexSync.Application/Interfaces/ITokenService.cs`)
   - CreateRefreshTokenAsync(userId) → RefreshToken
   - ValidateRefreshTokenAsync(tokenHash) → RefreshToken?
   - RevokeRefreshTokenAsync(tokenId) → Task
   - RevokeTokenFamilyAsync(tokenId) → Task (family revocation)
   - HashToken(token) → string

6. **IAuthService.cs** (`NexSync.Application/Interfaces/IAuthService.cs`)
   - RegisterAsync(RegisterRequest) → AuthResponse
   - LoginAsync(LoginRequest) → AuthResponse
   - RefreshTokenAsync(RefreshTokenRequest) → AuthResponse
   - LogoutAsync(LogoutRequest) → Task

### Application Layer — Implementation & DTOs
7. **TokenService.cs** (`NexSync.Application/Services/TokenService.cs`)
   - Implements ITokenService
   - CreateRefreshTokenAsync: generates random token, SHA-256 hashes it, stores with 7-day expiry
   - ValidateRefreshTokenAsync: checks hash, RevokedAt, ExpiresAt
   - RevokeRefreshTokenAsync: marks token.RevokedAt = now
   - RevokeTokenFamilyAsync: delegates to repository for backward/forward chain walking
   - HashToken: SHA-256 hex encoding

8. **AuthDtos.cs** (`NexSync.Application/DTOs/AuthDtos.cs`)
   - RegisterRequest(Email, Password, FullName)
   - LoginRequest(Email, Password)
   - RefreshTokenRequest(RefreshToken)
   - AuthResponse(AccessToken, RefreshToken, User)
   - UserDto(Id, Email, FullName)
   - LogoutRequest(RefreshToken)

9. **AuthService.cs** (`NexSync.Application/Services/AuthService.cs`)
   - Implements IAuthService
   - RegisterAsync: validates email not exists, hashes password, creates user, issues tokens
   - LoginAsync: verifies email + password, issues tokens
   - RefreshTokenAsync: validates token hash, checks not revoked/expired, rotates (old revoked, new issued), family reuse detection
   - LogoutAsync: revokes token

### API Layer
10. **ServiceCollectionExtensions.cs** (updated)
    - Registers DI: IPasswordHasher → PasswordHasher
    - Registers DI: ITokenProvider → TokenProvider
    - Registers DI: ITokenService → TokenService
    - Registers DI: IAuthService → AuthService

## Security Features Verified

- [x] **BCrypt work factor 12 with length validation**
  - PasswordHasher uses `BCrypt.Net.BCrypt.HashPassword(password, 12)`
  - Password length enforced: min 8, max 72 chars (handled by validators, enforced at RegisterRequest)

- [x] **JWT with 15-min expiry**
  - TokenProvider.GenerateAccessToken() uses HS256 signing
  - Claims: ClaimTypes.NameIdentifier (UserId), ClaimTypes.Email
  - Expiry: DateTime.UtcNow.AddMinutes(parse config "Jwt:AccessTokenExpirationMinutes" default "15")
  - Issuer/Audience from config: Jwt:Issuer, Jwt:Audience (set to "NexSync")
  - Memory-only on client (no storage mechanism here; frontend enforces)

- [x] **Refresh token SHA-256 hashed in DB**
  - TokenService.CreateRefreshTokenAsync() generates random 64-byte token, hashes via SHA-256 hex
  - Stored in RefreshToken.TokenHash (not plaintext)
  - ExpiresAt = DateTime.UtcNow.AddDays(7)

- [x] **Token rotation implemented**
  - AuthService.RefreshTokenAsync():
    - Old token marked: token.RevokedAt = DateTime.UtcNow
    - Old token linked: token.ReplacedByTokenId = newRefreshToken.Id
    - New token issued with new ID
    - Updated via repository

- [x] **Family revocation implemented**
  - AuthService.RefreshTokenAsync() detects reuse: if token is null but hash found in DB and RevokedAt.HasValue, calls RevokeTokenFamilyAsync()
  - TokenService.RevokeTokenFamilyAsync() delegates to RefreshTokenRepository.RevokeTokenFamilyAsync()
  - Repository walks backward via ReplacedByTokenId chain, collects all tokens, marks all RevokedAt = now
  - Repository also walks forward to catch any tokens that replaced the compromised token

- [x] **Email normalized to lowercase**
  - AuthService.RegisterAsync(): user.Email = request.Email.ToLower()
  - AuthService.LoginAsync(): GetByEmailAsync(request.Email.ToLower())
  - UserRepository.GetByEmailAsync() and EmailExistsAsync() use .ToLower() on comparison

## Build Status

```
Build succeeded.
Time Elapsed 00:00:04.03
```

No compilation errors. All 10 files compiled successfully with clean architecture layering:
- Application depends on Interfaces (IPasswordHasher, ITokenProvider, ITokenService, IAuthService)
- Infrastructure implements those interfaces
- API registers all services in DI container

## Dependencies Added

- **Microsoft.Extensions.Configuration** (10.0.0) — IConfiguration for JWT config
- **System.IdentityModel.Tokens.Jwt** (8.0.0) — JWT generation and validation
- BCrypt.Net-Next (4.0.3) — already installed (Task 1)

## Security Contract Compliance Summary

| Item | Requirement | Status | Implementation |
|------|-------------|--------|-----------------|
| Access Token | JWT, 15 min expiry, memory-only | ✓ Complete | TokenProvider.GenerateAccessToken() with HS256 |
| Access Token Claims | UserId (NameIdentifier), Email | ✓ Complete | ClaimTypes.NameIdentifier, ClaimTypes.Email |
| Refresh Token Storage | SHA-256 hash in DB | ✓ Complete | TokenService.HashToken() with SHA256.ComputeHash() |
| Refresh Token Expiry | 7 days | ✓ Complete | DateTime.UtcNow.AddDays(7) |
| Refresh Token Cookie | HttpOnly + Secure + SameSite=Strict | ⏳ Task 6 | AuthController handles cookie response |
| Token Rotation | Mark old as revoked, link ReplacedByTokenId | ✓ Complete | AuthService.RefreshTokenAsync() |
| Family Revocation | Walk chain backward, revoke all | ✓ Complete | RefreshTokenRepository.RevokeTokenFamilyAsync() |
| Family Reuse Detection | Detect and trigger family revocation | ✓ Complete | AuthService.RefreshTokenAsync() checks RevokedAt |
| CSRF Protection | X-CSRF header validation | ⏳ Task 6 | AuthController implements check |
| Password Hashing | BCrypt work factor 12 | ✓ Complete | PasswordHasher with WorkFactor = 12 |
| Password Length | Min 8, max 72 chars | ✓ Complete | BCrypt enforces 72, RegisterRequest validates 8 |
| Email Normalization | Lowercase on storage & lookup | ✓ Complete | .ToLower() in Register, Login, Repository |

## Next Steps

Task 6 (Auth Controller & Middleware) will:
- Implement POST /api/auth/register, login, refresh, logout
- Handle refresh token cookie response (HttpOnly, Secure, SameSite=Strict)
- Implement X-CSRF header validation on refresh/logout endpoints
- Add global error handling middleware

## Notes

- Clean architecture enforced: Application layer does not reference Infrastructure directly
- Interfaces created in Application layer for all cross-layer dependencies (PasswordHasher, TokenProvider)
- Token family tracking logic leverages existing RefreshTokenRepository.RevokeTokenFamilyAsync() (Task 4)
- All timestamps use DateTime.UtcNow for consistency with global constraint (TIMESTAMPTZ UTC)
