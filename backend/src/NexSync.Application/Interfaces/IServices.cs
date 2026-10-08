using NexSync.Application.DTOs;

namespace NexSync.Application.Interfaces;

public interface ITokenService
{
    Task<(Domain.Entities.RefreshToken Token, string RawToken)> CreateRefreshTokenAsync(Guid userId);
    Task<Domain.Entities.RefreshToken?> ValidateRefreshTokenAsync(string rawToken);
    Task<Domain.Entities.RefreshToken?> FindByRawTokenAsync(string rawToken);
    Task LinkRotationAsync(Guid oldTokenId, Guid newTokenId);
    Task RevokeRefreshTokenAsync(Guid tokenId);
    Task RevokeTokenFamilyAsync(Guid tokenId);
    string HashToken(string token);
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request);
    Task LogoutAsync(LogoutRequest request);
}

public interface IFolderService
{
    Task<FolderContentsResponse> GetContentsAsync(Guid userId, Guid? parentId, int page, int pageSize);
    Task<FolderDto> GetByIdAsync(Guid userId, Guid folderId);
    Task<FolderDto> CreateAsync(Guid userId, string name, Guid? parentFolderId, Guid? deviceId = null, MutationContext? mutation = null);
    Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, string name, Guid? parentFolderId, Guid? deviceId = null, MutationContext? mutation = null);
    Task DeleteAsync(Guid userId, Guid folderId, Guid? deviceId = null, MutationContext? mutation = null);
}

public interface IFileService
{
    Task<FileDto> UploadAsync(Guid userId, Stream fileStream, string fileName, string contentType, Guid? folderId, Guid? deviceId = null, MutationContext? mutation = null);
    Task<FileDto> GetByIdAsync(Guid userId, Guid fileId);
    Task<Stream> DownloadAsync(Guid userId, Guid fileId);
    Task<FileDto> UpdateAsync(Guid userId, Guid fileId, string name, Guid? folderId, Guid? deviceId = null, MutationContext? mutation = null);
    Task DeleteAsync(Guid userId, Guid fileId, Guid? deviceId = null, MutationContext? mutation = null);
}

public interface IStorageService
{
    Task<(string storagePath, string hash, long size)> SaveFileAsync(Guid ownerId, Stream stream, string fileName);
    Task<Stream> GetFileAsync(string storagePath);
    Task DeleteFileAsync(string storagePath);
    Task<bool> FileExistsAsync(string storagePath);
}
