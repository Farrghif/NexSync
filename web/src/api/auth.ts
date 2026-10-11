import { api, setAccessToken, newOperationId } from "./client";

export interface User {
  id: string;
  email: string;
  fullName: string;
}

interface AuthPayload {
  accessToken: string;
  user: User;
}

function deviceKey(userId: string) {
  return `nexsync:deviceId:${userId}`;
}

function deviceOpKey(userId: string) {
  return `nexsync:deviceOp:${userId}`;
}

function browserDeviceName(): string {
  const ua = navigator.userAgent;
  let browser = "Browser";
  if (ua.includes("Chrome") && !ua.includes("Edg")) browser = "Chrome";
  else if (ua.includes("Edg")) browser = "Edge";
  else if (ua.includes("Firefox")) browser = "Firefox";
  else if (ua.includes("Safari")) browser = "Safari";
  let os = "Unknown OS";
  if (ua.includes("Windows")) os = "Windows";
  else if (ua.includes("Android")) os = "Android";
  else if (ua.includes("Mac")) os = "macOS";
  else if (ua.includes("Linux")) os = "Linux";
  return `${browser} - ${os}`;
}

export async function ensureWebDevice(userId: string): Promise<string> {
  const existing = localStorage.getItem(deviceKey(userId));
  if (existing) return existing;

  let opId = localStorage.getItem(deviceOpKey(userId));
  if (!opId) {
    opId = newOperationId();
    localStorage.setItem(deviceOpKey(userId), opId);
  }
  const res = await api.post(
    "/devices",
    { name: browserDeviceName(), platform: "Web" },
    { headers: { "X-Operation-Id": opId } }
  );
  const deviceId = res.data.deviceId as string;
  localStorage.setItem(deviceKey(userId), deviceId);
  return deviceId;
}

export async function login(email: string, password: string): Promise<User> {
  const res = await api.post<AuthPayload>("/auth/login", { email, password });
  setAccessToken(res.data.accessToken);
  sessionStorage.setItem("nexsync:userId", res.data.user.id);
  await ensureWebDevice(res.data.user.id);
  return res.data.user;
}

export async function register(email: string, password: string, fullName: string): Promise<User> {
  const res = await api.post<AuthPayload>("/auth/register", { email, password, fullName });
  setAccessToken(res.data.accessToken);
  sessionStorage.setItem("nexsync:userId", res.data.user.id);
  await ensureWebDevice(res.data.user.id);
  return res.data.user;
}

export async function logout(): Promise<void> {
  try {
    await api.post("/auth/logout", {}, { headers: { "X-CSRF": "1" } });
  } finally {
    setAccessToken(null);
    sessionStorage.removeItem("nexsync:userId");
  }
}

export function currentUserId(): string | null {
  return sessionStorage.getItem("nexsync:userId");
}

export function currentDeviceId(): string | null {
  const userId = currentUserId();
  if (!userId) return null;
  return localStorage.getItem(deviceKey(userId));
}
