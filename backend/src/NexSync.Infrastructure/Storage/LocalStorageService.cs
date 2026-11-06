using Microsoft.Extensions.Configuration;
using NexSync.Application.Interfaces;

namespace NexSync.Infrastructure.Storage;

/// <summary>
/// Local file storage service with security validations.
/// Stores file blobs by content hash to enable deduplication.
/// </summary>
public class LocalStorageService(IConfiguration config) : IStorageService
{
    private readonly string _root = config["Storage:RootPath"] ?? "./storage";
    private readonly long _max = long.TryParse(config["Storage:MaxFileSizeBytes"], out var m) ? m : 104857600;

    public async Task<(string storagePath, string hash, long size)> SaveFileAsync(Guid ownerId, Stream stream, string fileName)
    {
        // Validate inputs
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be empty.");
        
        // Create storage directories
        Directory.CreateDirectory(Path.Combine(_root, "temp"));
        var tempPath = Path.Combine(_root, "temp", Guid.NewGuid().ToString("N"));
        
        // Ensure temp path stays within root
        var fullTempPath = Path.GetFullPath(tempPath);
        var fullRootPath = Path.GetFullPath(_root);
        if (!fullTempPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Invalid storage path.");

        using var sha = System.Security.Cryptography.SHA256.Create();
        long total = 0;
        
        try
        {
            await using (var fs = File.Create(tempPath))
            {
                var buf = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buf)) > 0)
                {
                    total += read;
                    if (total > _max)
                    {
                        fs.Close();
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        throw new InvalidOperationException("File exceeds maximum size.");
                    }
                    await fs.WriteAsync(buf.AsMemory(0, read));
                    sha.TransformBlock(buf, 0, read, null, 0);
                }
            }

            sha.TransformFinalBlock([], 0, 0);
            var hash = Convert.ToHexString(sha.Hash!);
            
            var userDir = Path.Combine(_root, ownerId.ToString());
            Directory.CreateDirectory(userDir);
            var finalPath = Path.Combine(userDir, hash);

            // Validate final path stays within root
            var fullFinalPath = Path.GetFullPath(finalPath);
            if (!fullFinalPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Invalid storage path.");

            // Handle file already exists (deduplication)
            if (File.Exists(finalPath))
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return (finalPath, hash, total);
            }

            // Atomic move: if file already exists, another process beat us to it
            try
            {
                File.Move(tempPath, finalPath, overwrite: false);
            }
            catch (IOException)
            {
                // File already exists or was deleted by another process
                if (File.Exists(finalPath))
                {
                    // File exists, which is fine (dedup)
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    return (finalPath, hash, total);
                }
                
                // Temp file still exists but final doesn't - real error
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            return (finalPath, hash, total);
        }
        catch
        {
            // Cleanup temp file on any error
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public Task<Stream> GetFileAsync(string storagePath)
    {
        if (!File.Exists(storagePath)) 
            throw new FileNotFoundException($"File not found: {storagePath}");

        // Validate path stays within root (security check)
        var fullPath = Path.GetFullPath(storagePath);
        var fullRootPath = Path.GetFullPath(_root);
        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar))
            throw new UnauthorizedAccessException("Invalid storage path.");

        // Return stream - ASP.NET Core will dispose it
        Stream s = File.OpenRead(storagePath);
        return Task.FromResult(s);
    }

    public Task DeleteFileAsync(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Task.CompletedTask;

        // Validate path stays within root
        var fullPath = Path.GetFullPath(storagePath);
        var fullRootPath = Path.GetFullPath(_root);
        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar))
            throw new UnauthorizedAccessException("Invalid storage path.");

        if (File.Exists(storagePath)) 
            File.Delete(storagePath);
        
        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Task.FromResult(false);

        // Validate path stays within root
        var fullPath = Path.GetFullPath(storagePath);
        var fullRootPath = Path.GetFullPath(_root);
        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar))
            return Task.FromResult(false);

        return Task.FromResult(File.Exists(storagePath));
    }
}

