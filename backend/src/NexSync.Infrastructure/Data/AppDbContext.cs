using Microsoft.EntityFrameworkCore;
using NexSync.Domain.Entities;
using File = NexSync.Domain.Entities.File;

namespace NexSync.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Folder> Folders { get; set; }
    public DbSet<File> Files { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");

            entity.HasOne(e => e.Owner).WithMany(u => u.Folders).HasForeignKey(e => e.OwnerId);
            entity.HasOne(e => e.ParentFolder).WithMany(f => f.Children).HasForeignKey(e => e.ParentFolderId).IsRequired(false);

            entity.HasIndex(e => new { e.OwnerId, e.Name })
                .HasDatabaseName("ix_folders_root_unique")
                .IsUnique()
                .HasFilter("\"ParentFolderId\" IS NULL");

            entity.HasIndex(e => new { e.OwnerId, e.ParentFolderId, e.Name })
                .HasDatabaseName("ix_folders_nested_unique")
                .IsUnique()
                .HasFilter("\"ParentFolderId\" IS NOT NULL");
        });

        modelBuilder.Entity<File>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Hash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");

            entity.HasOne(e => e.Owner).WithMany(u => u.Files).HasForeignKey(e => e.OwnerId);
            entity.HasOne(e => e.Folder).WithMany(f => f.Files).HasForeignKey(e => e.FolderId).IsRequired(false);

            entity.HasIndex(e => new { e.OwnerId, e.Name })
                .HasDatabaseName("ix_files_root_unique")
                .IsUnique()
                .HasFilter("\"FolderId\" IS NULL");

            entity.HasIndex(e => new { e.OwnerId, e.FolderId, e.Name })
                .HasDatabaseName("ix_files_nested_unique")
                .IsUnique()
                .HasFilter("\"FolderId\" IS NOT NULL");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.RevokedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");

            entity.HasOne(e => e.User).WithMany(u => u.RefreshTokens).HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.ReplacedByToken).WithMany().HasForeignKey(e => e.ReplacedByTokenId).IsRequired(false);
        });
    }
}
