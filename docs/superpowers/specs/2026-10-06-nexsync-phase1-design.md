# NexSync Phase 1 — Cloud Storage Foundation

**Date:** 2026-10-06
**Status:** Approved
**Scope:** Backend API + React Web Client

---

## 1. Overview

Phase 1 builds the foundation that all future phases depend on. It delivers a functional cloud storage system: users can register, log in, create folders, upload/download files, and manage their workspace through a web interface.

This phase intentionally excludes sync, offline, conflict resolution, versioning, chunked upload, and real-time features. Those are Phase 2+. The architecture is designed so those features slot in without restructuring.

## 2. Architecture

```
┌─────────────┐         ┌──────────────────────────────────┐
│  React Web  │────────→│       ASP.NET Core API            │
│  (Vite+TS)  │←────────│                                  │
└─────────────┘  HTTP   │  ┌────────────┐  ┌────────────┐  │
                        │  │ Application│→ │   Domain   │  │
                        │  └─────┬──────┘  └────────────┘  │
                        │        │                          │
                        │  ┌─────▼──────────────────────┐  │
                        │  │     Infrastructure          │  │
                        │  │  ┌──────────┐ ┌──────────┐ │  │
                        │  │  │ EF Core  │ │ Storage  │ │  │
                        │  │  │(Postgres)│ │(LocalDisk)│ │  │
                        │  │  └──────────┘ └──────────┘ │  │
                        │  └────────────────────────────┘  │
                        └──────────────────────────────────┘
```

### Clean Architecture Layers

| Layer | Project | Responsibility |
|-------|---------|----------------|
| API | `NexSync.API` | Controllers, middleware, auth config, DI setup, OpenAPI/Swagger |
| Application | `NexSync.Application` | Use cases (services), DTOs, interfaces, validators |
| Domain | `NexSync.Domain` | Entities, enums, exceptions |
| Infrastructure | `NexSync.Infrastructure` | EF Core DbContext, repositories, file storage, auth helpers |

Dependencies flow inward: API → Application → Domain. Infrastructure implements Application interfaces.

## 3. Authentication

### Flow

```
Register → hash password (BCrypt) → store user → return JWT + set refresh cookie
Login    → verify password → return JWT + set refresh cookie
Refresh  → validate refresh cookie → rotate → return new JWT + new refresh cookie
Logout   → revoke refresh token + clear cookie
```

### JWT Access Token

- 15 minute expiry
- Contains user ID + email as claims
- Stored in memory only on the client (not localStorage)
- Sent via `Authorization: Bearer` header
- All file/folder endpoints require valid JWT

### Refresh Token

- 7 day expiry, stored in database as SHA-256 hash (not plaintext)
- Delivered via `Set-Cookie: Secure; HttpOnly; SameSite=Strict; Path=/api/auth`
- Single-use rotation: using a refresh token revokes it and issues a new one
- Token family tracking: each token records `ReplacedByTokenId` pointing to its successor
- If a revoked token is reused, revoke the entire token family for that user (compromise detection)
- CSRF protection: refresh and logout endpoints require custom header `X-CSRF: 1` (SameSite=Strict + custom header)

### Password

- BCrypt with work factor 12
- Minimum 8 characters, maximum 72 characters (BCrypt input limit)
- Email normalized to lowercase before storage and lookup

## 4. Database Schema (PostgreSQL)

All timestamps use `TIMESTAMPTZ` (UTC). `UpdatedAt` is set by EF Core on every save, not by database default.

### Users

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK, default gen_random_uuid() |
| Email | VARCHAR(255) | UNIQUE, NOT NULL |
| PasswordHash | VARCHAR(255) | NOT NULL |
| FullName | VARCHAR(100) | NOT NULL |
| CreatedAt | TIMESTAMPTZ | NOT NULL, default now() |
| UpdatedAt | TIMESTAMPTZ | NOT NULL |

### RefreshTokens

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK |
| UserId | UUID | FK → Users, NOT NULL |
| TokenHash | VARCHAR(64) | UNIQUE, NOT NULL (SHA-256 of token) |
| ExpiresAt | TIMESTAMPTZ | NOT NULL |
| RevokedAt | TIMESTAMPTZ | NULLABLE (null = active, set = revoked) |
| ReplacedByTokenId | UUID | FK → RefreshTokens, NULLABLE |
| CreatedAt | TIMESTAMPTZ | NOT NULL, default now() |

A token is active when `RevokedAt IS NULL AND ExpiresAt > now()`. On rotation, the old token gets `RevokedAt = now()` and `ReplacedByTokenId` pointing to the new token. If a revoked token is reused, walk the `ReplacedByTokenId` chain and revoke the entire family.

### Folders

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK |
| Name | VARCHAR(255) | NOT NULL |
| ParentFolderId | UUID | FK → Folders, NULLABLE (null = root) |
| OwnerId | UUID | FK → Users, NOT NULL |
| CreatedAt | TIMESTAMPTZ | NOT NULL, default now() |
| UpdatedAt | TIMESTAMPTZ | NOT NULL |

**Unique constraints** (two partial indexes to handle NULL correctly):

```sql
CREATE UNIQUE INDEX ix_folders_root_unique
  ON folders (owner_id, LOWER(name))
  WHERE parent_folder_id IS NULL;

CREATE UNIQUE INDEX ix_folders_nested_unique
  ON folders (owner_id, parent_folder_id, LOWER(name))
  WHERE parent_folder_id IS NOT NULL;
```

This ensures case-insensitive uniqueness and handles root-level folders correctly (PostgreSQL treats NULLs as distinct in regular unique constraints).

### Files

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK |
| Name | VARCHAR(255) | NOT NULL |
| FolderId | UUID | FK → Folders, NULLABLE (null = root) |
| OwnerId | UUID | FK → Users, NOT NULL |
| Hash | VARCHAR(64) | NOT NULL (SHA-256 hex) |
| Size | BIGINT | NOT NULL |
| ContentType | VARCHAR(100) | NOT NULL |
| StoragePath | VARCHAR(500) | NOT NULL |
| CreatedAt | TIMESTAMPTZ | NOT NULL, default now() |
| UpdatedAt | TIMESTAMPTZ | NOT NULL |

**Unique constraints** (same pattern as folders):

```sql
CREATE UNIQUE INDEX ix_files_root_unique
  ON files (owner_id, LOWER(name))
  WHERE folder_id IS NULL;

CREATE UNIQUE INDEX ix_files_nested_unique
  ON files (owner_id, folder_id, LOWER(name))
  WHERE folder_id IS NOT NULL;
```

## 5. File Storage (Local Disk)

Files stored at: `{StorageRoot}/{OwnerId}/{SHA256Hash}`

```
storage/
└── a1b2c3d4-.../
    ├── e5f6a7b8...sha256hex
    └── 1234abcd...sha256hex
```

Using hash as filename sets up deduplication for Phase 7. Multiple File records can point to the same storage path if content is identical.

`StorageRoot` is configurable via `appsettings.json`.

### Storage Abstraction

```
IStorageService
├── SaveFileAsync(ownerId, stream) → (storagePath, hash, size)
├── GetFileAsync(storagePath) → stream
├── DeleteFileAsync(storagePath)
└── FileExistsAsync(storagePath) → bool
```

Phase 1: `LocalStorageService`. Phase 7+: `MinioStorageService`.

### Upload Pipeline (Streaming)

```
multipart/form-data
       ↓
stream to temporary file (not memory)
       ↓
compute SHA-256 while streaming
       ↓
move to final storage path
       ↓
insert database record
       ↓
on DB failure → delete stored file (compensation)
on storage failure → no DB record created
```

The backend MUST NOT buffer the entire file in memory. ASP.NET Core streaming upload is used to handle large files (up to 100MB in Phase 1) without excessive memory usage.

### Concurrent Identical Upload Handling

When two uploads produce the same hash for the same user:

```
Request A → temp file → hash ABC123 → final path exists? NO → move to final
Request B → temp file → hash ABC123 → final path exists? YES → discard temp, reuse existing
```

If the final storage path already exists, the temporary file is discarded and the existing storage object is reused. Each request still creates its own File metadata record. This is safe because the storage path is content-addressed (hash-based) — identical hash guarantees identical content.

### Upload Failure Compensation

```
1. Stream to temporary file
2. Compute SHA-256
3. Check if final path exists → if yes, discard temp (reuse)
4. If no, move temp to final path
5. BEGIN DB transaction
6. Insert File record
7. COMMIT
```

On failure at step 6-7:
- If the physical file was newly created (not reused) and no other File records reference it → delete the physical file
- If the physical file was reused (already existed) → leave it alone
- Temporary file is always cleaned up regardless of outcome

### Deletion Safety

When deleting a file record:

```
Delete File Record
       ↓
Check: Does another Files row reference the same StoragePath?
       ↓
YES → keep physical file
NO  → delete physical file
```

## 6. API Endpoints

### Auth

| Method | Path | Request | Response |
|--------|------|---------|----------|
| POST | `/api/auth/register` | Body: `{ email, password, fullName }` | `{ accessToken, user }` + Set-Cookie (refresh) |
| POST | `/api/auth/login` | Body: `{ email, password }` | `{ accessToken, user }` + Set-Cookie (refresh) |
| POST | `/api/auth/refresh` | Cookie (refresh) + Header `X-CSRF: 1` | `{ accessToken }` + Set-Cookie (new refresh) |
| POST | `/api/auth/logout` | Cookie (refresh) + Header `X-CSRF: 1` | 204 + Clear-Cookie |

### Folders

All require auth. All operations verify `folder.OwnerId == currentUserId`.

| Method | Path | Request | Response |
|--------|------|---------|----------|
| GET | `/api/folders` | Query: `?parentId={id}` (omit for root), `?page=1&pageSize=50` | Paginated `{ folders, files, page, pageSize, totalCount }` |
| GET | `/api/folders/{id}` | — | Folder detail |
| POST | `/api/folders` | Body: `{ name, parentFolderId? }` | 201 Created folder |
| PUT | `/api/folders/{id}` | Body: `{ name, parentFolderId }` | Updated folder |

`PUT` is a full update. Both `name` and `parentFolderId` are always required. `parentFolderId: null` explicitly means root. This avoids ambiguity between "omitted" and "null" after deserialization.
| DELETE | `/api/folders/{id}` | — | 204 |

### Files

All require auth. All operations verify `file.OwnerId == currentUserId`.

| Method | Path | Request | Response |
|--------|------|---------|----------|
| POST | `/api/files/upload` | Multipart: file + folderId? | 201 Created file metadata |
| GET | `/api/files/{id}` | — | File metadata |
| GET | `/api/files/{id}/download` | — | File stream |
| PUT | `/api/files/{id}` | Body: `{ name, folderId }` | Updated file |

`PUT` is a full update. Both `name` and `folderId` are always required. `folderId: null` explicitly means root.
| DELETE | `/api/files/{id}` | — | 204 |

### Pagination

Default page size: 50. Max page size: 100.

Ordering: folders first (sorted by `name ASC`, case-insensitive), then files (sorted by `name ASC`, case-insensitive). Pagination is applied to the combined logical list.

Example with 3 folders + 5 files and `pageSize=4`:
- Page 1: Folder A, Folder B, Folder C, File alpha.txt
- Page 2: File beta.pdf, File gamma.zip, File hello.docx, File readme.md

```json
{
  "folders": [...],
  "files": [...],
  "page": 1,
  "pageSize": 50,
  "totalCount": 120,
  "totalPages": 3
}
```

### Error Responses (RFC 9457 Problem Details)

```json
{
  "type": "https://nexsync.dev/errors/folder-not-found",
  "title": "Folder not found",
  "status": 404,
  "detail": "Folder with the specified id was not found.",
  "code": "FOLDER_NOT_FOUND",
  "traceId": "00-abc123..."
}
```

HTTP status codes: 400 (validation), 401 (unauth), 403 (forbidden), 404 (not found), 409 (conflict/duplicate name), 413 (file too large), 500 (server error).

### Duplicate Name Handling

When uploading a file or creating a folder with a name that already exists in the same location:

```
409 Conflict
code: FILE_NAME_CONFLICT / FOLDER_NAME_CONFLICT
```

Same for rename operations. No silent overwrite, no auto-numbering.

## 7. Business Rules

### Ownership Validation

Every operation MUST verify ownership:

- `folder.OwnerId == currentUserId`
- `file.OwnerId == currentUserId`
- When moving/creating into a parent folder: `parentFolder.OwnerId == currentUserId`
- When moving a file to a folder: `targetFolder.OwnerId == currentUserId`

FK constraints ensure referential integrity but do NOT enforce authorization. Authorization is always checked at the application layer.

### Folder Cycle Prevention

When moving a folder (changing `parentFolderId`):

```
Cannot move folder to itself
Cannot move folder to any of its descendants
```

Implementation: walk up from target parent to root. If the source folder ID is encountered, reject with 400.

### Recursive Folder Delete

```
Folder
  ↓ collect all descendant folders (recursive)
  ↓ collect all files in folder + descendants
  ↓ delete physical files (with dedup check)
  ↓ delete file records
  ↓ delete folder records (children first)
```

Database cascade handles metadata. Physical storage cleanup is done by the application service before the cascade.

### Case-Insensitive Names

Folder and file names are stored as-is (preserving case) but uniqueness is checked case-insensitively via `LOWER()` indexes.

```
"Projects" and "projects" → conflict
"Projects" stored as "Projects" (original case preserved)
```

### File and Folder Name Validation

Names must be safe for all target platforms (Windows, Android, Web). Validated at the application layer on create, upload, and rename.

**Forbidden characters:**

```
\ / : * ? " < > |
```

**Additional rules:**

- No trailing spaces
- No trailing dots
- No leading/trailing whitespace (trimmed)
- Maximum 255 characters
- Cannot be empty or whitespace-only
- Cannot be Windows reserved names: `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9` (case-insensitive, with or without extension)

This prevents file creation failures when the desktop client (Phase 3) syncs to Windows.

## 8. Application Services

### AuthService
- `Register(email, password, fullName)` → validate, hash password, create user, generate tokens, set cookie
- `Login(email, password)` → verify, generate tokens, set cookie
- `RefreshToken(tokenFromCookie)` → validate hash, rotate, return new pair
- `Logout(tokenFromCookie)` → revoke token, clear cookie

### FolderService
- `GetContents(userId, parentFolderId?, page, pageSize)` → paginated list of folders + files
- `GetById(userId, folderId)` → single folder (with ownership check)
- `Create(userId, name, parentFolderId?)` → validate parent ownership, check name conflict, create
- `Update(userId, folderId, name, parentFolderId)` → validate ownership, check cycles, check name conflict, update (full update: both fields required)
- `Delete(userId, folderId)` → validate ownership, recursive delete (storage + metadata)

### FileService
- `Upload(userId, fileStream, fileName, contentType, folderId?)` → validate folder ownership, stream to temp, hash, store, insert record, cleanup on failure
- `GetById(userId, fileId)` → metadata (with ownership check)
- `Download(userId, fileId)` → ownership check, return file stream
- `Update(userId, fileId, name, folderId)` → validate ownership, check target folder ownership, check name conflict, update (full update: both fields required, null folderId = root)
- `Delete(userId, fileId)` → validate ownership, delete record, delete physical if no other references

## 9. React Web Client

### Tech Stack
- React 19 + TypeScript
- Vite 8
- Axios (HTTP client)
- React Router 8
- Tailwind CSS 4

### Pages

| Route | Page | Description |
|-------|------|-------------|
| `/login` | Login | Email + password form |
| `/register` | Register | Email + password + name form |
| `/` | Dashboard/Files | File browser — main page |

### File Browser Features
- Breadcrumb navigation (Root > Projects > Form4x)
- Grid/List view toggle
- Folder icons, file icons (by type)
- Click folder to navigate into it
- Click file to see details / download
- Upload button (drag & drop on folder area)
- Right-click context menu: Rename, Move, Delete, Download
- New Folder button
- Upload progress indicator
- Empty state when no files
- Pagination controls

### Auth Flow
- Access token: stored in memory only (variable, not localStorage)
- Refresh token: HttpOnly cookie (managed by browser, sent automatically)
- Axios: `withCredentials: true` globally (required for cookie transmission with CORS)
- Axios interceptor: on 401, call `/api/auth/refresh` (with `X-CSRF: 1` header), retry original request
- Redirect to `/login` when refresh also fails
- On page load: call refresh endpoint to restore session

### State Management
- React Context for auth state
- Local state (`useState`/`useReducer`) for file browser — no Redux needed in Phase 1

## 10. Project Structure

```
NexSync/
├── backend/
│   ├── NexSync.sln
│   ├── src/
│   │   ├── NexSync.API/
│   │   │   ├── Controllers/
│   │   │   │   ├── AuthController.cs
│   │   │   │   ├── FoldersController.cs
│   │   │   │   └── FilesController.cs
│   │   │   ├── Middleware/
│   │   │   │   └── ErrorHandlingMiddleware.cs
│   │   │   ├── Extensions/
│   │   │   │   └── ServiceCollectionExtensions.cs
│   │   │   ├── Program.cs
│   │   │   └── appsettings.json
│   │   ├── NexSync.Application/
│   │   │   ├── DTOs/
│   │   │   ├── Interfaces/
│   │   │   ├── Services/
│   │   │   └── Validators/
│   │   ├── NexSync.Domain/
│   │   │   ├── Entities/
│   │   │   └── Exceptions/
│   │   └── NexSync.Infrastructure/
│   │       ├── Data/
│   │       │   ├── AppDbContext.cs
│   │       │   └── Migrations/
│   │       ├── Repositories/
│   │       ├── Storage/
│   │       └── Authentication/
│   └── tests/
│       └── NexSync.Tests/
│
├── web/
│   ├── package.json
│   ├── src/
│   │   ├── api/
│   │   ├── components/
│   │   ├── contexts/
│   │   ├── pages/
│   │   ├── types/
│   │   ├── App.tsx
│   │   └── main.tsx
│   └── index.html
│
├── docs/
│   └── superpowers/
│       └── specs/
│
├── storage/          (gitignored, local file storage)
└── docker/
    └── docker-compose.yml   (PostgreSQL)
```

## 11. Docker Compose (Development)

```yaml
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

## 12. Configuration

`appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=nexsync;Username=nexsync;Password=nexsync_dev"
  },
  "Jwt": {
    "Secret": "...(min 32 chars, use user-secrets in dev)...",
    "Issuer": "NexSync",
    "Audience": "NexSync",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Storage": {
    "RootPath": "./storage",
    "MaxFileSizeBytes": 104857600
  }
}
```

## 13. API Documentation

- OpenAPI document generated by `Microsoft.AspNetCore.OpenApi` (built-in .NET 10)
- Swagger UI enabled in Development environment only
- Endpoint: `/swagger`

## 14. Security Considerations

- All endpoints over HTTPS in production
- Passwords hashed with BCrypt (work factor 12, max 72 bytes input validated)
- JWT access token: short expiry (15 min), memory-only on client
- Refresh token: HttpOnly + Secure + SameSite=Strict cookie, stored as SHA-256 hash in DB
- Refresh token rotation with family tracking (`ReplacedByTokenId`) and compromise detection (reuse → revoke entire token family)
- CSRF protection on refresh and logout endpoints via custom header `X-CSRF: 1`
- All file/folder operations verify ownership at application layer
- File upload: streaming (not buffered), size limit configurable (default 100MB)
- Input validation on all endpoints
- CORS configured for web client origin only (specific origin, not wildcard, because `AllowCredentials` is required for cookie-based auth)
- Axios configured with `withCredentials: true` globally for cookie transmission
- No secrets in source code — user-secrets in development, env vars in production
- ProblemDetails error responses (no stack traces in production)

## 15. Testing Strategy

### Unit Tests
- `AuthService` — register, login, refresh, logout, password validation, token rotation
- `FolderService` — create, rename, move, delete, cycle detection, name conflict
- `FileService` — upload, download, rename, move, delete, dedup check on delete
- Validators — email, password length, file name

### Integration Tests
- Full auth flow (register → login → refresh → logout)
- Folder CRUD via API
- File upload/download via API
- Recursive folder delete
- Folder move with cycle detection
- Pagination

### Authorization Tests
- User A cannot access User B's folders
- User A cannot access User B's files
- User A cannot move files/folders into User B's folders
- User A cannot download User B's files by guessing ID
- Expired JWT rejected
- Revoked refresh token rejected
- Reused refresh token triggers full revocation

### Storage Tests
- File stored at correct path
- Hash computed correctly
- Orphan cleanup on DB failure
- Physical file preserved when other records reference same hash

## 16. Future-Proofing Decisions

| Decision | Supports |
|----------|----------|
| SHA-256 hash on every file | Phase 7: deduplication |
| Hash-based storage paths | Phase 7: dedup, no re-upload for identical files |
| UUID primary keys | Phase 3: multi-device, distributed IDs |
| Clean Architecture layers | Phase 2+: sync engine, workers slot into Infrastructure |
| Separate metadata from storage (`IStorageService`) | Phase 7: MinIO/S3 migration |
| User ownership on all entities | Phase 3: multi-device, permissions |
| TIMESTAMPTZ + UTC everywhere | Phase 2+: multi-device sync across timezones |
| Paginated API from day one | Scales without API contract changes |

## 17. Success Criteria

Phase 1 is complete when:

1. User can register and log in via web UI
2. User can create, rename, delete folders (nested)
3. User can upload files to any folder (or root)
4. User can download files
5. User can rename and move files between folders
6. User can delete files (with storage cleanup)
7. File browser shows breadcrumb navigation
8. Upload shows progress
9. Auth persists across page refresh (refresh cookie)
10. PostgreSQL stores all metadata with TIMESTAMPTZ
11. Files stored on local disk organized by hash
12. API returns ProblemDetails error responses
13. Duplicate names return 409 Conflict
14. Folder cycle prevention works
15. Cross-user access denied
16. Pagination works on folder contents
17. Swagger UI available in development
18. Unit + integration + authorization tests pass
