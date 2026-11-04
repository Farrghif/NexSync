using Microsoft.Extensions.Configuration;
using NexSync.Application.Interfaces;

namespace NexSync.Infrastructure.Storage;

public class LocalStorageService(IConfiguration config) : IStorageService
{
    private readonly string _root = config["Storage:RootPath"] ?? "./storage";
    private readonly long _max = long.TryParse(config["Storage:MaxFileSizeBytes"], out var m) ? m : 104857600;

    public async Task<(string storagePath, string hash, long size)> SaveFileAsync(Guid ownerId, Stream stream, string fileName)
    {
        Directory.CreateDirectory(Path.Combine(_root, "temp"));
        var tempPath = Path.Combine(_root, "temp", Guid.NewGuid().ToString("N"));
        using var sha = System.Security.Cryptography.SHA256.Create();
        long total = 0;
        await using (var fs = File.Create(tempPath))
        {
            var buf = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                total += read;
                if (total > _max) { fs.Close(); File.Delete(tempPath); throw new InvalidOperationException("File exceeds maximum size."); }
                await fs.WriteAsync(buf.AsMemory(0, read));
                sha.TransformBlock(buf, 0, read, null, 0);
            }
        }
        sha.TransformFinalBlock([], 0, 0);
        var hash = Convert.ToHexString(sha.Hash!);
        var userDir = Path.Combine(_root, ownerId.ToString());
        Directory.CreateDirectory(userDir);
        var finalPath = Path.Combine(userDir, hash);
        if (File.Exists(finalPath))
        {
            File.Delete(tempPath);
            return (finalPath, hash, total);
        }
        File.Move(tempPath, finalPath);
        return (finalPath, hash, total);
    }

    public Task<Stream> GetFileAsync(string storagePath)
    {
        if (!File.Exists(storagePath)) throw new FileNotFoundException($"File not found: {storagePath}");
        Stream s = File.OpenRead(storagePath);
        return Task.FromResult(s);
    }

    public Task DeleteFileAsync(string storagePath)
    {
        if (File.Exists(storagePath)) File.Delete(storagePath);
        return Task.CompletedTask;
    }

    public Task<bool> FileExistsAsync(string storagePath) => Task.FromResult(File.Exists(storagePath));
}
