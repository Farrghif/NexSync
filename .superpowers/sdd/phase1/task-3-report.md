# Task 3: Database Schema — EF Core Migrations Report

## Status: COMPLETE ✓

All migration files generated successfully with correct schema design.

## Files Created

1. **AppDbContext.cs** — `backend/src/NexSync.Infrastructure/Data/AppDbContext.cs`
   - Fluent API configuration for all four entities (User, Folder, File, RefreshToken)
   - All timestamps configured as TIMESTAMPTZ (PostgreSQL UTC)
   - Partial unique indexes with WHERE clauses for NULL handling
   - Foreign key relationships with appropriate cascade behavior

2. **DesignTimeDbContextFactory.cs** — `backend/src/NexSync.Infrastructure/Data/DesignTimeDbContextFactory.cs`
   - Implements IDesignTimeDbContextFactory for dotnet ef CLI
   - Hardcoded dev connection string for migrations tooling

3. **Migration Files** — `backend/src/NexSync.Infrastructure/Migrations/`
   - `20261006025619_InitialCreate.cs` — Schema creation
   - `20261006025619_InitialCreate.Designer.cs` — Migration metadata
   - `AppDbContextModelSnapshot.cs` — Current model snapshot

## Verification Checklist

### ✓ Schema Structure
- [x] Users table with unique Email index
- [x] Folders table with ParentFolderId self-reference
- [x] Files table with FolderId foreign key to Folders
- [x] RefreshTokens table with ReplacedByTokenId self-reference

### ✓ Timestamps
All timestamp columns configured as `timestamptz`:
- Users: CreatedAt, UpdatedAt
- Folders: CreatedAt, UpdatedAt
- Files: CreatedAt, UpdatedAt
- RefreshTokens: CreatedAt, ExpiresAt, RevokedAt

### ✓ Unique Indexes with Partial WHERE Clauses

**Folders:**
- Root level (lines 147-152): `ix_folders_root_unique` on (OwnerId, Name) WHERE ParentFolderId IS NULL
- Nested level (lines 135-140): `ix_folders_nested_unique` on (OwnerId, ParentFolderId, Name) WHERE ParentFolderId IS NOT NULL

**Files:**
- Root level (lines 128-133): `ix_files_root_unique` on (OwnerId, Name) WHERE FolderId IS NULL
- Nested level (lines 121-126): `ix_files_nested_unique` on (OwnerId, FolderId, Name) WHERE FolderId IS NOT NULL

These partial indexes correctly handle NULL values and enforce case-sensitive uniqueness at the database level.

### ✓ Foreign Keys
- Users FK in Folders: CASCADE delete
- Users FK in Files: CASCADE delete
- Users FK in RefreshTokens: CASCADE delete
- Folder self-reference (ParentFolderId): No cascade (nullable)
- RefreshToken self-reference (ReplacedByTokenId): No cascade (nullable)

### ✓ Build Status
```
dotnet build — SUCCESS (0 errors)
dotnet ef migrations add InitialCreate — SUCCESS
```

### ✓ Migration SQL Coverage
- All four DbSets present (Users, Folders, Files, RefreshTokens)
- All columns mapped with correct types
- Primary keys configured
- Foreign keys configured
- All indexes created with correct naming

## Architecture Notes

- DbContext isolated to Infrastructure layer (src/NexSync.Infrastructure/Data/)
- Fluent API configuration centralized in OnModelCreating()
- No entity-specific config files; all configuration in one place
- Design-time factory enables EF Core CLI without needing a running API
- Null reference handling explicit in partial indexes (per PostgreSQL requirements)

## Migration Not Applied

As per specification, migration files have been generated **only**. The database has NOT been updated. Schema validation happens before task completion per ARCHITECTURE GATE.

## Next Steps

- ARCHITECTURE GATE review before proceeding to Task 4
- Execute migration only after design review passes
- Verify no circular dependencies between layers
