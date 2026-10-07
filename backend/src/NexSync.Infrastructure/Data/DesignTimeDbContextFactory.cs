using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexSync.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<AppDbContext>();
        o.UseNpgsql("Host=localhost;Database=nexsync;Username=nexsync;Password=nexsync_dev");
        return new AppDbContext(o.Options);
    }
}
