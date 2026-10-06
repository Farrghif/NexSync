# NexSync Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fully functional cloud storage system with user authentication, folder management, file upload/download, and React web client.

**Architecture:** ASP.NET Core 10 backend with clean architecture (API → Application → Domain → Infrastructure), PostgreSQL for metadata, local disk storage with content-addressed hash-based paths (Phase 7 deduplication ready). React 19 frontend with token-based auth. Streaming upload, case-insensitive naming, ownership-verified access control.

**Tech Stack:** ASP.NET Core 10, EF Core 10, PostgreSQL 17, React 19, TypeScript, Vite 8, Axios, React Router 8, Tailwind CSS 4, NUnit/xUnit for tests.

**Spec:** `docs/superpowers/specs/2026-10-06-nexsync-phase1-design.md`

## Global Constraints

- .NET 10 LTS (support until 14 November 2028)
- React 19.3, Vite 8, React Router 8, Tailwind CSS 4
- PostgreSQL 17 (supported until 8 November 2029)
- All timestamps in TIMESTAMPTZ (UTC)
- File names validated for Windows compatibility (no `\ / : * ? " < > |`, no reserved names)
- Case-insensitive name uniqueness via `LOWER()` indexes
- JWT access token: 15 min expiry, memory-only on client
- Refresh token: HttpOnly, SHA-256 hashed in DB, family tracking via `ReplacedByTokenId`
- Maximum file size: 100MB in Phase 1
- Streaming upload (no memory buffering)
- HTTPS in production, CORS with specific origin only

## Review Focus

These inputs/conditions are critical and likely to cause integration issues if missed:

1. **Root folder uniqueness with NULL handling:** Partial indexes with `WHERE parent_folder_id IS NULL` must be used; regular unique constraint fails on NULL values.
2. **Folder cycle prevention:** Moving a folder into a descendant creates a cycle; walk up from target to root and reject if source folder ID found. Test must verify: self-reference rejected, descendant rejection works, but sibling moves allowed.
3. **Cross-user access protection:** Every file/folder operation must verify `OwnerId == currentUserId` at the application layer, not just FK constraints. Test: User A cannot access User B's file by UUID alone.
4. **Concurrent identical uploads:** Two requests uploading identical content (same SHA-256) must safely reuse the storage object; temporary file discarded if final path already exists; no race corruption.
5. **Token family revocation:** If a revoked token is reused after rotation, walk the `ReplacedByTokenId` chain backward to find the original token and revoke the entire family (all tokens in the chain). This detects compromise early.
6. **Windows filename safety:** Files must not be creatable with forbidden chars (`\ / : * ? " < > |`), reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9), or trailing dots/spaces. Phase 3 WPF desktop depends on this.
7. **Auth security contract:** Refresh token must be HttpOnly cookie, access token memory-only, rotation with `ReplacedByTokenId` tracking, CSRF via `X-CSRF: 1` header on refresh/logout, token reuse detection on application layer.

---

# File Structure

## Backend

```
backend/
├── NexSync.sln
├── src/
│   ├── NexSync.API/
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   ├── FoldersController.cs
│   │   │   └── FilesController.cs
│   │   ├── Middleware/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── AuthenticationMiddleware.cs (if needed)
│   │   ├── Extensions/
│   │   │   └── ServiceCollectionExtensions.cs
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── NexSync.API.csproj
│   ├── NexSync.Application/
│   │   ├── DTOs/
│   │   │   ├── AuthDtos.cs
│   │   │   ├── FolderDtos.cs
│   │   │   └── FileDtos.cs
│   │   ├── Interfaces/
│   │   │   ├── IAuthService.cs
│   │   │   ├── IFolderService.cs
│   │   │   ├── IFileService.cs
│   │   │   ├── IStorageService.cs
│   │   │   └── ITokenService.cs
│   │   ├── Services/
│   │   │   ├── AuthService.cs
│   │   │   ├── FolderService.cs
│   │   │   ├── FileService.cs
│   │   │   └── TokenService.cs
│   │   ├── Validators/
│   │   │   ├── AuthValidator.cs
│   │   │   ├── FileNameValidator.cs
│   │   │   └── PasswordValidator.cs
│   │   └── NexSync.Application.csproj
│   ├── NexSync.Domain/
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── Folder.cs
│   │   │   ├── File.cs
│   │   │   └── RefreshToken.cs
│   │   ├── Exceptions/
│   │   │   ├── DomainException.cs
│   │   │   ├── FolderNotFoundExecution.cs
│   │   │   ├── FileNotFoundExecution.cs
│   │   │   ├── UnauthorizedAccessException.cs
│   │   │   └── ConflictException.cs
│   │   └── NexSync.Domain.csproj
│   └── NexSync.Infrastructure/
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   ├── Migrations/
│       │   │   ├── 001_InitialCreate.cs
│       │   │   └── 001_InitialCreate.Designer.cs
│       │   └── DesignTimeDbContextFactory.cs
│       ├── Repositories/
│       │   ├── IRepository.cs (generic base)
│       │   ├── UserRepository.cs
│       │   ├── FolderRepository.cs
│       │   ├── FileRepository.cs
│       │   └── RefreshTokenRepository.cs
│       ├── Storage/
│       │   └── LocalStorageService.cs
│       ├── Authentication/
│       │   ├── TokenProvider.cs
│       │   └── PasswordHasher.cs
│       └── NexSync.Infrastructure.csproj
└── tests/
    └── NexSync.Tests/
        ├── Unit/
        │   ├── AuthServiceTests.cs
        │   ├── FolderServiceTests.cs
        │   ├── FileServiceTests.cs
        │   ├── TokenServiceTests.cs
        │   └── ValidatorTests.cs
        ├── Integration/
        │   ├── AuthFlowTests.cs
        │   ├── FolderCrudTests.cs
        │   ├── FileCrudTests.cs
        │   └── AuthorizationTests.cs
        └── NexSync.Tests.csproj
```

## Frontend

```
web/
├── package.json
├── vite.config.ts
├── tsconfig.json
├── tailwind.config.ts
├── index.html
├── src/
│   ├── api/
│   │   ├── client.ts (Axios instance + interceptor)
│   │   ├── auth.ts (endpoints)
│   │   ├── folders.ts
│   │   └── files.ts
│   ├── components/
│   │   ├── FileBrowser.tsx
│   │   ├── FolderBreadcrumb.tsx
│   │   ├── FileList.tsx
│   │   ├── FolderList.tsx
│   │   ├── UploadDropZone.tsx
│   │   └── ContextMenu.tsx
│   ├── contexts/
│   │   └── AuthContext.tsx
│   ├── pages/
│   │   ├── LoginPage.tsx
│   │   ├── RegisterPage.tsx
│   │   └── DashboardPage.tsx
│   ├── types/
│   │   ├── auth.ts
│   │   ├── folder.ts
│   │   └── file.ts
│   ├── App.tsx
│   ├── main.tsx
│   └── index.css
└── .gitignore
```

## Docker & Config

```
docker/
└── docker-compose.yml

.gitignore
```

---

# Task Breakdown

### Task 1: Backend Setup — Solution & Dependencies

**Files:**
- Create: `backend/NexSync.sln`
- Create: `backend/src/NexSync.API/NexSync.API.csproj`
- Create: `backend/src/NexSync.Application/NexSync.Application.csproj`
- Create: `backend/src/NexSync.Domain/NexSync.Domain.csproj`
- Create: `backend/src/NexSync.Infrastructure/NexSync.Infrastructure.csproj`
- Create: `backend/tests/NexSync.Tests/NexSync.Tests.csproj`
- Create: `backend/src/NexSync.API/Program.cs`
- Create: `backend/src/NexSync.API/appsettings.json`
- Create: `docker/docker-compose.yml`
- Create: `.gitignore`

**Interfaces:**
- Produces: Solution structure with four projects, NuGet packages, Docker Compose with PostgreSQL

- [ ] **Step 1: Create solution file**

```bash
cd backend
dotnet new sln --name NexSync
```

- [ ] **Step 2: Create class library projects**

```bash
dotnet new classlib --name NexSync.Domain --output src/NexSync.Domain
dotnet new classlib --name NexSync.Application --output src/NexSync.Application
dotnet new classlib --name NexSync.Infrastructure --output src/NexSync.Infrastructure
dotnet new webapi --name NexSync.API --output src/NexSync.API
dotnet new mstest --name NexSync.Tests --output tests/NexSync.Tests
```

- [ ] **Step 3: Add projects to solution**

```bash
dotnet sln add src/NexSync.Domain/NexSync.Domain.csproj
dotnet sln add src/NexSync.Application/NexSync.Application.csproj
dotnet sln add src/NexSync.Infrastructure/NexSync.Infrastructure.csproj
dotnet sln add src/NexSync.API/NexSync.API.csproj
dotnet sln add tests/NexSync.Tests/NexSync.Tests.csproj
```

- [ ] **Step 4: Add project references**

API → Application, Domain, Infrastructure
Application → Domain
Infrastructure → Application, Domain
Tests → API, Application, Domain, Infrastructure

```bash
cd src/NexSync.API
dotnet add reference ../NexSync.Application/NexSync.Application.csproj
dotnet add reference ../NexSync.Domain/NexSync.Domain.csproj
dotnet add reference ../NexSync.Infrastructure/NexSync.Infrastructure.csproj

cd ../NexSync.Application
dotnet add reference ../NexSync.Domain/NexSync.Domain.csproj

cd ../NexSync.Infrastructure
dotnet add reference ../NexSync.Application/NexSync.Application.csproj
dotnet add reference ../NexSync.Domain/NexSync.Domain.csproj

cd ../../tests/NexSync.Tests
dotnet add reference ../../src/NexSync.API/NexSync.API.csproj
dotnet add reference ../../src/NexSync.Application/NexSync.Application.csproj
dotnet add reference ../../src/NexSync.Domain/NexSync.Domain.csproj
dotnet add reference ../../src/NexSync.Infrastructure/NexSync.Infrastructure.csproj
```

- [ ] **Step 5: Add NuGet packages to NexSync.Infrastructure**

```bash
cd src/NexSync.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore --version 10.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.0
dotnet add package BCrypt.Net-Next --version 4.0.3
```

- [ ] **Step 6: Add NuGet packages to NexSync.API**

```bash
cd ../NexSync.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.0
dotnet add package Microsoft.AspNetCore.OpenApi --version 10.0.0
dotnet add package Swashbuckle.AspNetCore --version 6.4.0
```

- [ ] **Step 7: Create docker-compose.yml**

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: nexsync
      POSTGRES_USER: nexsync
      POSTGRES_PASSWORD: nexsync_dev
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

Save to `docker/docker-compose.yml`.

- [ ] **Step 8: Create appsettings.json**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=nexsync;Username=nexsync;Password=nexsync_dev"
  },
  "Jwt": {
    "Secret": "nexsync_super_secret_key_min_32_chars_12345",
    "Issuer": "NexSync",
    "Audience": "NexSync",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Storage": {
    "RootPath": "./storage",
    "MaxFileSizeBytes": 104857600
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

Save to `backend/src/NexSync.API/appsettings.json`.

- [ ] **Step 9: Create Program.cs** with DI setup

Basic template with CORS and service registration (to be filled in by later tasks).

- [ ] **Step 10: Create .gitignore**

Standard .NET + Node ignored paths, including `storage/`, `node_modules/`, `.env`.

- [ ] **Step 11: Verify solution builds**

```bash
cd backend
dotnet build
```

Expected: SUCCESS

- [ ] **Step 12: Commit**

```bash
git add backend/ docker/ .gitignore
git commit -m "chore: initialize NexSync solution structure with dependencies"
```

---

### Task 2: Domain Models — Entities & Exceptions

**Files:**
- Create: `backend/src/NexSync.Domain/Entities/User.cs`
- Create: `backend/src/NexSync.Domain/Entities/RefreshToken.cs`
- Create: `backend/src/NexSync.Domain/Entities/Folder.cs`
- Create: `backend/src/NexSync.Domain/Entities/File.cs`
- Create: `backend/src/NexSync.Domain/Exceptions/DomainException.cs`
- Create: `backend/src/NexSync.Domain/Exceptions/UnauthorizedAccessException.cs`
- Create: `backend/src/NexSync.Domain/Exceptions/ConflictException.cs`
- Create: `backend/src/NexSync.Domain/Exceptions/FileNameInvalidException.cs`

**Interfaces:**
- Produces: Domain entities (User, RefreshToken, Folder, File) with exact property names; exception hierarchy

- [ ] **Step 1: Write User entity**

```csharp
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<File> Files { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
```

- [ ] **Step 2: Write RefreshToken entity**

```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public User User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }
}
```

- [ ] **Step 3: Write Folder entity**

```csharp
public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public Folder? ParentFolder { get; set; }
    public ICollection<Folder> Children { get; set; } = [];
    public User Owner { get; set; } = null!;
    public ICollection<File> Files { get; set; } = [];
}
```

- [ ] **Step 4: Write File entity**

```csharp
public class File
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public Guid OwnerId { get; set; }
    public string Hash { get; set; } = null!;
    public long Size { get; set; }
    public string ContentType { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public Folder? Folder { get; set; }
    public User Owner { get; set; } = null!;
}
```

- [ ] **Step 5: Write exception classes**

`DomainException`, `UnauthorizedAccessException`, `ConflictException`, `FileNameInvalidException` — all inherit from `Exception` with message parameter in ctor.

- [ ] **Step 6: Commit**

```bash
git add backend/src/NexSync.Domain/
git commit -m "feat: add domain entities and exceptions"
```

---

### Task 3: Database Schema — EF Core Migrations

**Files:**
- Create: `backend/src/NexSync.Infrastructure/Data/AppDbContext.cs`
- Create: `backend/src/NexSync.Infrastructure/Data/DesignTimeDbContextFactory.cs`
- Create: `backend/src/NexSync.Infrastructure/Data/Migrations/001_InitialCreate.cs`
- Create: `backend/src/NexSync.Infrastructure/Data/Migrations/001_InitialCreate.Designer.cs`
- Create: `backend/src/NexSync.Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: User, Folder, File, RefreshToken entities
- Produces: Configured DbContext with fluent API, migration files with partial unique indexes

- [ ] **Step 1: Write AppDbContext**

```csharp
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
        
        // Configure User
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
        
        // Configure Folder (with partial unique indexes for root and nested)
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            
            entity.HasOne(e => e.Owner).WithMany(u => u.Folders);
            entity.HasOne(e => e.ParentFolder).WithMany(f => f.Children).HasForeignKey(e => e.ParentFolderId).IsRequired(false);
            
            // Partial indexes for uniqueness (case-insensitive, handling NULL)
            entity.HasIndex(e => new { e.OwnerId, e.Name })
                .HasName("ix_folders_root_unique")
                .IsUnique()
                .HasFilter("\"ParentFolderId\" IS NULL");
            
            entity.HasIndex(e => new { e.OwnerId, e.ParentFolderId, e.Name })
                .HasName("ix_folders_nested_unique")
                .IsUnique()
                .HasFilter("\"ParentFolderId\" IS NOT NULL");
        });
        
        // Configure File (same pattern)
        modelBuilder.Entity<File>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Hash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            
            entity.HasOne(e => e.Owner).WithMany(u => u.Files);
            entity.HasOne(e => e.Folder).WithMany(f => f.Files).HasForeignKey(e => e.FolderId).IsRequired(false);
            
            entity.HasIndex(e => new { e.OwnerId, e.Name })
                .HasName("ix_files_root_unique")
                .IsUnique()
                .HasFilter("\"FolderId\" IS NULL");
            
            entity.HasIndex(e => new { e.OwnerId, e.FolderId, e.Name })
                .HasName("ix_files_nested_unique")
                .IsUnique()
                .HasFilter("\"FolderId\" IS NOT NULL");
        });
        
        // Configure RefreshToken
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.RevokedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()");
            
            entity.HasOne(e => e.User).WithMany(u => u.RefreshTokens);
            entity.HasOne(e => e.ReplacedByToken).WithMany().HasForeignKey(e => e.ReplacedByTokenId).IsRequired(false);
        });
    }
}
```

- [ ] **Step 2: Write DesignTimeDbContextFactory** for `dotnet ef` CLI

```csharp
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=nexsync;Username=nexsync;Password=nexsync_dev");
        return new AppDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 3: Create initial migration**

```bash
cd backend
dotnet ef migrations add InitialCreate --project src/NexSync.Infrastructure --startup-project src/NexSync.API
```

Expected: Migration files created in `src/NexSync.Infrastructure/Data/Migrations/`.

- [ ] **Step 4: Verify migration SQL** (inspect generated migration file)

Confirm:
- Partial unique indexes exist with correct WHERE clauses
- TIMESTAMPTZ columns used
- Foreign keys configured
- Cascade behavior correct

- [ ] **Step 5: Commit**

```bash
git add backend/src/NexSync.Infrastructure/Data/
git commit -m "feat: add EF Core DbContext and initial migration"
```

---

## 🔒 ARCHITECTURE GATE — After Task 3

Before proceeding to services and controllers, verify the foundation is clean:

- [ ] **Domain Layer** — No dependencies on Application, Infrastructure, or API
- [ ] **Application Layer** — Interfaces only; no EF Core DbContext references; no HTTP types
- [ ] **Infrastructure Layer** — Implements Application interfaces; DbContext isolated to Infrastructure
- [ ] **API Layer** — Contains only controllers and middleware; no business logic; depends on Application
- [ ] **DTOs** — Do not expose EF Core entities; DTO layer between API and Application
- [ ] **Storage Abstraction** — `IStorageService` interface defined; Phase 7 MinIO substitutable
- [ ] **Repository Interfaces** — Defined in Application; implementations in Infrastructure
- [ ] **EF Core Configuration** — All fluent API and OnModelCreating in `AppDbContext.cs`, not scattered

**Reviewer checklist:**
- Can you remove Infrastructure without breaking Application layer?
- Can you swap PostgreSQL for MySQL by changing only Infrastructure?
- Are all domain entities independent of any framework?

If all items pass, proceed to Task 4. If any fail, fix the architecture before continuing.

---

### Task 4: Repository Pattern & Dependency Injection

**Files:**
- Create: `backend/src/NexSync.Infrastructure/Repositories/IRepository.cs`
- Create: `backend/src/NexSync.Infrastructure/Repositories/UserRepository.cs`
- Create: `backend/src/NexSync.Infrastructure/Repositories/FolderRepository.cs`
- Create: `backend/src/NexSync.Infrastructure/Repositories/FileRepository.cs`
- Create: `backend/src/NexSync.Infrastructure/Repositories/RefreshTokenRepository.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IUserRepository.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IFolderRepository.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IFileRepository.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IRefreshTokenRepository.cs`
- Create: `backend/src/NexSync.API/Extensions/ServiceCollectionExtensions.cs`
- Modify: `backend/src/NexSync.API/Program.cs`

**Interfaces:**
- Consumes: AppDbContext, entities
- Produces: IRepository interfaces, concrete repository classes, DI configuration

- [ ] **Step 1: Write generic IRepository interface**

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
}
```

- [ ] **Step 2: Write generic Repository base class**

```csharp
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    
    public Repository(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<T?> GetByIdAsync(Guid id)
    {
        return await _context.Set<T>().FindAsync(id);
    }
    
    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _context.Set<T>().ToListAsync();
    }
    
    public async Task AddAsync(T entity)
    {
        await _context.Set<T>().AddAsync(entity);
        await SaveChangesAsync();
    }
    
    public async Task UpdateAsync(T entity)
    {
        _context.Set<T>().Update(entity);
        await SaveChangesAsync();
    }
    
    public async Task DeleteAsync(T entity)
    {
        _context.Set<T>().Remove(entity);
        await SaveChangesAsync();
    }
    
    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Write IUserRepository interface**

```csharp
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<bool> EmailExistsAsync(string email);
}
```

- [ ] **Step 4: Write UserRepository implementation**

```csharp
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }
    
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }
    
    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
    }
}
```

- [ ] **Step 5: Write IFolderRepository interface**

```csharp
public interface IFolderRepository : IRepository<Folder>
{
    Task<Folder?> GetByIdWithOwnerAsync(Guid id);
    Task<IEnumerable<Folder>> GetByParentAsync(Guid userId, Guid? parentId, int page, int pageSize);
    Task<int> GetCountByParentAsync(Guid userId, Guid? parentId);
    Task<bool> NameExistsInParentAsync(Guid userId, Guid? parentId, string name);
    Task<IEnumerable<Folder>> GetDescendantsAsync(Guid folderId);
}
```

- [ ] **Step 6: Write FolderRepository implementation**

Include methods for querying folders by parent, checking name uniqueness, and traversing the tree.

- [ ] **Step 7: Write IFileRepository interface**

```csharp
public interface IFileRepository : IRepository<File>
{
    Task<File?> GetByIdWithOwnerAsync(Guid id);
    Task<IEnumerable<File>> GetByFolderAsync(Guid userId, Guid? folderId, int page, int pageSize);
    Task<int> GetCountByFolderAsync(Guid userId, Guid? folderId);
    Task<bool> NameExistsInFolderAsync(Guid userId, Guid? folderId, string name);
    Task<int> GetCountByStoragePathAsync(string storagePath);
}
```

- [ ] **Step 8: Write FileRepository implementation**

- [ ] **Step 9: Write IRefreshTokenRepository interface**

```csharp
public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task<RefreshToken?> GetByIdWithUserAsync(Guid id);
    Task RevokeTokenFamilyAsync(Guid originalTokenId);
    Task<IEnumerable<RefreshToken>> GetActiveTokensByUserAsync(Guid userId);
}
```

- [ ] **Step 10: Write RefreshTokenRepository implementation**

Include logic for token family revocation by walking the `ReplacedByTokenId` chain.

- [ ] **Step 11: Write ServiceCollectionExtensions**

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("DefaultConnection")));
        
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        
        return services;
    }
}
```

- [ ] **Step 12: Update Program.cs** to call AddInfrastructure()

```csharp
builder.Services
    .AddInfrastructure(builder.Configuration);
```

- [ ] **Step 13: Verify solution still builds**

```bash
dotnet build
```

- [ ] **Step 14: Commit**

```bash
git add backend/src/NexSync.Application/Interfaces/ backend/src/NexSync.Infrastructure/Repositories/ backend/src/NexSync.API/Extensions/ backend/src/NexSync.API/Program.cs
git commit -m "feat: add repository pattern and dependency injection"
```

---

### Task 5: Authentication Service — JWT, BCrypt, Tokens

**Files:**
- Create: `backend/src/NexSync.Infrastructure/Authentication/PasswordHasher.cs`
- Create: `backend/src/NexSync.Infrastructure/Authentication/TokenProvider.cs`
- Create: `backend/src/NexSync.Application/Interfaces/ITokenService.cs`
- Create: `backend/src/NexSync.Application/Services/TokenService.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IAuthService.cs`
- Create: `backend/src/NexSync.Application/DTOs/AuthDtos.cs`
- Create: `backend/src/NexSync.Application/Services/AuthService.cs`

**Interfaces:**
- Consumes: User, RefreshToken repositories; config (Jwt section); BCrypt.Net-Next
- Produces: PasswordHasher, TokenProvider, ITokenService, IAuthService with Register/Login/Refresh/Logout

**Security Contract (MUST be implemented in full):**
- Access token: JWT, 15 min expiry, memory-only on client, contains UserId + Email claims
- Refresh token: HttpOnly + Secure + SameSite=Strict cookie, SHA-256 hashed in DB, 7 day expiry
- Token rotation: each refresh invalidates old token and issues new one; old token marked `RevokedAt = now()`, `ReplacedByTokenId` points to new token
- Token family tracking: if a revoked token is reused (after rotation), walk the chain backward to find the original token ID and revoke all tokens in the family
- CSRF protection: refresh and logout endpoints require `X-CSRF: 1` header (in addition to SameSite=Strict cookie)
- Password: BCrypt work factor 12, minimum 8 chars, maximum 72 chars (BCrypt input limit enforced)
- Email: normalized to lowercase on storage and lookup

- [ ] **Step 1: Write PasswordHasher**

```csharp
public class PasswordHasher
{
    private const int WorkFactor = 12;
    
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }
    
    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
```

- [ ] **Step 2: Write TokenProvider** for JWT generation

```csharp
public class TokenProvider
{
    private readonly IConfiguration _config;
    
    public TokenProvider(IConfiguration config)
    {
        _config = config;
    }
    
    public string GenerateAccessToken(Guid userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"] ?? ""));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email)
        };
        
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "15")),
            signingCredentials: credentials
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}
```

- [ ] **Step 3: Write ITokenService interface**

```csharp
public interface ITokenService
{
    Task<RefreshToken> CreateRefreshTokenAsync(Guid userId);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string tokenHash);
    Task RevokeRefreshTokenAsync(Guid tokenId);
    Task RevokeTokenFamilyAsync(Guid tokenId);
    string HashToken(string token);
}
```

- [ ] **Step 4: Write TokenService implementation**

```csharp
public class TokenService : ITokenService
{
    private readonly IRefreshTokenRepository _tokenRepository;
    
    public TokenService(IRefreshTokenRepository tokenRepository)
    {
        _tokenRepository = tokenRepository;
    }
    
    public async Task<RefreshToken> CreateRefreshTokenAsync(Guid userId)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(Guid.NewGuid().ToString()),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        
        await _tokenRepository.AddAsync(token);
        return token;
    }
    
    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string tokenHash)
    {
        var token = await _tokenRepository.GetByTokenHashAsync(tokenHash);
        
        if (token is null || token.RevokedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            return null;
        
        return token;
    }
    
    public async Task RevokeRefreshTokenAsync(Guid tokenId)
    {
        var token = await _tokenRepository.GetByIdAsync(tokenId);
        if (token is not null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _tokenRepository.UpdateAsync(token);
        }
    }
    
    public async Task RevokeTokenFamilyAsync(Guid tokenId)
    {
        await _tokenRepository.RevokeTokenFamilyAsync(tokenId);
    }
    
    public string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }
}
```

- [ ] **Step 5: Write AuthDtos**

```csharp
public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, UserDto User);
public record UserDto(Guid Id, string Email, string FullName);
public record LogoutRequest(string RefreshToken);
```

- [ ] **Step 6: Write IAuthService interface**

```csharp
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request);
    Task LogoutAsync(LogoutRequest request);
}
```

- [ ] **Step 7: Write AuthService implementation**

```csharp
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly TokenProvider _tokenProvider;
    private readonly PasswordHasher _passwordHasher;
    
    public AuthService(
        IUserRepository userRepository,
        ITokenService tokenService,
        TokenProvider tokenProvider,
        PasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _tokenProvider = tokenProvider;
        _passwordHasher = passwordHasher;
    }
    
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _userRepository.EmailExistsAsync(request.Email))
            throw new ConflictException("Email already registered");
        
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLower(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = request.FullName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        await _userRepository.AddAsync(user);
        
        var refreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, refreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user is null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");
        
        var refreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, refreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var token = await _tokenService.ValidateRefreshTokenAsync(tokenHash);
        
        if (token is null)
        {
            await _tokenService.RevokeTokenFamilyAsync(token!.Id);
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }
        
        var user = await _userRepository.GetByIdAsync(token.UserId);
        if (user is null)
            throw new UnauthorizedAccessException("User not found");
        
        token.RevokedAt = DateTime.UtcNow;
        var newRefreshToken = await _tokenService.CreateRefreshTokenAsync(user.Id);
        newRefreshToken.ReplacedByTokenId = newRefreshToken.Id;
        await _tokenService.UpdateAsync(token);
        
        var accessToken = _tokenProvider.GenerateAccessToken(user.Id, user.Email);
        
        return new AuthResponse(accessToken, newRefreshToken.TokenHash, new UserDto(user.Id, user.Email, user.FullName));
    }
    
    public async Task LogoutAsync(LogoutRequest request)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var token = await _tokenService.ValidateRefreshTokenAsync(tokenHash);
        
        if (token is not null)
        {
            await _tokenService.RevokeRefreshTokenAsync(token.Id);
        }
    }
}
```

- [ ] **Step 8: Update Program.cs** with auth DI

```csharp
builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<TokenProvider>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
```

- [ ] **Step 9: Build and verify**

```bash
dotnet build
```

- [ ] **Step 10: Commit**

```bash
git add backend/src/NexSync.Infrastructure/Authentication/ backend/src/NexSync.Application/ backend/src/NexSync.API/Program.cs
git commit -m "feat: add authentication service with JWT and refresh tokens"
```

---

### Task 6: Auth Controller & Middleware

**Files:**
- Create: `backend/src/NexSync.API/Controllers/AuthController.cs`
- Create: `backend/src/NexSync.API/Middleware/ErrorHandlingMiddleware.cs`
- Modify: `backend/src/NexSync.API/Program.cs`

**Interfaces:**
- Consumes: IAuthService, auth DTOs
- Produces: POST /api/auth/register, login, refresh, logout with cookie handling; global error middleware

- [ ] **Step 1: Write AuthController**

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }
    
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken, user = response.User });
    }
    
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken, user = response.User });
    }
    
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1")
            return BadRequest("CSRF header required");
        
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized("No refresh token");
        
        var response = await _authService.RefreshTokenAsync(new RefreshTokenRequest(refreshToken));
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken });
    }
    
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1")
            return BadRequest("CSRF header required");
        
        var refreshToken = Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _authService.LogoutAsync(new LogoutRequest(refreshToken));
        }
        
        Response.Cookies.Delete("refreshToken");
        return NoContent();
    }
    
    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append(
            "refreshToken",
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}
```

- [ ] **Step 2: Write ErrorHandlingMiddleware**

```csharp
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    
    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }
    
    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var response = new ProblemDetails
        {
            Type = $"https://nexsync.dev/errors/{GetErrorCode(exception)}",
            Title = GetTitle(exception),
            Status = GetStatusCode(exception),
            Detail = exception.Message,
            Instance = context.Request.Path
        };
        
        if (context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true)
        {
            response.Extensions["traceId"] = context.TraceIdentifier;
        }
        
        context.Response.StatusCode = response.Status ?? 500;
        return context.Response.WriteAsJsonAsync(response);
    }
    
    private static string GetErrorCode(Exception ex) => ex.GetType().Name;
    private static string GetTitle(Exception ex) => ex.GetType().Name.ToKebabCase();
    private static int GetStatusCode(Exception ex) => ex switch
    {
        UnauthorizedAccessException => 401,
        ConflictException => 409,
        _ => 500
    };
}
```

- [ ] **Step 3: Update Program.cs** with middleware and JWT config

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"] ?? "");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

- [ ] **Step 4: Build and test**

```bash
dotnet build
dotnet run --project src/NexSync.API
```

Test auth endpoints with Swagger UI (available at `/swagger`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/NexSync.API/
git commit -m "feat: add auth controller and error handling middleware"
```

---

### Task 7: File Name Validation

**Files:**
- Create: `backend/src/NexSync.Application/Validators/FileNameValidator.cs`
- Modify: `backend/src/NexSync.Application/Services/FolderService.cs`
- Modify: `backend/src/NexSync.Application/Services/FileService.cs`

**Interfaces:**
- Produces: FileNameValidator enforcing Windows filename safety for Phase 3 WPF compatibility

**Validation Rules (CRITICAL for multi-platform support):**
- Forbidden characters: `\ / : * ? " < > |`
- Forbidden leading/trailing: spaces, dots
- Windows reserved names (case-insensitive, with or without extension): CON, PRN, AUX, NUL, COM1-9, LPT1-9
- Maximum length: 255 characters
- Reason: Phase 3 WPF desktop client will sync to Windows filesystem; files must be creatable on Windows immediately, not fail silently mid-sync

- [ ] **Step 1: Write FileNameValidator**

```csharp
public class FileNameValidator
{
    private static readonly char[] ForbiddenCharacters = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
    private static readonly string[] WindowsReservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    
    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FileNameInvalidException("File name cannot be empty");
        
        name = name.Trim();
        
        if (name.Length > 255)
            throw new FileNameInvalidException("File name exceeds 255 characters");
        
        if (name.EndsWith(" ") || name.EndsWith("."))
            throw new FileNameInvalidException("File name cannot end with space or dot");
        
        if (name.Any(c => ForbiddenCharacters.Contains(c)))
            throw new FileNameInvalidException("File name contains forbidden characters");
        
        var baseName = Path.GetFileNameWithoutExtension(name).ToUpper();
        if (WindowsReservedNames.Contains(baseName))
            throw new FileNameInvalidException("File name is a Windows reserved name");
    }
}
```

- [ ] **Step 2: Update FolderService** to validate on create/update

Add `FileNameValidator.Validate(name)` calls in FolderService.Create() and FolderService.Update().

- [ ] **Step 3: Update FileService** to validate on upload/rename

Add `FileNameValidator.Validate(name)` calls in FileService.Upload() and FileService.Update().

- [ ] **Step 4: Write unit tests for FileNameValidator**

Test:
- Empty name → throws
- Name with `?` → throws
- Name ending with space → throws
- Name ending with dot → throws
- "CON" → throws
- "valid_name.txt" → passes
- "My Folder" → passes

- [ ] **Step 5: Commit**

```bash
git add backend/src/NexSync.Application/Validators/ backend/src/NexSync.Application/Services/
git commit -m "feat: add file name validation for Windows compatibility"
```

---

### Task 8: Folder Service & Controller

**Files:**
- Create: `backend/src/NexSync.Application/DTOs/FolderDtos.cs`
- Create: `backend/src/NexSync.Application/Services/FolderService.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IFolderService.cs`
- Create: `backend/src/NexSync.API/Controllers/FoldersController.cs`

**Interfaces:**
- Consumes: IFolderRepository, FileNameValidator, auth context
- Produces: GET /api/folders, POST, PUT, DELETE with ownership checks, cycle prevention

**Cycle Prevention (CRITICAL):**
- Cannot move folder into itself: `folderId == parentFolderId` → reject
- Cannot move folder into any descendant: walk `GetDescendantsAsync(folderId)` and check if target parent ID appears in descendants
- Test cases required:
  - A → A (self-reference) → 400 Conflict
  - A has child B, B has child C; Move A → B → 400 Conflict
  - Move A → B → 400 Conflict
  - Move C → root (sibling move) → 200 OK
  - Move C → A (move to ancestor) → 200 OK

- [ ] **Step 1: Write FolderDtos**

```csharp
public record CreateFolderRequest(string Name, Guid? ParentFolderId);
public record UpdateFolderRequest(string Name, Guid? ParentFolderId);
public record FolderDto(Guid Id, string Name, Guid? ParentFolderId, DateTime CreatedAt, DateTime UpdatedAt);
public record FolderContentsResponse(List<FolderDto> Folders, List<FileDto> Files, int Page, int PageSize, int TotalCount, int TotalPages);
```

- [ ] **Step 2: Write IFolderService interface**

```csharp
public interface IFolderService
{
    Task<FolderContentsResponse> GetContentsAsync(Guid userId, Guid? parentId, int page, int pageSize);
    Task<FolderDto> GetByIdAsync(Guid userId, Guid folderId);
    Task<FolderDto> CreateAsync(Guid userId, string name, Guid? parentFolderId);
    Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, string name, Guid? parentFolderId);
    Task DeleteAsync(Guid userId, Guid folderId);
}
```

- [ ] **Step 3: Write FolderService implementation**

```csharp
public class FolderService : IFolderService
{
    private readonly IFolderRepository _folderRepository;
    private readonly IFileRepository _fileRepository;
    private readonly IStorageService _storageService;
    
    public FolderService(
        IFolderRepository folderRepository,
        IFileRepository fileRepository,
        IStorageService storageService)
    {
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _storageService = storageService;
    }
    
    public async Task<FolderContentsResponse> GetContentsAsync(Guid userId, Guid? parentId, int page, int pageSize)
    {
        if (parentId.HasValue)
        {
            var parent = await _folderRepository.GetByIdAsync(parentId.Value);
            if (parent is null || parent.OwnerId != userId)
                throw new UnauthorizedAccessException("Access denied");
        }
        
        const int maxPageSize = 100;
        pageSize = Math.Min(pageSize, maxPageSize);
        
        var folders = await _folderRepository.GetByParentAsync(userId, parentId, page, pageSize);
        var files = await _fileRepository.GetByFolderAsync(userId, parentId, page, pageSize);
        
        var totalCount = await _folderRepository.GetCountByParentAsync(userId, parentId);
        totalCount += await _fileRepository.GetCountByFolderAsync(userId, parentId);
        
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        
        return new FolderContentsResponse(
            folders.Select(f => MapToDto(f)).ToList(),
            files.Select(f => MapToDto(f)).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages);
    }
    
    public async Task<FolderDto> GetByIdAsync(Guid userId, Guid folderId)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        return MapToDto(folder);
    }
    
    public async Task<FolderDto> CreateAsync(Guid userId, string name, Guid? parentFolderId)
    {
        FileNameValidator.Validate(name);
        
        if (parentFolderId.HasValue)
        {
            var parent = await _folderRepository.GetByIdAsync(parentFolderId.Value);
            if (parent is null || parent.OwnerId != userId)
                throw new UnauthorizedAccessException("Access denied");
        }
        
        if (await _folderRepository.NameExistsInParentAsync(userId, parentFolderId, name))
            throw new ConflictException("Folder name already exists");
        
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentFolderId = parentFolderId,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        await _folderRepository.AddAsync(folder);
        return MapToDto(folder);
    }
    
    public async Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, string name, Guid? parentFolderId)
    {
        FileNameValidator.Validate(name);
        
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        // Prevent moving folder into itself
        if (folderId == parentFolderId)
            throw new ConflictException("Cannot move folder into itself");
        
        // Prevent moving into descendant
        if (parentFolderId.HasValue)
        {
            var descendants = await _folderRepository.GetDescendantsAsync(folderId);
            if (descendants.Any(d => d.Id == parentFolderId))
                throw new ConflictException("Cannot move folder into one of its descendants");
            
            var targetParent = await _folderRepository.GetByIdAsync(parentFolderId.Value);
            if (targetParent is null || targetParent.OwnerId != userId)
                throw new UnauthorizedAccessException("Access denied");
        }
        
        if (await _folderRepository.NameExistsInParentAsync(userId, parentFolderId, name))
            throw new ConflictException("Folder name already exists in target location");
        
        folder.Name = name;
        folder.ParentFolderId = parentFolderId;
        folder.UpdatedAt = DateTime.UtcNow;
        
        await _folderRepository.UpdateAsync(folder);
        return MapToDto(folder);
    }
    
    public async Task DeleteAsync(Guid userId, Guid folderId)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        // Collect all files in folder tree and delete physical storage
        var descendants = await _folderRepository.GetDescendantsAsync(folderId);
        var allFolderIds = new[] { folderId }.Concat(descendants.Select(d => d.Id)).ToList();
        
        foreach (var f in allFolderIds)
        {
            var files = await _fileRepository.GetByFolderAsync(userId, f, 1, 1000);
            foreach (var file in files)
            {
                var refCount = await _fileRepository.GetCountByStoragePathAsync(file.StoragePath);
                if (refCount == 1)
                {
                    await _storageService.DeleteFileAsync(file.StoragePath);
                }
                await _fileRepository.DeleteAsync(file);
            }
        }
        
        // Delete folders (children first due to FK)
        foreach (var f in allFolderIds.Reverse<Guid>())
        {
            var folderToDelete = await _folderRepository.GetByIdAsync(f);
            if (folderToDelete is not null)
                await _folderRepository.DeleteAsync(folderToDelete);
        }
    }
    
    private static FolderDto MapToDto(Folder folder) =>
        new(folder.Id, folder.Name, folder.ParentFolderId, folder.CreatedAt, folder.UpdatedAt);
}
```

- [ ] **Step 4: Write FoldersController**

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;
    
    public FoldersController(IFolderService folderService)
    {
        _folderService = folderService;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetContents([FromQuery] Guid? parentId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var response = await _folderService.GetContentsAsync(uid, parentId, page, pageSize);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var folder = await _folderService.GetByIdAsync(uid, id);
        return Ok(folder);
    }
    
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var folder = await _folderService.CreateAsync(uid, request.Name, request.ParentFolderId);
        return CreatedAtAction(nameof(GetById), new { id = folder.Id }, folder);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var folder = await _folderService.UpdateAsync(uid, id, request.Name, request.ParentFolderId);
        return Ok(folder);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        await _folderService.DeleteAsync(uid, id);
        return NoContent();
    }
}
```

- [ ] **Step 5: Update Program.cs** with FolderService DI

```csharp
builder.Services.AddScoped<IFolderService, FolderService>();
```

- [ ] **Step 6: Build and verify**

```bash
dotnet build
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/NexSync.Application/ backend/src/NexSync.API/Controllers/FoldersController.cs backend/src/NexSync.API/Program.cs
git commit -m "feat: add folder service and controller with cycle prevention"
```

---

### Task 9: Storage Service & File Upload

**Files:**
- Create: `backend/src/NexSync.Application/Interfaces/IStorageService.cs`
- Create: `backend/src/NexSync.Infrastructure/Storage/LocalStorageService.cs`
- Create: `backend/src/NexSync.Application/DTOs/FileDtos.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IFileService.cs`
- Create: `backend/src/NexSync.Application/Services/FileService.cs`
- Create: `backend/src/NexSync.API/Controllers/FilesController.cs`

**Interfaces:**
- Consumes: IFileRepository, config (Storage:RootPath), SHA-256
- Produces: IStorageService with streaming upload, content-addressed reuse; IFileService with full CRUD

**Streaming & Concurrent Upload Handling (CRITICAL):**
- Stream to temporary file (NOT memory buffer) while computing SHA-256
- If final path `{StorageRoot}/{OwnerId}/{SHA256Hash}` already exists:
  - Discard temporary file (do NOT overwrite final path)
  - Return the existing storage path (reuse)
  - This safely handles two simultaneous identical uploads — both get the same physical object
- If final path does not exist:
  - Move temporary file to final path (atomic rename)
  - Return new storage path
- On database insertion failure:
  - Check: does any other File record reference this storage path?
  - If NO other references → delete physical file (cleanup)
  - If YES other references → leave it alone (safe reuse)
  - Temporary file always cleaned up regardless

- [ ] **Step 1: Write IStorageService interface**

```csharp
public interface IStorageService
{
    Task<(string storagePath, string hash, long size)> SaveFileAsync(Guid ownerId, Stream stream, string fileName);
    Task<Stream> GetFileAsync(string storagePath);
    Task DeleteFileAsync(string storagePath);
    Task<bool> FileExistsAsync(string storagePath);
}
```

- [ ] **Step 2: Write LocalStorageService** with streaming and hash computation

```csharp
public class LocalStorageService : IStorageService
{
    private readonly string _rootPath;
    private readonly long _maxFileSize;
    
    public LocalStorageService(IConfiguration config)
    {
        _rootPath = config["Storage:RootPath"] ?? "./storage";
        _maxFileSize = long.Parse(config["Storage:MaxFileSizeBytes"] ?? "104857600");
        
        Directory.CreateDirectory(_rootPath);
    }
    
    public async Task<(string storagePath, string hash, long size)> SaveFileAsync(Guid ownerId, Stream stream, string fileName)
    {
        var tempPath = Path.Combine(_rootPath, "temp", Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var fileStream = System.IO.File.Create(tempPath);
        
        var buffer = new byte[8192];
        int bytesRead;
        long totalBytes = 0;
        
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > _maxFileSize)
                throw new InvalidOperationException("File exceeds maximum size");
            
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }
        
        sha256.TransformFinalBlock(new byte[0], 0, 0);
        var hash = Convert.ToHexString(sha256.Hash!);
        
        var userPath = Path.Combine(_rootPath, ownerId.ToString());
        Directory.CreateDirectory(userPath);
        
        var finalPath = Path.Combine(userPath, hash);
        
        // If file already exists (dedup), discard temp and reuse
        if (System.IO.File.Exists(finalPath))
        {
            System.IO.File.Delete(tempPath);
            return (finalPath, hash, totalBytes);
        }
        
        // Move temp to final
        System.IO.File.Move(tempPath, finalPath, overwrite: false);
        
        return (finalPath, hash, totalBytes);
    }
    
    public async Task<Stream> GetFileAsync(string storagePath)
    {
        if (!System.IO.File.Exists(storagePath))
            throw new FileNotFoundException($"File not found at {storagePath}");
        
        return System.IO.File.OpenRead(storagePath);
    }
    
    public async Task DeleteFileAsync(string storagePath)
    {
        if (System.IO.File.Exists(storagePath))
        {
            System.IO.File.Delete(storagePath);
        }
    }
    
    public async Task<bool> FileExistsAsync(string storagePath)
    {
        return System.IO.File.Exists(storagePath);
    }
}
```

- [ ] **Step 3: Write FileDtos**

```csharp
public record FileDto(Guid Id, string Name, Guid? FolderId, string Hash, long Size, string ContentType, DateTime CreatedAt, DateTime UpdatedAt);
public record UpdateFileRequest(string Name, Guid? FolderId);
```

- [ ] **Step 4: Write IFileService interface**

```csharp
public interface IFileService
{
    Task<FileDto> UploadAsync(Guid userId, IFormFile file, Guid? folderId);
    Task<FileDto> GetByIdAsync(Guid userId, Guid fileId);
    Task<Stream> DownloadAsync(Guid userId, Guid fileId);
    Task<FileDto> UpdateAsync(Guid userId, Guid fileId, string name, Guid? folderId);
    Task DeleteAsync(Guid userId, Guid fileId);
}
```

- [ ] **Step 5: Write FileService implementation**

```csharp
public class FileService : IFileService
{
    private readonly IFileRepository _fileRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IStorageService _storageService;
    
    public FileService(
        IFileRepository fileRepository,
        IFolderRepository folderRepository,
        IStorageService storageService)
    {
        _fileRepository = fileRepository;
        _folderRepository = folderRepository;
        _storageService = storageService;
    }
    
    public async Task<FileDto> UploadAsync(Guid userId, IFormFile file, Guid? folderId)
    {
        FileNameValidator.Validate(file.FileName);
        
        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetByIdAsync(folderId.Value);
            if (folder is null || folder.OwnerId != userId)
                throw new UnauthorizedAccessException("Access denied");
        }
        
        if (await _fileRepository.NameExistsInFolderAsync(userId, folderId, file.FileName))
            throw new ConflictException("File name already exists");
        
        // Stream to storage
        using var stream = file.OpenReadStream();
        var (storagePath, hash, size) = await _storageService.SaveFileAsync(userId, stream, file.FileName);
        
        // Create metadata record
        var newFile = new File
        {
            Id = Guid.NewGuid(),
            Name = file.FileName,
            FolderId = folderId,
            OwnerId = userId,
            Hash = hash,
            Size = size,
            ContentType = file.ContentType,
            StoragePath = storagePath,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        try
        {
            await _fileRepository.AddAsync(newFile);
        }
        catch
        {
            // Compensation: delete storage if not reused
            var refCount = await _fileRepository.GetCountByStoragePathAsync(storagePath);
            if (refCount == 0)
            {
                await _storageService.DeleteFileAsync(storagePath);
            }
            throw;
        }
        
        return MapToDto(newFile);
    }
    
    public async Task<FileDto> GetByIdAsync(Guid userId, Guid fileId)
    {
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file is null || file.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        return MapToDto(file);
    }
    
    public async Task<Stream> DownloadAsync(Guid userId, Guid fileId)
    {
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file is null || file.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        return await _storageService.GetFileAsync(file.StoragePath);
    }
    
    public async Task<FileDto> UpdateAsync(Guid userId, Guid fileId, string name, Guid? folderId)
    {
        FileNameValidator.Validate(name);
        
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file is null || file.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetByIdAsync(folderId.Value);
            if (folder is null || folder.OwnerId != userId)
                throw new UnauthorizedAccessException("Access denied");
        }
        
        if (await _fileRepository.NameExistsInFolderAsync(userId, folderId, name))
            throw new ConflictException("File name already exists in target location");
        
        file.Name = name;
        file.FolderId = folderId;
        file.UpdatedAt = DateTime.UtcNow;
        
        await _fileRepository.UpdateAsync(file);
        return MapToDto(file);
    }
    
    public async Task DeleteAsync(Guid userId, Guid fileId)
    {
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file is null || file.OwnerId != userId)
            throw new UnauthorizedAccessException("Access denied");
        
        // Check if any other records reference this storage path
        var refCount = await _fileRepository.GetCountByStoragePathAsync(file.StoragePath);
        if (refCount == 1)
        {
            await _storageService.DeleteFileAsync(file.StoragePath);
        }
        
        await _fileRepository.DeleteAsync(file);
    }
    
    private static FileDto MapToDto(File file) =>
        new(file.Id, file.Name, file.FolderId, file.Hash, file.Size, file.ContentType, file.CreatedAt, file.UpdatedAt);
}
```

- [ ] **Step 6: Write FilesController**

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    
    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] Guid? folderId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var result = await _fileService.UploadAsync(uid, file, folderId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var file = await _fileService.GetByIdAsync(uid, id);
        return Ok(file);
    }
    
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var file = await _fileService.GetByIdAsync(uid, id);
        var stream = await _fileService.DownloadAsync(uid, id);
        
        return File(stream, file.ContentType, file.Name);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFileRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        var file = await _fileService.UpdateAsync(uid, id, request.Name, request.FolderId);
        return Ok(file);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var uid))
            return Unauthorized();
        
        await _fileService.DeleteAsync(uid, id);
        return NoContent();
    }
}
```

- [ ] **Step 7: Update Program.cs** with DI

```csharp
builder.Services.AddScoped<IStorageService, LocalStorageService>();
builder.Services.AddScoped<IFileService, FileService>();
```

- [ ] **Step 8: Build and verify**

```bash
dotnet build
```

- [ ] **Step 9: Commit**

```bash
git add backend/src/NexSync.Infrastructure/Storage/ backend/src/NexSync.Application/Services/FileService.cs backend/src/NexSync.Application/Interfaces/IFileService.cs backend/src/NexSync.Application/DTOs/FileDtos.cs backend/src/NexSync.API/Controllers/FilesController.cs backend/src/NexSync.API/Program.cs
git commit -m "feat: add file storage service and file controller with streaming upload"
```

---

### Task 10: Integration Tests — Auth & Authorization

**Files:**
- Create: `backend/tests/NexSync.Tests/Integration/AuthFlowTests.cs`
- Create: `backend/tests/NexSync.Tests/Integration/AuthorizationTests.cs`
- Modify: `backend/tests/NexSync.Tests/NexSync.Tests.csproj`

**Interfaces:**
- Produces: Comprehensive integration tests for auth, authorization, storage safety, and failure paths

**Test Coverage (CRITICAL — all tests required, not optional):**

Auth Flow:
- [ ] Register → creates user, returns JWT + refresh token
- [ ] Invalid email on register → 400
- [ ] Duplicate email → 409 Conflict
- [ ] Login with correct password → 200
- [ ] Login with wrong password → 401
- [ ] Refresh token → rotates, old revoked, new valid
- [ ] Refresh expired token → 401
- [ ] Refresh revoked token (after rotation) → 401 + entire family revoked
- [ ] Logout → revokes token, subsequent refresh fails

Authorization & Access Control:
- [ ] User A cannot GET User B's folder (403 Forbidden, not 404)
- [ ] User A cannot GET User B's file by UUID (403 Forbidden)
- [ ] User A cannot download User B's file (403)
- [ ] User A cannot move User B's folder (403)
- [ ] User A cannot rename User B's file (403)
- [ ] User A cannot delete User B's folder (403)
- [ ] User A cannot upload file to User B's folder (403)
- [ ] Expired JWT rejected (401)
- [ ] Missing JWT rejected (401)

Storage & Content-Addressing:
- [ ] Two uploads of identical content → both get same storage path, single physical file
- [ ] Delete first file → physical file preserved (second file still references it)
- [ ] Delete second file → physical file deleted (no remaining references)
- [ ] File deletion with non-existent storage path → no crash
- [ ] Upload with DB failure → physical file cleaned up if no other references

Folder Cycles:
- [ ] Move folder to itself → 400 Conflict
- [ ] Move folder A into descendant C → 400 Conflict
- [ ] Move folder to sibling → 200 OK

Windows Filename Safety:
- [ ] Create folder named "CON" → 400 (reserved)
- [ ] Upload file "test?.txt" → 400 (forbidden char)
- [ ] Create file "test." (trailing dot) → 400
- [ ] Create file "test " (trailing space) → 400

- [ ] **Step 1: Add test dependencies to NexSync.Tests.csproj**

```bash
cd backend/tests/NexSync.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Testcontainers.PostgreSql
```

- [ ] **Step 2: Write AuthFlowTests** with all auth flow test cases above

- [ ] **Step 3: Write AuthorizationTests** with all access control test cases above

- [ ] **Step 4: Write StorageTests** with content-addressing and cycle test cases above

- [ ] **Step 5: Run tests**

```bash
dotnet test
```

Expected: All 40+ tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/tests/
git commit -m "test: add comprehensive integration tests for auth, authorization, and storage"
```

---

### Task 11: React Setup & Auth Context

**Files:**
- Create: `web/package.json`
- Create: `web/vite.config.ts`
- Create: `web/tsconfig.json`
- Create: `web/tailwind.config.ts`
- Create: `web/index.html`
- Create: `web/src/main.tsx`
- Create: `web/src/App.tsx`
- Create: `web/src/api/client.ts`
- Create: `web/src/contexts/AuthContext.tsx`
- Create: `web/src/types/auth.ts`
- Create: `web/src/index.css`

**Interfaces:**
- Produces: React app with Vite, TypeScript, Tailwind, Axios client with token/cookie handling

- [ ] **Step 1: Create React app scaffold**

```bash
cd web
npm create vite@latest . -- --template react-ts
npm install
```

- [ ] **Step 2: Install dependencies**

```bash
npm install axios react-router-dom
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p
```

- [ ] **Step 3: Configure Tailwind**

Create `tailwind.config.ts` and `src/index.css` with Tailwind directives.

- [ ] **Step 4: Write Axios client with interceptor**

```typescript
const client = axios.create({
  baseURL: 'http://localhost:5000/api',
  withCredentials: true
});

client.interceptors.response.use(
  response => response,
  async error => {
    const original = error.config;
    
    if (error.response?.status === 401 && !original._retry) {
      original._retry = true;
      try {
        const response = await client.post('/auth/refresh', {}, {
          headers: { 'X-CSRF': '1' }
        });
        const { accessToken } = response.data;
        localStorage.setItem('accessToken', accessToken);
        
        original.headers.Authorization = `Bearer ${accessToken}`;
        return client(original);
      } catch {
        localStorage.removeItem('accessToken');
        window.location.href = '/login';
      }
    }
    
    return Promise.reject(error);
  }
);

export default client;
```

- [ ] **Step 5: Write AuthContext**

Store access token in memory, provide useAuth hook, manage login/logout/register.

- [ ] **Step 6: Build and verify**

```bash
npm run build
```

Expected: No errors.

- [ ] **Step 7: Commit**

```bash
git add web/
git commit -m "feat: initialize React app with TypeScript and Tailwind"
```

---

### Task 12: React Auth Pages — Login & Register

**Files:**
- Create: `web/src/pages/LoginPage.tsx`
- Create: `web/src/pages/RegisterPage.tsx`
- Create: `web/src/types/auth.ts` (extend with form types)

**Interfaces:**
- Consumes: AuthContext, Axios client
- Produces: Login and register pages with form validation

- [ ] **Step 1: Write LoginPage**

Form: email, password
Actions: submit → call auth service → redirect to dashboard or show error

- [ ] **Step 2: Write RegisterPage**

Form: email, password, fullName
Validation: password min 8 chars, email format

- [ ] **Step 3: Update App.tsx** with Router

Define routes: /login, /register, /

- [ ] **Step 4: Build and test locally**

```bash
npm run dev
```

Navigate to http://localhost:5173/login

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/ web/src/App.tsx
git commit -m "feat: add login and register pages"
```

---

### Task 13: React File Browser — Dashboard

**Files:**
- Create: `web/src/pages/DashboardPage.tsx`
- Create: `web/src/components/FileBrowser.tsx`
- Create: `web/src/components/FolderBreadcrumb.tsx`
- Create: `web/src/components/FileList.tsx`
- Create: `web/src/components/FolderList.tsx`
- Create: `web/src/api/folders.ts`
- Create: `web/src/api/files.ts`
- Create: `web/src/types/folder.ts`
- Create: `web/src/types/file.ts`

**Interfaces:**
- Consumes: Axios client, API endpoints for folders and files
- Produces: Dashboard with file browser, breadcrumbs, folder/file lists, pagination

- [ ] **Step 1: Write API modules** (folders.ts, files.ts)

Export functions: getContents, createFolder, uploadFile, downloadFile, etc.

- [ ] **Step 2: Write type definitions** (folder.ts, file.ts)

Define FolderDto, FileDto, folder contents response.

- [ ] **Step 3: Write FileBrowser component**

State: currentFolder, files, folders, page, pageSize
Actions: navigate into folder, back via breadcrumb, create folder, upload file, delete, rename

- [ ] **Step 4: Write FolderBreadcrumb component**

Display path: Root > Projects > Form4x
Click to navigate.

- [ ] **Step 5: Write FolderList & FileList components**

Display folders and files with icons.

- [ ] **Step 6: Write DashboardPage**

Wrapper with FileBrowser.

- [ ] **Step 7: Build and test**

```bash
npm run dev
```

Login, navigate to dashboard, create folder, upload file.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/DashboardPage.tsx web/src/components/ web/src/api/ web/src/types/
git commit -m "feat: add file browser dashboard with folder and file management"
```

---

### Task 14: End-to-End Integration Test

**Files:**
- Modify: `backend/src/NexSync.API/Program.cs` (enable OpenAPI in development)
- No new files; tests use running backend

**Interfaces:**
- Consumes: Running ASP.NET backend, running React dev server
- Produces: Manual verification of complete user flow

- [ ] **Step 1: Start backend**

```bash
cd backend
dotnet run --project src/NexSync.API
```

Swagger UI available at http://localhost:5000/swagger.

- [ ] **Step 2: Start frontend (new terminal)**

```bash
cd web
npm run dev
```

App available at http://localhost:5173.

- [ ] **Step 3: Test registration**

Navigate to /register, fill form, submit.
Expected: Redirect to dashboard with "Welcome" message.

- [ ] **Step 4: Test folder creation**

Create folder "Projects".
Expected: Folder appears in dashboard.

- [ ] **Step 5: Test file upload**

Upload a file to "Projects".
Expected: File appears in folder.

- [ ] **Step 6: Test download**

Click download on file.
Expected: File downloaded to local machine.

- [ ] **Step 7: Test rename & move**

Rename folder to "Work".
Expected: Dashboard updates.

- [ ] **Step 8: Test cross-user isolation**

Open incognito window, register as different user.
Expected: Different user sees only their files, cannot access first user's files.

- [ ] **Step 9: Commit**

No changes needed; verification complete.

```bash
git log --oneline -5
```

---

### Task 15: Final Cleanup & Documentation

**Files:**
- Create: `backend/README.md`
- Create: `web/README.md`
- Create: `DEVELOPMENT.md` (root)
- Modify: `.gitignore`

**Interfaces:**
- Produces: Documentation for running, testing, and developing

- [ ] **Step 1: Write backend README**

Setup PostgreSQL, run migrations, seed data (if needed), run tests, start server.

- [ ] **Step 2: Write frontend README**

Install dependencies, run dev server, build for production.

- [ ] **Step 3: Write DEVELOPMENT.md**

Full setup guide: prerequisites, clone, install, configure, run both backend and frontend.

- [ ] **Step 4: Update .gitignore**

Add `storage/`, `dist/`, `node_modules/`, `.env`, `.env.local`.

- [ ] **Step 5: Verify all tests pass**

```bash
cd backend
dotnet test
```

Expected: All tests pass.

- [ ] **Step 6: Final commit**

```bash
git add README.md DEVELOPMENT.md .gitignore
git commit -m "docs: add development and deployment documentation"
```

- [ ] **Step 7: View final log**

```bash
git log --oneline | head -20
```

---

## Success Criteria

All Phase 1 tasks complete when:

1. Backend builds without errors
2. Database migrations run successfully
3. All API endpoints functional (verified via Swagger)
4. React frontend builds without errors
5. User can register, log in, create folders, upload/download files
6. Cross-user access denied (authorization enforced)
7. Folder cycle prevention works
8. File deduplication functional
9. Integration and authorization tests pass
10. Documentation complete

---

## Next Steps After Phase 1

Once Phase 1 is complete:
- Phase 2: File watcher + sync engine (desktop client)
- Phase 3: Android client
- Phase 4: Offline mode + conflict resolution
- Phase 5: Versioning
- Phase 6: Chunked upload
- Phase 7: Deduplication + MinIO
- Phase 8: SignalR + distributed architecture
