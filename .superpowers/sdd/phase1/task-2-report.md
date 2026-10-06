# Task 2: Domain Models — Entities & Exceptions — COMPLETED

**Timestamp:** 2026-10-06T02:51:46Z  
**Commit:** d20ebdfafc2da2868a073768c8d4d0433838f05d  
**Status:** ✅ PASSED

## Files Created

| File | Lines | Purpose |
|------|-------|---------|
| `backend/src/NexSync.Domain/Entities/User.cs` | 14 | User aggregate with navigation to Folders, Files, RefreshTokens |
| `backend/src/NexSync.Domain/Entities/RefreshToken.cs` | 14 | Refresh token with family tracking via ReplacedByTokenId |
| `backend/src/NexSync.Domain/Entities/Folder.cs` | 16 | Folder with parent/children hierarchy and file collection |
| `backend/src/NexSync.Domain/Entities/File.cs` | 16 | File with content hash, storage path, and folder reference |
| `backend/src/NexSync.Domain/Exceptions/DomainException.cs` | 5 | Base exception for all domain errors |
| `backend/src/NexSync.Domain/Exceptions/UnauthorizedAccessException.cs` | 5 | Access control violations |
| `backend/src/NexSync.Domain/Exceptions/ConflictException.cs` | 5 | Business rule conflicts (e.g., duplicate email, folder cycle) |
| `backend/src/NexSync.Domain/Exceptions/FileNameInvalidException.cs` | 5 | Invalid file/folder names (Windows compatibility) |

**Total lines created:** 80

## Entities Count

✅ **4 entities created:**
- User (with Guid Id, Email, PasswordHash, FullName, timestamps, and 3 navigation collections)
- RefreshToken (with token family tracking for rotation/revocation)
- Folder (with parent/children hierarchy support)
- File (with content-addressed hash and storage path)

All entities use exact property names and types from specification. Navigation properties configured for EF Core relationships.

## Exceptions Count

✅ **4 exceptions created:**
- DomainException (base, inherits from Exception)
- UnauthorizedAccessException (access control)
- ConflictException (business rule violations)
- FileNameInvalidException (Windows filename validation)

All inherit from DomainException and accept message parameter in constructor.

## Build Verification

```
dotnet build
```

**Result:** ✅ SUCCESS (0 errors, 20 warnings from transitive dependencies, not from domain code)

Build output confirms:
- No syntax errors
- No compilation errors
- All entities compile cleanly
- All exceptions compile cleanly
- Solution structure intact (API, Application, Domain, Infrastructure, Tests all compile)

## Entity Instantiation Verification

All entities can be instantiated without errors:

```csharp
// User instantiation
var user = new User
{
    Id = Guid.NewGuid(),
    Email = "test@example.com",
    PasswordHash = "hashed",
    FullName = "Test User",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

// RefreshToken instantiation
var token = new RefreshToken
{
    Id = Guid.NewGuid(),
    UserId = user.Id,
    TokenHash = "abc123",
    ExpiresAt = DateTime.UtcNow.AddDays(7),
    RevokedAt = null,
    ReplacedByTokenId = null,
    CreatedAt = DateTime.UtcNow
};

// Folder instantiation
var folder = new Folder
{
    Id = Guid.NewGuid(),
    Name = "My Folder",
    ParentFolderId = null,
    OwnerId = user.Id,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

// File instantiation
var file = new File
{
    Id = Guid.NewGuid(),
    Name = "document.pdf",
    FolderId = folder.Id,
    OwnerId = user.Id,
    Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    Size = 1024,
    ContentType = "application/pdf",
    StoragePath = "/hashes/e3/b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

// Exception instantiation
try { throw new FileNameInvalidException("Name contains invalid character: ?"); }
catch (DomainException ex) { /* caught */ }
```

All entities instantiate cleanly with proper navigation initialization (collections default to empty arrays `[]`).

## Architecture Compliance

✅ **Domain layer independence verified:**
- No references to Application, Infrastructure, or API layers
- No Entity Framework Core imports
- No HTTP types
- Pure domain model with navigation properties for relationship configuration
- Domain exceptions inherit from base `Exception` class only

## Concerns & Notes

**None.** Implementation matches specification exactly:
- All entity property names, types, and navigation configurations match Task 2 spec verbatim
- All exception classes follow specified inheritance hierarchy (DomainException base)
- Build succeeds with zero domain-layer errors
- Ready for Task 3 (Database Schema — EF Core Migrations)

## Next Steps

Task 2 complete. Ready to proceed to Task 3: Database Schema — EF Core Migrations.
- Will consume: User, RefreshToken, Folder, File entities
- Will produce: AppDbContext, DesignTimeDbContextFactory, migration files with partial unique indexes
