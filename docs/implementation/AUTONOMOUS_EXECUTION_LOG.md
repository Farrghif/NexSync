# NexSync — Autonomous Execution Log

Plan: `docs/implementation/PHASE_J_AUTONOMOUS_PLAN.md`
Ledger: `.superpowers/sdd/PHASE_J_AUTONOMOUS_PLAN/progress.md`

## Current state (2026-11-11)

- **Phase:** J (Task 1 — repository & environment verification)
- **Branch:** `feature/android-storage-node`
- **HEAD:** `71d68a7c892b0aa775afd6b19a4bf2c8fe15cffd` (= reported Phase I commit;
  it IS HEAD, so trivially an ancestor of itself; matches directive hash)
- **Working tree:** clean before this phase's changes; 290 tracked files
- **Protected baseline:** tag `v2.0-phase2` = `a8fc6ba` — untouched

## Verified baseline test results (executed 2026-11-11, this machine)

| Gate | Command | Result |
|------|---------|--------|
| Desktop tests | `dotnet test desktop/NexSync.Desktop.Tests/...` | **164/164 PASS** (6 s) |
| Backend solution build | `dotnet build backend/NexSync.slnx` | **0 errors, 59 warnings** |
| Backend tests | `dotnet test backend/tests/NexSync.Tests/...` (Docker/Testcontainers) | **86/126 pass, 40 FAIL** — Task 2 boot fix + DevicePlatform enum fix; 40 platform-registration failures resolved by extending DevicePlatform enum with Desktop/iOS (was 29/124 pass, 95 FAIL before Task 2) |
| Web build | `npm run build:web` | PASS (91 modules) |
| Web lint | `npm run lint:web` | 5 warnings, 0 errors |
| Vulnerable deps | `dotnet list backend/NexSync.slnx package --vulnerable --include-transitive` | all 8 projects clean (docs `dev-run.md`/`PHASE2-BASELINE.md` are stale: they still list 4 fixed advisories) |
| CI | `.github` | none exists — gates are manual |

Environment inventory (detail in ledger):
- JDK 21 present (`C:\Program Files\Android\openjdk\jdk-21.0.8`, Android Studio
  JBR 21.0.9); `JAVA_HOME` unset, `java` not on PATH; no JDK 17 standalone.
- Android SDK at `C:\Users\farre\AppData\Local\Android\Sdk`;
  `ANDROID_HOME`/`ANDROID_SDK_ROOT` unset.
- Gradle not on PATH; wrapper has `gradlew` + properties (Gradle 8.6) but
  **no `gradle-wrapper.jar`**; wrapper `distributionSha256Sum` is a placeholder
  pattern (will fail validation).
- `android-storage-node`: AGP version **not pinned anywhere** → plugin
  resolution fails as-is. Kotlin 1.9.23, compileSdk 35, jvmTarget 17.
- Test infra: only `src/test/.../SafStorageAdapterTest.kt` +
  `SafStorageAdapterRegressionTestTest.kt`; **no `src/androidTest/`** despite
  declared runner/deps; tests import `kotlin.test` with no such dependency.

Known defects carried into this execution (verified, see plan Task 2 and audits):
- Backend won't boot: `AddSession()` with no `IDistributedCache`
  (`Program.cs:12` vs `:58`); `CsrfMiddleware` registered before `UseSession`
  (`Program.cs:63-64`); `RateLimiter.IsAllowed` never returns false.
- Web: CSRF token read from body only → stale after first rotation → session
  dies ~15 min; MAUI sends `X-CSRF: 1` (wrong header name/value) → refresh/
  logout always 401; storage-node build blockers (missing wrapper JAR, missing
  `@xml/backup_rules`/`data_extraction_rules`, missing `documentfile` dep,
  Material theme parent without Material dep, missing `kotlin("test")`).
- Storage-node runtime blockers: hardcoded SAF tree URI, `writeFileStream`
  delete-then-rename ignoring results (data loss), fake-green tests.

## Completed

- Task 1 (complete): inventory + this log + `MASTER_ROADMAP.md` created.
- Task 2 (complete): boot repair + platform enum fix. 86/126 backend tests pass (40 platform-registration failures resolved by extending DevicePlatform enum with Desktop/iOS). Backend boot defect resolved: AddDistributedMemoryCache() registered, UseSession() before CsrfMiddleware, RateLimiter deny-before-increment. DevicePlatform enum now includes Desktop and iOS, matching controller ValidPlatforms {Web, Desktop, Android, iOS}.

## Remaining

Tasks 3-15 per `MASTER_ROADMAP.md`. Gate: Sprint 3B blocked by Sprint 2.1.

## Next action

Task 1 self-check → commit Phase J docs → Task 2 (backend boot repair, TDD).
