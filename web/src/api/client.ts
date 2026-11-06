import axios, { type AxiosRequestConfig } from "axios";

const API_BASE = import.meta.env.VITE_API_BASE ?? "http://localhost:5000/api";

export const api = axios.create({
  baseURL: API_BASE,
  withCredentials: true, // Include cookies (refresh token, session)
});

let accessToken: string | null = null;
let csrfToken: string | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function setCsrfToken(token: string | null) {
  csrfToken = token;
}

api.interceptors.request.use((config) => {
  // Add Bearer token for API authentication
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }

  // Add CSRF token for state-changing operations (POST, PUT, DELETE)
  if (csrfToken && ["post", "put", "delete"].includes(config.method?.toLowerCase() ?? "")) {
    config.headers["X-CSRF-Token"] = csrfToken;
  }

  // Add device ID for sync operations
  const deviceId = loadDeviceIdForCurrentUser();
  if (deviceId) {
    config.headers["X-Device-Id"] = deviceId;
  }

  return config;
});

let refreshing: Promise<string | null> | null = null;

async function refreshSession(): Promise<string | null> {
  if (!refreshing) {
    refreshing = axios
      .post(
        `${API_BASE}/auth/refresh`,
        {},
        { 
          headers: { "X-CSRF-Token": csrfToken ?? "" },
          withCredentials: true 
        }
      )
      .then((res) => {
        const token = res.data.accessToken as string;
        const newCsrfToken = res.data.csrfToken as string | undefined;
        
        setAccessToken(token);
        if (newCsrfToken) setCsrfToken(newCsrfToken);
        
        return token;
      })
      .catch(() => {
        setAccessToken(null);
        setCsrfToken(null);
        return null;
      })
      .finally(() => {
        refreshing = null;
      });
  }
  return refreshing;
}

// Proactive token refresh: refresh 1 minute before expiration
// Access tokens are 15 minutes, so refresh at 14 minutes
function scheduleProactiveRefresh() {
  const refreshInterval = 14 * 60 * 1000; // 14 minutes
  setInterval(async () => {
    if (accessToken) {
      await refreshSession();
    }
  }, refreshInterval);
}

// Start proactive refresh on first successful login
let refreshScheduled = false;

api.interceptors.response.use(
  (res) => {
    // After successful auth response, schedule proactive refresh
    if (!refreshScheduled && res.config.url?.includes("/auth/")) {
      refreshScheduled = true;
      scheduleProactiveRefresh();
    }
    return res;
  },
  async (error) => {
    const original = error.config as AxiosRequestConfig & { _retried?: boolean };
    
    // Handle 401 (Unauthorized) - try refresh token
    if (error.response?.status === 401 && original && !original._retried) {
      original._retried = true;
      const token = await refreshSession();
      if (token) {
        return api(original);
      }
      
      // Refresh failed - dispatch logout event
      window.dispatchEvent(new CustomEvent("nexsync:unauthorized"));
    }
    
    return Promise.reject(error);
  }
);

function loadDeviceIdForCurrentUser(): string | null {
  try {
    const userId = sessionStorage.getItem("nexsync:userId");
    if (!userId) return null;
    // Store in localStorage (persistent) but scoped by user
    return localStorage.getItem(`nexsync:deviceId:${userId}`);
  } catch {
    return null;
  }
}

export function newOperationId(): string {
  return crypto.randomUUID();
}
