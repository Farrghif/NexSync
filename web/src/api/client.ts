import axios, { type AxiosRequestConfig } from "axios";

const API_BASE = import.meta.env.VITE_API_BASE ?? "http://localhost:5000/api";

export const api = axios.create({
  baseURL: API_BASE,
  withCredentials: true,
});

let accessToken: string | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
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
        { headers: { "X-CSRF": "1" }, withCredentials: true }
      )
      .then((res) => {
        const token = res.data.accessToken as string;
        setAccessToken(token);
        return token;
      })
      .catch(() => {
        setAccessToken(null);
        return null;
      })
      .finally(() => {
        refreshing = null;
      });
  }
  return refreshing;
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config as AxiosRequestConfig & { _retried?: boolean };
    if (error.response?.status === 401 && original && !original._retried) {
      original._retried = true;
      const token = await refreshSession();
      if (token) {
        return api(original);
      }
      window.dispatchEvent(new CustomEvent("nexsync:unauthorized"));
    }
    return Promise.reject(error);
  }
);

function loadDeviceIdForCurrentUser(): string | null {
  try {
    const userId = sessionStorage.getItem("nexsync:userId");
    if (!userId) return null;
    return localStorage.getItem(`nexsync:deviceId:${userId}`);
  } catch {
    return null;
  }
}

export function newOperationId(): string {
  return crypto.randomUUID();
}
