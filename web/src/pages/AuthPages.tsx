import { useState, type FormEvent, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { DeviceRevokedError, currentUserId, enrollNewDevice } from "../api/auth";
import { CheckIcon, CloudIcon, ShieldIcon } from "../components/icons";

const YEAR = new Date().getFullYear();

function errorMessage(e: unknown): string {
  if (typeof e === "object" && e !== null && "response" in e) {
    const resp = (e as { response?: { data?: { detail?: string; message?: string } } }).response;
    return resp?.data?.detail ?? resp?.data?.message ?? "Request failed.";
  }
  return "Request failed.";
}

function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="auth-shell">
      <aside className="auth-hero">
        <div className="auth-brand">
          <span className="logo-badge">
            <CloudIcon size={20} />
          </span>
          NexSync
        </div>
        <div>
          <h2>Your files, in perfect sync across every device.</h2>
          <p>
            Private, end-to-end synchronisation between your desktop, browser and
            everything in between — no plugins, no manual copying.
          </p>
          <div className="auth-points">
            <div className="auth-point">
              <CheckIcon size={18} /> Real-time two-way sync with conflict recovery
            </div>
            <div className="auth-point">
              <CheckIcon size={18} /> Atomic, crash-safe writes on every device
            </div>
            <div className="auth-point">
              <CheckIcon size={18} /> Device-level access control, revoke anytime
            </div>
          </div>
        </div>
        <p style={{ fontSize: "0.82rem", color: "rgba(255,255,255,0.8)" }}>
          © {YEAR} NexSync · Phase 2
        </p>
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
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [revoked, setRevoked] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    setRevoked(false);
    try {
      await login(email, password);
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
      <form onSubmit={submit} style={{ display: "contents" }}>
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
          />
        </div>
        <div className="field">
          <label htmlFor="login-password">Password</label>
          <input
            id="login-password"
            className="input"
            type="password"
            autoComplete="current-password"
            placeholder="••••••••"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>
        {error && <p className="error">{error}</p>}
        {revoked && (
          <button type="button" className="btn btn-outline" disabled={busy} onClick={() => void enroll()}>
            <ShieldIcon size={17} /> Enroll this browser as a new device
          </button>
        )}
        <button type="submit" className="btn btn-primary" disabled={busy} style={{ width: "100%", padding: "0.7rem" }}>
          {busy ? "Signing in…" : "Sign in"}
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
  const [fullName, setFullName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await register(email, password, fullName);
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
      <form onSubmit={submit} style={{ display: "contents" }}>
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
          />
        </div>
        <div className="field">
          <label htmlFor="reg-password">Password</label>
          <input
            id="reg-password"
            className="input"
            type="password"
            autoComplete="new-password"
            placeholder="At least 8 characters"
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>
        {error && <p className="error">{error}</p>}
        <button type="submit" className="btn btn-primary" disabled={busy} style={{ width: "100%", padding: "0.7rem" }}>
          {busy ? "Creating…" : "Create account"}
        </button>
      </form>
      <p className="muted auth-footer">
        Have an account? <Link to="/login">Sign in</Link>
      </p>
    </AuthLayout>
  );
}
