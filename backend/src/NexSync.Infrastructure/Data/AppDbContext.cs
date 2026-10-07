using Microsoft.EntityFrameworkCore;
using NexSync.Domain.Entities;

namespace NexSync.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileEntry> Files => Set<FileEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SyncCounter> SyncCounters => Set<SyncCounter>();
    public DbSet<ChangeLog> ChangeLogs => Set<ChangeLog>();
    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    public DbSet<Device> Devices => Set<Device>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        });

        b.Entity<Folder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.Owner).WithMany(u => u.Folders).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentFolder).WithMany(f => f.Children).HasForeignKey(x => x.ParentFolderId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OwnerId, x.Name }).HasDatabaseName("ix_folders_root_unique").IsUnique().HasFilter("\"ParentFolderId\" IS NULL");
            e.HasIndex(x => new { x.OwnerId, x.ParentFolderId, x.Name }).HasDatabaseName("ix_folders_nested_unique").IsUnique().HasFilter("\"ParentFolderId\" IS NOT NULL");
        });

        b.Entity<FileEntry>(e =>
        {
            e.ToTable("Files");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Hash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.Owner).WithMany(u => u.Files).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Folder).WithMany(f => f.Files).HasForeignKey(x => x.FolderId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OwnerId, x.Name }).HasDatabaseName("ix_files_root_unique").IsUnique().HasFilter("\"FolderId\" IS NULL");
            e.HasIndex(x => new { x.OwnerId, x.FolderId, x.Name }).HasDatabaseName("ix_files_nested_unique").IsUnique().HasFilter("\"FolderId\" IS NOT NULL");
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
            e.Property(x => x.RevokedAt).HasColumnType("timestamptz");
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReplacedByToken).WithMany().HasForeignKey(x => x.ReplacedByTokenId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<SyncCounter>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.NextSequence).HasDefaultValue(1L).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChangeLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sequence).IsRequired();
            e.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Operation).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.EntityId).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.OccurredAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Sequence }).IsUnique();
        });

        b.Entity<ProcessedOperation>(e =>
        {
            e.HasKey(x => x.OperationId);
            e.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            e.Property(x => x.ResultPayload).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        });

        b.Entity<Device>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            e.Property(x => x.LastSeenAt).HasColumnType("timestamptz");
            e.Property(x => x.RevokedAt).HasColumnType("timestamptz");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });
    }
}
