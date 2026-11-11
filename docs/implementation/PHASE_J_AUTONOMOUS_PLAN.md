# NexSync — Phase J Onward Autonomous Execution Plan

Source of authority: the project owner's directive "NEXSYNC — PHASE J ONWARD"
(conversation, 2026-11-11). This plan restates that directive as executable
tasks. Where the two differ, the directive wins; record a `Ruling:` line.

Protected baseline: tag `v2.0-phase2` = `a8fc6ba40129aa8f2be9f5c78f696843260f68c8`.
Never rewrite, amend, delete, reset, or force-push over it.
Preserve user-authored docs: `ANALISIS_LENGKAP_NEXSYNC.md`,
`SPRINT_2_1_FINAL_EVIDENCE.md`.
Primary branch: `feature/android-storage-node` (current, HEAD `71d68a7`).

Critical quality gate: Sprint 3B (Task 6) cannot begin until Sprint 2.1
(Tasks 3-5) passes its acceptance criteria.

# Task 1: Phase J — repository and environment verification

Inspect and record, with real command output:
- Actual branch, HEAD, working tree, recent commits.
- Whether `71d68a7` is an existing ancestor of HEAD (it IS the HEAD; record it).
- Java installation(s), JAVA_HOME, java version.
- Android SDK path, ANDROID_HOME / ANDROID_SDK_ROOT.
- Gradle wrapper presence (gradlew, gradlew.bat, gradle-wrapper.jar, properties).
- AGP/Kotlin/SDK versions declared in build files; compatibility with installed JDK.
- Existing test infrastructure (unit + androidTest sources, test deps).
- Backend API + database architecture (note: backend currently fails to boot —
  see Task 2), Desktop and Web API integration patterns (read-only survey).

Deliverables created in this task:
- `docs/implementation/AUTONOMOUS_EXECUTION_LOG.md` — current phase, completed
  work, remaining tasks, actual HEAD, build/test results, next action.
- `docs/implementation/MASTER_ROADMAP.md` — all phases J..T with status.

Environment decisions to record in the log:
- Which JDK is used (prefer existing compatible JDK 21; do NOT commit JDKs,
  SDKs, or machine-specific paths).
- JAVA_HOME / ANDROID_HOME / PATH setup for this session (shell-level only).
- Whether the Gradle wrapper can bootstrap (wrapper JAR missing = known issue).

Verification commands (record exact command + exit code in the log):
- `git branch --show-current`, `git rev-parse HEAD`, `git status --porcelain`
- `java -version` via the chosen JDK
- `dir`/`ls` of gradle wrapper files
- Read `android-storage-node/build.gradle.kts`, `settings.gradle.kts`,
  `gradle/wrapper/gradle-wrapper.properties`, `gradle.properties`, `local.properties`

Expected: a written inventory; NO build claimed green yet.

# Task 2: Backend boot repair (verified defect blocking all later verification)

Verified by direct execution on 2026-11-11:
`dotnet run --project backend/src/NexSync.API` crashes at `Program.cs:58`
(`builder.Build()`):
`Unable to resolve service for type 'IDistributedCache' while attempting to
activate 'DistributedSessionStore'`.
Cause: `Program.cs:12` calls `builder.Services.AddSession(...)` but nothing
registers an `IDistributedCache` (grep for `AddDistributedMemoryCache` /
`IDistributedCache` across backend = 0 matches).
Effect: API cannot start in ANY environment; 95/124 backend integration tests
fail in fixture setup (only 29 pure unit tests pass).

Second defect, found by code reading (will surface immediately after the first
is fixed): `CsrfMiddleware` is registered BEFORE `app.UseSession()`
(`Program.cs:63-64`), so `context.Session.LoadAsync()` runs before
`SessionMiddleware` sets `ISessionFeature` → null `Session` → NRE → 500 on
every request.

Third defect (security): `Security/RateLimiter.cs` `IsAllowed` returns
`bucket.Count <= _maxRequests`, and the update lambda caps `Count` at
`_maxRequests`, so the expression is ALWAYS true → brute-force protection on
`/api/auth/login|register|refresh` never blocks a single request.

TDD required for this task:
- RED: add/extend a WebApplicationFactory-based test that asserts the host
  starts and a trivial request returns non-500 (and, for the rate limiter, that
  request N+1 beyond the window limit returns 429). Watch it fail first.
- GREEN: minimal fix — register `AddDistributedMemoryCache()` (or
  `AddDistributedMemoryCaching()`), move `app.UseSession()` above
  `CsrfMiddleware`, and correct `IsAllowed` to deny once the window count has
  reached the limit (check BEFORE incrementing).
- Re-run the full backend suite: target 124/124 pass.

Do NOT change the frozen `v2.0-phase2` tag or historical test evidence docs.

# Task 3: Phase K / Sprint 2.1 — SAF onboarding (dynamic root URI)

Remove the hardcoded production root URI in
`android-storage-node/.../di/HiltModules.kt` (~line 144):
`content://com.android.providers.media.documents/tree/primary%3A`.

Requirements:
1. Folder selection UI using `Intent.ACTION_OPEN_DOCUMENT_TREE` (SAF picker).
2. Obtain the actual selected tree URI.
3. Persist read/write URI permissions via
   `ContentResolver.takePersistableUriPermission`.
4. Persist the selected URI (DataStore/SharedPreferences — note DataStore is
   currently NOT a dependency; either add it or use SharedPreferences).
5. Restore access after app restart.
6. Support changing the selected root folder.
7. Detect revoked/invalid permissions (compare persisted grants against
   `getPersistedUriPermissions`).
8. Return the app to onboarding when storage is not configured.
9. Display understandable permission/initialization errors.
10. No arbitrary `runBlocking` inside Hilt providers; app must start normally
    when storage is not yet configured.

Do NOT use `runBlocking` in Hilt providers to load DataStore values.

TDD: unit tests for the repository/state logic (configured vs not-configured,
grant present vs revoked, restore-on-restart) using a fake `ContentResolver` /
fake prefs; instrumentation tests where provider behavior matters.

Verify flow across: first install, first folder selection, app restart,
permission revocation, folder change, invalid/inaccessible provider.

# Task 4: Phase K / Sprint 2.1 — SAF document handling + write redesign

Investigate every method in
`android-storage-node/.../data/storage/SafStorageAdapter.kt`.

Correct `fromSingleUri()` parent assumption: a `SingleDocumentFile` has NO
parent. Use the selected tree URI + a tree-based document representation to
create new child documents. Treat each returned document URI as a real
provider-issued identity.

Correctly handle: file create, dir create, read, write, rename, move, copy,
delete, listing, metadata, MIME type, URI change after rename, provider
exceptions, Boolean failure results, missing permissions, invalid documents.
Every relevant Boolean SAF result MUST be checked explicitly; never silently
continue after failure.

Redesign `writeFileStream()` (currently reachable failure + latent defects):
- Reachable: `fromSingleUri()` has no parent → invalid temp path → error
  before writing. Fix the root cause, not a workaround.
- Latent: ignored `delete()` / `renameTo()` results, potential false success,
  potential loss of the old file, nullable output stream, missing byte-count
  validation, missing expected-hash comparison, missing persisted-content
  verification, provider-issued URI changes.

Adopt **Immutable Versioned Objects + Recoverable Commit Protocol**:
Operation lifecycle PREPARING → WRITING → WRITTEN → VERIFIED → ACTIVE →
GC_PENDING, plus FAILED_NEEDS_RECONCILIATION.
Requirements: durable Room operation record; unique version id; create new SAF
document without touching the active version; persist provider URI; stream
content; treat null output stream as failure; close/flush; track bytes;
compare bytes to expected size; compute SHA-256; compare to expected hash;
distinguish HASH_CALCULATED vs HASH_VERIFIED; read back where feasible;
promote active version in a Room transaction; retain previous version until
promotion + recovery checks complete; GC abandoned objects only after proving
they are not referenced by an active version.
A Room transaction is NOT a SAF ContentProvider transaction — implement a
recoverable state machine, never claim global atomicity.

Crash reconciliation at every boundary (before/after SAF doc creation, during
write, after write, during hash verify, after verify, before metadata
promotion, after active-pointer promotion, during old-version cleanup).
Recovery must be idempotent; NEVER delete a valid active version due to an
uncertain recovery state. Orphan discovery by unique operation id + an
app-managed SAF directory. If Room metadata is damaged/unavailable, NEVER
auto-delete all managed SAF objects.

Filename/storage UX decision (MVP): reliability > conventional physical
filenames. Logical filenames via metadata; immutable physical version objects
in an app-managed SAF subtree. Document: what the user sees in-app, what they
see in an external file manager, how metadata maps logical→physical, how
recovery works when metadata is incomplete, limitations when another app
manipulates managed objects. Do NOT pretend every SAF provider supports atomic
conventional-name replacement.

# Task 5: Phase K / Sprint 2.1 — SAF test coverage + acceptance gate

Implement measurable tests for: empty files; small files; large files; size
boundaries; expected SHA-256 match; expected SHA-256 mismatch; missing
expected hash; incorrect byte count; null output stream;
FileNotFoundException; provider exception; permission revoked; create failure;
delete returning false; rename returning false; URI changes; interrupted
transfer; app restart; recovery of incomplete writes; duplicate operations;
safe GC; preservation of the previous active version.
Use unit tests, failure-injection tests, and Android instrumentation tests.
Where provider behavior matters, test against an actual or purpose-built
Android DocumentsProvider test implementation — a mock alone does not verify
real provider behavior.

Create `android-storage-node/docs/testing/SAF_STORAGE_VERIFICATION.md` with an
exact evidence matrix (test name → command → result → report path).

Sprint 2.1 acceptance gate (all must hold, with executed evidence — NOT
historical counts or source inspection):
- Root URI configuration is dynamic.
- Persisted permission works.
- The write path completes successfully.
- Previous version remains recoverable after a failed write.
- Hash validation is real.
- Byte-count validation works.
- Crash recovery works for tested interruption points.
- No known false-success path remains.
- Gradle build succeeds.
- Executed tests have actual reports.
- Documentation reflects verified behavior.

After all criteria pass, proceed directly to Task 6 (Sprint 3B).

# Task 6: Phase L / Sprint 3B — Ktor server and lifecycle

Implement the actual Android HTTP server (Ktor CIO if compatible after
inspecting current deps/structure). A declared dependency is NOT an
implemented server.
Implement: server module; embedded server instantiation; configurable port;
startup; shutdown; lifecycle state; graceful shutdown; error handling; health
status; resource cleanup; integration with existing architecture.
The server must not start unexpectedly without required configuration.
Keep Android storage/domain logic independent of Ktor; define a clear
application interface between HTTP handlers and storage use cases.
The server must NOT expose unauthenticated file operations.
Local access: justified network binding + security model. Do NOT expose an
unrestricted public Android inbound port for remote access.
Create actual lifecycle tests.

# Task 7: Phase M / Sprint 3C — API routes and streaming

Versioned, documented API. Endpoints for: health; node status; storage info;
file listing; directory listing; file metadata; download; upload; directory
create; rename; move; copy; delete; transfer status; transfer cancellation.
Appropriate HTTP semantics + status codes.
Requirements: streaming request bodies; streaming response bodies; bounded
memory; coroutine cancellation; client-disconnect handling; size limits; hash
verification; error mapping; idempotency where appropriate; safe resource
cleanup. Do NOT read an entire large file into memory.
Generate/update OpenAPI docs; consistent error response formats. Test every
route.

# Task 8: Phase N / Sprint 3D — security middleware

Implement: authentication; authorization; device identity; secure
configuration; request validation; rate limiting; request-size limits;
pairing authorization checks; transfer-specific permissions; security event
logging; revocation handling.
Use Android Keystore for relevant device-held crypto material.
Never commit production credentials. Never expose refresh tokens, private
keys, or internal storage paths via logs or API responses.
Unauthorized devices must not browse/access node files.
Do NOT use an insecure development configuration in production builds.

# Task 9: Phase O / Sprint 3E — integration testing

Tests spanning: HTTP server; storage use cases; SAF adapter; authentication;
upload; download; cancellation; invalid permissions; failed streams; large
files; concurrent operations; integrity verification.
Distinguish: unit tests; mock-based tests; instrumentation tests; emulator
tests; physical-device tests. Do NOT label a mock transport test as
end-to-end network verification.

# Task 10: Phase P / Sprint 3F — server UI integration

Integrate the server into the Android Compose UI: server start/stop controls;
current server status; storage setup status; available storage information
(where reliably obtainable); active transfer list; transfer progress;
transfer failures; retry/cancel controls; device identity; pairing status;
clear error states.
Avoid fake online indicators. Do not block the UI thread. Preserve existing
visual design unless a correctness/usability fix is required.

# Task 11: Phase Q / Sprint 4 — pairing + backend-mediated remote access

APPROVED MVP ARCHITECTURE: **Backend-Mediated Remote Access.**
Control Plane: Android establishes an OUTBOUND authenticated connection to
NexSync Backend (inspect existing backend infra; choose compatible mechanism
such as supported WebSocket or SignalR). Implement node registration;
heartbeat; online/offline status; reconnection; backoff; device
authentication; command dispatch; command acknowledgement; revocation;
session expiry; duplicate-command protection. Do NOT send large file payloads
over the Control Plane.
Data Plane: separate HTTPS transfer sessions. Use existing Backend storage
abstractions where appropriate. Evaluate bounded, backend-managed staging area
(LocalDisk or existing storage service). If staging: isolated temp storage;
quotas; expiry; cleanup; ownership validation; transfer-state metadata; hash
verification; resume/retry; cancellation; idempotency. If direct streaming
relay: bounded buffers; backpressure; timeouts; correct cancellation. Do NOT
introduce an unbounded in-memory relay.
Create scoped, short-lived transfer authorization bound to: user; node;
transfer; file/object; direction; expected size; expected hash where supplied;
expiration; required operation. A transfer ticket must NOT grant unrestricted
access to the owner's other files.
Pairing: secure flow; prefer one-time QR-based challenge or other justified
mechanism. Include device identity; ownership association; short challenge
expiry; authenticated confirmation; secure key handling; revocation; replay
protection; rate limiting. One user must NOT pair/access another user's
storage node.
ADR update: `android-storage-node/docs/architecture/ADR-ANDROID-REMOTE-ACCESS.md`
— set decision to approved Backend-mediated MVP; document why direct P2P and
VPN are deferred; keep Control Plane and Data Plane independent; document
unresolved future optimizations without blocking the MVP.
Backend Relay must NOT mean sending large file contents through
SignalR/WebSocket control messages or opening an unrestricted inbound port on
the Android device.

# Task 12: Phase R / Sprint 5 — background service + local discovery

Compliant Android background behavior. Evaluate + implement an appropriate
foreground service when persistent operation is required. Handle:
notifications; notification permission; user start/stop; lifecycle recovery;
process death; battery restrictions; resource cleanup; safe server shutdown;
reconnection. Do NOT claim Android can guarantee permanent background
execution.
Local discovery via Android NSD/mDNS if appropriate — an optimization for
same-network devices, NOT a replacement for the Backend-mediated remote
architecture.

# Task 13: Phase S / Sprint 6 — desktop and web integration

Integrate NexSync Desktop WPF and NexSync Web React/TypeScript. Inspect
existing architecture; reuse existing API clients, auth conventions, UI
patterns.
Required user capabilities: view paired Android nodes; view actual node
online/offline status; view trustworthy storage information; pair a device;
revoke a device; browse authorized files; upload; download; create folders;
rename/move/copy/delete supported objects; view transfer progress; cancel
transfers; retry safe operations; view meaningful errors.
Same backend contract for both clients. Do NOT expose secrets to the
frontend. Do NOT bypass Backend authorization. Do NOT invent available
storage or online state. The UX must make clear which storage node a file
belongs to.

# Task 14: Phase T / Sprint 7 — E2E testing and hardening

Minimum cross-component scenario (19 steps, per directive §14): configure
Android storage → register/pair Android with a NexSync account → Android
establishes outbound control connection → backend confirms node availability →
desktop lists node → desktop requests upload → backend creates authorized
transfer session → Android receives authorized command → Android stores and
verifies file → backend updates transfer status → desktop downloads same file
→ web accesses same authorized object → SHA-256 values match → disconnect
Android during transfer → verify recoverable behavior → reconnect Android →
resume or safely retry → revoke node → confirm old credentials and tickets
rejected.
Also test: large files; network interruptions; duplicate requests; concurrent
transfers; invalid permissions; expired tickets; unauthorized ownership;
storage exhaustion; server restart; backend restart; Android process
termination.
Run real integration tests where possible. Do NOT substitute mocks for claimed
E2E verification.

Pre-release review (§15): inspect data-loss paths; hash-mismatch paths;
permissions; authentication; authorization; secrets; dependency
vulnerabilities; API exposure; resource leaks; threading; coroutine
cancellation; memory usage; large file streaming; database consistency;
recovery state transitions; temporary file retention; network timeouts; retry
and idempotency; Android lifecycle; sensitive-data logging; Desktop/Web
authorization. Resolve critical + high-severity findings. Prioritize
reliability over cosmetics and premature optimization. Do NOT silently weaken
security or test assertions to obtain a passing build.

# Task 15: Final project completion report

Create `docs/implementation/FINAL_PROJECT_COMPLETION_REPORT.md` with the 22
required sections (final architecture; actual branch + full HEAD; completed
phases + commits; Android implementation; SAF storage implementation;
recovery architecture; Ktor server + API contracts; auth + device pairing;
backend integration; remote-access architecture; desktop integration; web
integration; background service + local discovery; installation and setup;
build instructions; actual build exit codes; actual unit test results; actual
instrumentation results; actual integration/E2E results; APK/AAB artifact path
+ SHA-256 where available; remaining limitations; exact external requirements
for unverified scenarios).

Assign three independent verdicts: Implementation Completion; Verification
Completion; Production Readiness. Never claim 100% completion without
evidence. Never claim production-ready while critical defects or essential
verification gaps remain.

For every build record: exact command; tool versions; exit code; warnings and
failures; test counts; pass/fail/skipped counts; XML report paths; artifact
path; artifact size; SHA-256. For every runtime test record the execution
environment. Distinguish unverified physical-device behavior from verified
emulator behavior. Never invent test results or mark a feature TEST VERIFIED
merely because its source code exists.
