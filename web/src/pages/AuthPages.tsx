import { useState, type FormEvent, type ReactNode, type SVGProps } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { DeviceRevokedError, currentUserId, enrollNewDevice } from "../api/auth";
import { CheckIcon, CloudIcon, FileIcon, FolderIcon, ShieldIcon } from "../components/icons";

const YEAR = new Date().getFullYear();

function errorMessage(e: unknown): string {
  if (typeof e === "object" && e !== null && "response" in e) {
    const resp = (e as { response?: { data?: { detail?: string; message?: string } } }).response;
    return resp?.data?.detail ?? resp?.data?.message ?? "Request failed.";
  }
  return "Request failed.";
}

function EyeIcon(props: SVGProps<SVGSVGElement> & { size?: number }) {
  const { size = 16, ...rest } = props;
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.9}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      <path d="M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6S2 12 2 12Z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  );
}

function EyeOffIcon(props: SVGProps<SVGSVGElement> & { size?: number }) {
  const { size = 16, ...rest } = props;
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.9}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      <path d="M9.53 9.53A3 3 0 0 0 12 15a3 3 0 0 0 2.47-5.47" />
      <path d="M10.7 6.18A10.5 10.5 0 0 1 12 6c7 0 10 6 10 6a14.5 14.5 0 0 1-3.2 4.1" />
      <path d="M6.1 8.9A14.5 14.5 0 0 0 2 12s3 6 10 6a10.5 10.5 0 0 0 3.3-.5" />
      <path d="M2 2 22 22" />
    </svg>
  );
}

function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="auth-shell">
      <aside className="auth-hero">
        <div style={{ display: "flex", flexDirection: "column", gap: "1.4rem", minWidth: 0 }}>
          <div className="auth-brand" style={{ marginBottom: "0.2rem" }}>
            <span className="logo-badge">
              <CloudIcon size={20} />
            </span>
            NexSync
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
            <h2
              style={{
                fontSize: "clamp(1.45rem, 2.2vw, 2.05rem)",
                lineHeight: 1.2,
                maxWidth: "19ch",
                margin: 0,
                letterSpacing: "-0.03em",
              }}
            >
              Your files, synced between browser and Windows.
            </h2>
            <p style={{ margin: 0, lineHeight: 1.6, fontSize: "0.95rem", maxWidth: "42ch", opacity: 0.92 }}>
              Private synchronisation between your browser and Windows desktop — no plugins, no manual copying.
            </p>
            <div className="auth-points" style={{ marginTop: "0.4rem", gap: "0.6rem" }}>
              <div className="auth-point" style={{ fontSize: "0.9rem" }}>
                <CheckIcon size={18} /> Automatic two-way sync with conflict recovery
              </div>
              <div className="auth-point" style={{ fontSize: "0.9rem" }}>
                <CheckIcon size={18} /> Crash-safe writes on Windows desktop
              </div>
              <div className="auth-point" style={{ fontSize: "0.9rem" }}>
                <CheckIcon size={18} /> Device access control — revoke any device
              </div>
            </div>
          </div>

          {/* File-sync illustration — small card stack with sync flow */}
          <div
            aria-hidden="true"
            style={{
              marginTop: "0.6rem",
              background: "rgba(255,255,255,0.13)",
              border: "1px solid rgba(255,255,255,0.22)",
              borderRadius: 16,
              padding: "14px 14px",
              display: "flex",
              alignItems: "center",
              gap: 12,
              maxWidth: 340,
              backdropFilter: "blur(8px)",
              boxShadow: "0 8px 24px rgba(0,0,0,0.14)",
            }}
          >
            <div style={{ display: "flex", flexDirection: "column", gap: 8, flex: 1, minWidth: 0 }}>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  background: "rgba(255,255,255,0.96)",
                  color: "#4a4ac4",
                  borderRadius: 10,
                  padding: "8px 10px",
                  fontSize: "0.78rem",
                  fontWeight: 650,
                  lineHeight: 1,
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                }}
              >
                <FileIcon size={15} style={{ flexShrink: 0 }} /> Quarterly.pdf
                <span
                  style={{
                    marginLeft: "auto",
                    width: 6,
                    height: 6,
                    borderRadius: 99,
                    background: "#22c55e",
                    flexShrink: 0,
                  }}
                />
              </div>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  background: "rgba(255,255,255,0.9)",
                  color: "#4a4ac4",
                  borderRadius: 10,
                  padding: "8px 10px",
                  fontSize: "0.78rem",
                  fontWeight: 650,
                  lineHeight: 1,
                }}
              >
                <FolderIcon size={15} style={{ flexShrink: 0 }} /> Photos
                <span
                  style={{
                    marginLeft: "auto",
                    width: 6,
                    height: 6,
                    borderRadius: 99,
                    background: "#22c55e",
                    flexShrink: 0,
                  }}
                />
              </div>
            </div>

            <div
              style={{
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                justifyContent: "center",
                gap: 2,
                color: "rgba(255,255,255,0.95)",
                flexShrink: 0,
              }}
            >
              <svg width={26} height={26} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.9} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M7 7h10l-2.3-2.3" />
                <path d="M17 17H7l2.3 2.3" />
              </svg>
              <span style={{ fontSize: "0.62rem", fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", opacity: 0.95 }}>sync</span>
            </div>

            <div
              style={{
                background: "rgba(255,255,255,0.96)",
                borderRadius: 12,
                padding: "10px 12px",
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: 6,
                color: "#5b5bd6",
                flexShrink: 0,
                minWidth: 72,
              }}
            >
              <CloudIcon size={22} />
              <span style={{ fontSize: "0.68rem", fontWeight: 750, letterSpacing: "-0.02em", lineHeight: 1 }}>NexSync</span>
              <span style={{ fontSize: "0.62rem", color: "#8b90a0", fontWeight: 600, lineHeight: 1 }}>Windows · Web</span>
            </div>
          </div>
        </div>

        <p style={{ fontSize: "0.82rem", color: "rgba(255,255,255,0.78)", margin: 0, marginTop: "1.5rem" }}>© {YEAR} NexSync</p>
      </aside>
      <main className="auth-panel">
        <div className="auth-card">
          <div className="auth-mobile-brand">
            <span className="logo-badge">
              <CloudIcon size={18} />
            </span>
            NexSync
          </div>
          {children}
        </div>
      </main>
    </div>
  );
}

export function LoginPage() {
  const { login } = useAuth();
  const nav = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPw, setShowPw] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [revoked, setRevoked] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    setRevoked(false);
    try {
      await login(email.trim(), password);
      nav("/");
    } catch (err) {
      if (err instanceof DeviceRevokedError) {
        setRevoked(true);
        setError(err.message);
      } else {
        setError(errorMessage(err));
      }
    } finally {
      setBusy(false);
    }
  };

  const enroll = async () => {
    const userId = currentUserId();
    if (!userId) return;
    if (busy) return;
    setBusy(true);
    try {
      await enrollNewDevice(userId);
      nav("/");
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthLayout>
      <div>
        <h1>Welcome back</h1>
        <p className="subtitle">Sign in to your workspace to continue syncing.</p>
      </div>
      <form onSubmit={submit} style={{ display: "contents" }} noValidate>
        <div className="field">
          <label htmlFor="login-email">Email</label>
          <input
            id="login-email"
            className="input"
            type="email"
            autoComplete="email"
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            disabled={busy}
            aria-disabled={busy}
          />
        </div>
        <div className="field">
          <label htmlFor="login-password">Password</label>
          <div style={{ position: "relative" }}>
            <input
              id="login-password"
              className="input"
              type={showPw ? "text" : "password"}
              autoComplete="current-password"
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              disabled={busy}
              aria-disabled={busy}
              style={{ paddingRight: "2.5rem" }}
            />
            <button
              type="button"
              aria-label={showPw ? "Hide password" : "Show password"}
              aria-pressed={showPw}
              onClick={() => setShowPw((v) => !v)}
              disabled={busy}
              style={{
                position: "absolute",
                right: 6,
                top: "50%",
                transform: "translateY(-50%)",
                border: "none",
                background: "transparent",
                color: "var(--text-faint)",
                padding: 6,
                borderRadius: 8,
                display: "grid",
                placeItems: "center",
                cursor: "pointer",
              }}
            >
              {showPw ? <EyeOffIcon size={16} /> : <EyeIcon size={16} />}
            </button>
          </div>
        </div>
        {error && (
          <p className="error" role="alert" aria-live="polite" style={{ margin: 0 }}>
            {error}
          </p>
        )}
        {revoked && (
          <button type="button" className="btn btn-outline" disabled={busy} onClick={() => void enroll()}>
            <ShieldIcon size={17} /> Enroll this browser as a new device
          </button>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy} aria-busy={busy} style={{ width: "100%", padding: "0.7rem" }}>
          {busy ? (
            <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
              <span
                className="spinner"
                aria-hidden="true"
                style={{
                  width: 16,
                  height: 16,
                  borderWidth: 2,
                  borderColor: "rgba(255,255,255,0.35)",
                  borderTopColor: "#fff",
                }}
              />
              Signing in…
            </span>
          ) : (
            "Sign in"
          )}
        </button>
      </form>
      <p className="muted auth-footer">
        No account? <Link to="/register">Create one</Link>
      </p>
    </AuthLayout>
  );
}

export function RegisterPage() {
  const { register } = useAuth();
  const nav = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPw, setShowPw] = useState(false);
  const [fullName, setFullName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      await register(email.trim(), password, fullName.trim());
      nav("/");
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthLayout>
      <div>
        <h1>Create your workspace</h1>
        <p className="subtitle">Start syncing in under a minute.</p>
      </div>
      <form onSubmit={submit} style={{ display: "contents" }} noValidate>
        <div className="field">
          <label htmlFor="reg-name">Full name</label>
          <input
            id="reg-name"
            className="input"
            autoComplete="name"
            placeholder="Ada Lovelace"
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            required
            disabled={busy}
            aria-disabled={busy}
          />
        </div>
        <div className="field">
          <label htmlFor="reg-email">Email</label>
          <input
            id="reg-email"
            className="input"
            type="email"
            autoComplete="email"
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            disabled={busy}
            aria-disabled={busy}
          />
        </div>
        <div className="field">
          <label htmlFor="reg-password">Password</label>
          <div style={{ position: "relative" }}>
            <input
              id="reg-password"
              className="input"
              type={showPw ? "text" : "password"}
              autoComplete="new-password"
              placeholder="At least 8 characters"
              minLength={8}
              aria-describedby="reg-password-hint"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              disabled={busy}
              aria-disabled={busy}
              style={{ paddingRight: "2.5rem" }}
            />
            <button
              type="button"
              aria-label={showPw ? "Hide password" : "Show password"}
              aria-pressed={showPw}
              onClick={() => setShowPw((v) => !v)}
              disabled={busy}
              style={{
                position: "absolute",
                right: 6,
                top: "50%",
                transform: "translateY(-50%)",
                border: "none",
                background: "transparent",
                color: "var(--text-faint)",
                padding: 6,
                borderRadius: 8,
                display: "grid",
                placeItems: "center",
                cursor: "pointer",
              }}
            >
              {showPw ? <EyeOffIcon size={16} /> : <EyeIcon size={16} />}
            </button>
          </div>
          <span id="reg-password-hint" className="faint" style={{ fontSize: "0.78rem" }}>
            At least 8 characters
          </span>
        </div>
        {error && (
          <p className="error" role="alert" aria-live="polite" style={{ margin: 0 }}>
            {error}
          </p>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy} aria-busy={busy} style={{ width: "100%", padding: "0.7rem" }}>
          {busy ? (
            <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
              <span
                className="spinner"
                aria-hidden="true"
                style={{
                  width: 16,
                  height: 16,
                  borderWidth: 2,
                  borderColor: "rgba(255,255,255,0.35)",
                  borderTopColor: "#fff",
                }}
              />
              Creating…
            </span>
          ) : (
            "Create account"
          )}
        </button>
      </form>
      <p className="muted auth-footer">
        Have an account? <Link to="/login">Sign in</Link>
      </p>
    </AuthLayout>
  );
}
