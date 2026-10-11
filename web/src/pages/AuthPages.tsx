import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { DeviceRevokedError, currentUserId, enrollNewDevice } from "../api/auth";

function errorMessage(e: unknown): string {
  if (typeof e === "object" && e !== null && "response" in e) {
    const resp = (e as { response?: { data?: { detail?: string; message?: string } } }).response;
    return resp?.data?.detail ?? resp?.data?.message ?? "Request failed.";
  }
  return "Request failed.";
}

export function LoginPage() {
  const { login } = useAuth();
  const nav = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [revoked, setRevoked] = useState(false);

  const submit = async (e: React.FormEvent) => {
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
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <h1>NexSync</h1>
        <p className="muted">One workspace. Every device. Always in sync.</p>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && <p className="error">{error}</p>}
        {revoked && (
          <button type="button" disabled={busy} onClick={() => void enroll()}>
            Enroll this browser as a new device
          </button>
        )}
        <button type="submit" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
        <p className="muted">No account? <Link to="/register">Register</Link></p>
      </form>
    </div>
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

  const submit = async (e: React.FormEvent) => {
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
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <h1>NexSync</h1>
        <p className="muted">Create your workspace.</p>
        <label>
          Full name
          <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
        </label>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password (min 8 chars)
          <input type="password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && <p className="error">{error}</p>}
        <button type="submit" disabled={busy}>{busy ? "Creating…" : "Register"}</button>
        <p className="muted">Have an account? <Link to="/login">Sign in</Link></p>
      </form>
    </div>
  );
}
