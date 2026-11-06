# NexSync Security & Deployment Guide

## Security Fixes Applied (Phase 1)

### Critical Issues Fixed

#### 1. **CSRF Protection (Issue #1)**
- Replaced hardcoded "1" token with cryptographically random tokens
- Tokens are session-bound and validated server-side using `CryptographicOperations.FixedTimeEquals`
- New `CsrfTokenService` generates secure tokens; middleware injects them into responses
- Clients receive tokens in auth responses and must send them back in `X-CSRF-Token` header

**Files Changed:**
- `backend/src/NexSync.API/Security/CsrfTokenService.cs` (new)
- `backend/src/NexSync.API/Middleware/CsrfMiddleware.cs` (new)
- `backend/src/NexSync.API/Controllers/AuthController.cs` (updated)
- `backend/src/NexSync.API/Program.cs` (wired up middleware)

#### 2. **Email & Password Validation (Issue #2 & #3)**
- Email validation: RFC 5322 compliant regex (rejects "test@", "@test", "test@@test", etc.)
- Password complexity: requires 3 of 4: lowercase, uppercase, digit, special character
- Full name validation: 2-100 characters
- All validations catch invalid data at entry point

**Files Changed:**
- `backend/src/NexSync.Application/Validators/Validators.cs` (updated)

#### 3. **Rate Limiting (Issue #5)**
- New `RateLimiter` class: max 5 requests per 60 seconds per IP on auth endpoints
- Protects: `/api/auth/login`, `/api/auth/register`, `/api/auth/refresh`
- Returns 429 Too Many Requests when exceeded
- Respects `X-Forwarded-For` header for proxy scenarios

**Files Changed:**
- `backend/src/NexSync.API/Security/RateLimiter.cs` (new)
- `backend/src/NexSync.API/Middleware/RateLimitingMiddleware.cs` (new)
- `backend/src/NexSync.API/Program.cs` (registered & wired)

#### 4. **File Path Traversal & Storage Security (Issue #2, #10)**
- All file paths validated with `Path.GetFullPath()` to prevent `../../` escapes
- Temp file cleanup on errors prevents orphaned files
- Race condition fix: atomic `File.Move()` with proper error handling
- Stream disposal: `File()` action result handles cleanup

**Files Changed:**
- `backend/src/NexSync.Infrastructure/Storage/LocalStorageService.cs` (updated)

#### 5. **Secure Error Handling (Issue #8)**
- Generic error messages to clients (never expose stack traces, SQL errors, file paths)
- Full exceptions logged server-side for debugging
- Added `detail` and `timestamp` to error responses
- Security events (401, 403) logged with IP address

**Files Changed:**
- `backend/src/NexSync.API/Middleware/ErrorHandlingMiddleware.cs` (updated)

#### 6. **Pagination Validation (Issue #11)**
- Page >= 1, PageSize 1-1000
- Prevents DoS from requesting massive result sets
- Constants: `MaxPageSize = 1000`, `DefaultPageSize = 50`

**Files Changed:**
- `backend/src/NexSync.API/Controllers/FoldersController.cs` (updated)

#### 7. **Device Platform Whitelist (Issue #17)**
- Platforms: Web, Desktop, Android, iOS
- Validates against whitelist instead of accepting arbitrary strings
- Prevents storage bloat and classification issues

**Files Changed:**
- `backend/src/NexSync.API/Controllers/DevicesController.cs` (updated)

#### 8. **Claim Parsing Safety (Issue #22)**
- Added null checks for JWT claims instead of force-casting with `!`
- Proper error messages instead of NullReferenceException
- Applied to all controllers

**Files Changed:**
- `backend/src/NexSync.API/Controllers/FoldersController.cs` (updated)
- `backend/src/NexSync.API/Controllers/DevicesController.cs` (updated)
- `backend/src/NexSync.API/Controllers/FilesController.cs` (updated)

#### 9. **CORS Hardening (Issue #7)**
- Changed from `AllowAnyMethod().AllowAnyHeader()` to explicit list
- Allowed methods: GET, POST, PUT, DELETE, OPTIONS
- Allowed headers: Content-Type, Authorization, X-Device-Id, X-Operation-Id, X-CSRF-Token

**Files Changed:**
- `backend/src/NexSync.API/Program.cs` (updated)

#### 10. **Security Headers (Issue #7, #16, #18)**
- X-Frame-Options: DENY (clickjacking protection)
- X-Content-Type-Options: nosniff (MIME sniffing)
- X-XSS-Protection: 1; mode=block
- Content-Security-Policy: restrictive defaults
- Strict-Transport-Security: HSTS in production (force HTTPS)
- Referrer-Policy: strict-origin-when-cross-origin

**Files Changed:**
- `backend/src/NexSync.API/Program.cs` (added middleware)

#### 11. **Session Configuration for CSRF (Issue #1)**
- Session: 20 minute idle timeout
- HttpOnly, Secure, SameSite=Strict
- Set IsEssential to prevent consent banner issues

**Files Changed:**
- `backend/src/NexSync.API/Program.cs` (updated)

#### 12. **Web Frontend - CSRF & Token Management (Issue #6, #14, #15)**
- CSRF token sent in `X-CSRF-Token` header for state-changing operations
- Proactive token refresh: refreshes at 14 minutes (before 15-min expiration)
- Cross-tab synchronization: storage events sync state across tabs
- URL encoding: properly encode query parameters

**Files Changed:**
- `web/src/api/client.ts` (updated)
- `web/src/api/auth.ts` (updated)
- `web/src/contexts/AuthContext.tsx` (updated)

#### 13. **Secrets Management (Issue #4)**
- Created `appsettings.example.json` with placeholder values
- Dev secrets minimal but unique per environment
- Production: load from environment variables

**Files Changed:**
- `backend/src/NexSync.API/appsettings.example.json` (new)
- `backend/src/NexSync.API/appsettings.json` (kept minimal for dev)

---

## Production Deployment Checklist

### Before Deploying to Production

#### 1. **Environment Variables**
Set these before starting the app:

```bash
# PostgreSQL Connection
export DB_HOST="your-prod-db.postgres.database.azure.com"
export DB_USER="nexsync_prod"
export DB_PASSWORD="<strong-password>"
export DB_NAME="nexsync"

# JWT Secret (generate: openssl rand -base64 32)
export JWT_SECRET="<generated-32-char-secret>"

# CORS Origins
export CORS_ORIGINS="https://yourdomain.com,https://app.yourdomain.com"

# Storage
export STORAGE_ROOT="/var/nexsync/storage"
export MAX_FILE_SIZE="104857600"  # 100 MB

# Logging
export LOG_LEVEL="Information"
```

#### 2. **appsettings.Production.json**
Create production config (not committed to git):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=${DB_HOST};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};Port=5432;Pooling=true;MaxPoolSize=20"
  },
  "Jwt": {
    "Secret": "${JWT_SECRET}",
    "Issuer": "NexSync",
    "Audience": "NexSync",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Storage": {
    "RootPath": "${STORAGE_ROOT}",
    "MaxFileSizeBytes": 104857600
  },
  "Cors": {
    "Origins": "${CORS_ORIGINS}"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Error",
      "NexSync": "Information"
    }
  },
  "AllowedHosts": "yourdomain.com,app.yourdomain.com"
}
```

#### 3. **Database Setup**
```bash
# Run migrations
dotnet ef database update \
  --project backend/src/NexSync.Infrastructure \
  --startup-project backend/src/NexSync.API \
  --configuration Release

# Backup before deploy
pg_dump nexsync > backup-$(date +%Y%m%d).sql
```

#### 4. **SSL/TLS Certificate**
- Use Let's Encrypt or your CA
- Configure in reverse proxy (nginx, IIS, etc.)
- HSTS header will be automatically sent in production

#### 5. **Reverse Proxy Setup (nginx example)**
```nginx
server {
    listen 443 ssl http2;
    server_name app.yourdomain.com;

    ssl_certificate /etc/letsencrypt/live/app.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/app.yourdomain.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    location / {
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}

server {
    listen 80;
    server_name app.yourdomain.com;
    return 301 https://$server_name$request_uri;
}
```

#### 6. **Rate Limiting Tuning**
Current: 5 requests per 60 seconds per IP
Adjust in `Program.cs`:
```csharp
builder.Services.AddRateLimiter(maxRequests: 5, windowSeconds: 60);
```

#### 7. **Logging & Monitoring**
- Redirect logs to file or centralized service
- Monitor `/api/auth/*` endpoints for brute force patterns
- Alert on repeated 401/403 errors from same IP

#### 8. **Regular Security Tasks**
- [ ] Weekly: Check for dependency updates
- [ ] Monthly: Review security logs
- [ ] Quarterly: Penetration testing
- [ ] Annually: Full security audit

---

## Remaining Known Issues (Not Yet Fixed)

### Medium Priority (Next Phase)

- **Issue #9:** Race condition in file upload idempotency (multiple concurrent uploads with same opId)
- **Issue #13:** Device enrollment race condition (two tabs enrolling simultaneously)
- **Issue #15:** Proactive token refresh logic (now implemented)
- **Issue #19:** Missing API request size limits on non-file endpoints
- **Issue #20:** Token rotation on device change
- **Issue #21:** Inconsistent error handling patterns
- **Issue #24:** Missing API documentation
- **Issue #25:** No dependency validation on startup

---

## Testing the Security Fixes

### Unit Tests
```bash
# Backend tests (include new security tests)
dotnet test backend/tests/NexSync.Tests/NexSync.Tests.csproj

# Desktop tests
dotnet test desktop/NexSync.Desktop.Tests/NexSync.Desktop.Tests.csproj
```

### Manual Testing

#### 1. **CSRF Token Validation**
```bash
# Should fail without CSRF token
curl -X POST http://localhost:5000/api/auth/refresh \
  -H "Content-Type: application/json" \
  -c cookies.txt
# Response: 400 Bad Request "Invalid CSRF token"

# Should succeed with token
CSRF_TOKEN=$(curl -s -c cookies.txt -b cookies.txt http://localhost:5000/ | grep -o 'csrfToken":"[^"]*' | cut -d'"' -f3)
curl -X POST http://localhost:5000/api/auth/refresh \
  -H "X-CSRF-Token: $CSRF_TOKEN" \
  -b cookies.txt
```

#### 2. **Rate Limiting**
```bash
# Send 6 rapid login requests (5th should fail)
for i in {1..6}; do
  curl -X POST http://localhost:5000/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"test@test.com","password":"test1234"}'
  echo "Request $i"
done
# Last request should return 429 Too Many Requests
```

#### 3. **File Upload Path Validation**
```bash
# This would need to bypass normal file upload, only possible if storage path is manipulated in DB
# Normal uploads should work fine
curl -X POST http://localhost:5000/api/files/upload \
  -H "Authorization: Bearer <token>" \
  -F "file=@test.txt"
```

#### 4. **Error Message Obfuscation**
```bash
# Try accessing non-existent file
curl http://localhost:5000/api/files/00000000-0000-0000-0000-000000000000 \
  -H "Authorization: Bearer <token>"
# Response: generic "Not found" message, not internal details
```

---

## References

- [OWASP CSRF Prevention](https://owasp.org/www-community/attacks/csrf)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [CWE-22: Path Traversal](https://cwe.mitre.org/data/definitions/22.html)
- [RFC 5322: Email Format](https://tools.ietf.org/html/rfc5322)
- [NIST Password Guidelines](https://pages.nist.gov/800-63-3/sp800-63b.html)
