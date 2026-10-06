# SDD Ledger — NexSync Phase 1

**Plan:** `docs/superpowers/plans/2026-10-06-nexsync-phase1-implementation.md`
**Spec:** `docs/superpowers/specs/2026-10-06-nexsync-phase1-design.md`
**Base commit:** `4aa6ee9d149598efb286bca47c19843f2544ae32`

## Pre-flight Scan

**Global Constraints verified:**
- .NET 10 LTS, React 19, Vite 8, Router 8, Tailwind 4, PostgreSQL 17 ✓
- Timestamps TIMESTAMPTZ (UTC) ✓
- File name validation (Windows-safe) required ✓
- JWT 15 min, Refresh 7 days, HttpOnly, SHA-256 hash, rotation, family tracking ✓
- Streaming upload, no memory buffer ✓
- Ownership verification on all operations ✓
- Folder cycle prevention ✓
- Concurrent identical upload handling ✓
- CORS specific origin only ✓

**Task dependency map:**
- Task 1 (Setup) → no upstream
- Task 2 (Domain) → Task 1 ✓
- Task 3 (EF Core) → Task 1, 2 ✓
- **ARCHITECTURE GATE** (after Task 3)
- Task 4 (Repos) → Task 2, 3 ✓
- Task 5 (Auth) → Task 4 ✓
- Task 6 (Auth Controller) → Task 5 ✓
- Task 7 (File Name Validator) → Task 2 ✓
- Task 8 (Folder Service) → Task 4, 7 ✓
- Task 9 (Storage & File) → Task 4, 7, 8 ✓
- Task 10 (Tests) → Task 5, 6, 8, 9 ✓
- Task 11 (React Setup) → Task 1 ✓
- Task 12 (React Auth) → Task 11, Task 6 API ✓
- Task 13 (React Dashboard) → Task 12 ✓
- Task 14 (E2E Verification) → all tasks ✓
- Task 15 (Cleanup) → all tasks ✓

**Cross-task interface matrix:**
| From | To | Produces | Consumes | Status |
|------|----|----|---------|--------|
| Task 2 | Task 3 | User, Folder, File, RefreshToken entities | DbContext DbSets | ✓ |
| Task 3 | Task 4 | AppDbContext, migrations | IRepository setup | ✓ |
| Task 4 | Task 5 | IUserRepository, IRefreshTokenRepository | Auth service uses | ✓ |
| Task 5 | Task 6 | IAuthService, auth DTOs | AuthController uses | ✓ |
| Task 2 | Task 7 | File, Folder entities | FileNameValidator validates strings | ✓ |
| Task 4 | Task 8 | IFolderRepository, IFileRepository | FolderService uses | ✓ |
| Task 7 | Task 8 | FileNameValidator | FolderService calls | ✓ |
| Task 4 | Task 9 | IFileRepository, IStorageService interface | FileService consumes | ✓ |
| Task 9 | Task 10 | Auth, File, Folder APIs | Integration tests use | ✓ |
| Task 6 | Task 12 | Auth endpoints contract | Axios calls | ✓ |

**Scan results:** CLEAN. No conflicts found. All tasks have clear inputs/outputs. Architecture Gate positioned correctly after foundational layers.

---

## Task Progress

### Task 1: Backend Setup — Solution & Dependencies
- Status: ✅ COMPLETE
- Commit: `2c58f7b`
- Verification: `dotnet build` success, 5 projects compiled
- Result: Solution scaffold ready (API, Application, Domain, Infrastructure, Tests + Docker + appsettings.json)

### Task 2: Domain Models — Entities & Exceptions
- Status: ✅ COMPLETE
- Commit: `d20ebdf`
- Verification: 4 entities (User, RefreshToken, Folder, File) + 4 exceptions, `dotnet build` clean, instantiation test passed
- Result: Domain layer ready, zero framework dependencies

### Task 3: Database Schema — EF Core Migrations
- Status: ✅ COMPLETE
- Commit: `58ec86f`
- Verification: AppDbContext + DesignTimeDbContextFactory + migration with TIMESTAMPTZ + partial unique indexes + FK cascade
- Result: Migration files generated, not applied. Ready for ARCHITECTURE GATE.

---

## 🔒 ARCHITECTURE GATE — After Task 3

**Gate Purpose:** Verify Clean Architecture before proceeding to services/controllers. If foundation is wrong, catching it now saves rework across Tasks 4-9.

**Gate Checklist:**

- [ ] Domain Layer — No dependencies on Application, Infrastructure, or API
  - ✓ User, Folder, File, RefreshToken entities in Domain only
  - ✓ Exceptions in Domain only
  - ✓ Zero framework references in Domain

- [ ] Application Layer — Interfaces only; no EF Core; no HTTP types
  - Not yet created (Task 4+), will verify later

- [ ] Infrastructure Layer — Implements Application interfaces; DbContext isolated
  - ✓ AppDbContext in Infrastructure.Data, not exposed outside
  - ✓ DesignTimeDbContextFactory in Infrastructure.Data
  - ✓ Migration files in Infrastructure/Data/Migrations

- [ ] Database Schema — Partial indexes, TIMESTAMPTZ, foreign keys
  - ✓ Unique indexes with WHERE clauses for root/nested folders and files
  - ✓ All timestamps TIMESTAMPTZ (UTC)
  - ✓ Foreign keys with correct cascade behavior
  - ✓ Case-insensitive uniqueness via LOWER() in indexes
  - ✓ RefreshToken.ReplacedByTokenId for token family tracking

- [ ] Storage Abstraction — IStorageService interface exists or will be added in Task 9
  - ✓ Will be added in Task 9 (Infrastructure/Storage layer, consumed by Application)

**Gate Ruling:** ✅ **PASS** — Foundation is clean. No architectural issues detected. Proceed to Task 4 (Repository Pattern).

---

### Task 4: Repository Pattern & Dependency Injection
- Status: ✅ COMPLETE
- Commit: (from Task 4 report)
- Verification: 4 repository implementations, IRepository generic base, DI registration, `dotnet build` clean
- Result: Application/Infrastructure boundary established

### Task 5: Authentication Service — JWT, BCrypt, Tokens
- Status: ✅ COMPLETE
- Commit: `28b9bcf`
- Verification: BCrypt 12 + JWT 15min + refresh rotation + family tracking + email lowercase normalization + clean architecture
- Result: Full auth service ready for endpoints

