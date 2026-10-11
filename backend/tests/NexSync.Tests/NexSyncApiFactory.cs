using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexSync.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace NexSync.Tests;

public sealed class NexSyncApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("nexsync_test")
        .WithUsername("nexsync")
        .WithPassword("nexsync_test")
        .Build();

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "nexsync-test", Guid.NewGuid().ToString("N"));

    private string? _connectionString;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _db.StartAsync();
                last = null;
                break;
            }
            catch (TimeoutException ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)));
            }
        }
        if (last is not null) throw last;
        _connectionString = _db.GetConnectionString();
        Directory.CreateDirectory(StorageRoot);
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _db.DisposeAsync();
                break;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)));
            }
        }
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true);
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = StorageRoot,
                ["Storage:MaxFileSizeBytes"] = "104857600",
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            var cs = _connectionString ?? "Host=localhost;Database=nexsync_test;Username=nexsync;Password=nexsync_test";
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
        });
    }
}



