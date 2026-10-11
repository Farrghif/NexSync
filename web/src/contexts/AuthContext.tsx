import { createContext, useContext, useState, useEffect, type ReactNode } from "react";
import { login as apiLogin, register as apiRegister, logout as apiLogout, type User } from "../api/auth";

interface AuthState {
  user: User | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, fullName: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = sessionStorage.getItem("nexsync:user");
    if (stored) {
      try {
        setUser(JSON.parse(stored));
      } catch {
        sessionStorage.removeItem("nexsync:user");
      }
    }
    setLoading(false);
    const onUnauthorized = () => {
      setUser(null);
      sessionStorage.removeItem("nexsync:user");
    };
    window.addEventListener("nexsync:unauthorized", onUnauthorized);
    return () => window.removeEventListener("nexsync:unauthorized", onUnauthorized);
  }, []);

  const persist = (u: User | null) => {
    setUser(u);
    if (u) sessionStorage.setItem("nexsync:user", JSON.stringify(u));
    else sessionStorage.removeItem("nexsync:user");
  };

  const login = async (email: string, password: string) => {
    persist(await apiLogin(email, password));
  };

  const register = async (email: string, password: string, fullName: string) => {
    persist(await apiRegister(email, password, fullName));
  };

  const logout = async () => {
    await apiLogout();
    persist(null);
  };

  return <AuthContext.Provider value={{ user, loading, login, register, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
