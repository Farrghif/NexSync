import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode, type CSSProperties } from "react";
import { useAuth } from "../contexts/AuthContext";
import { currentDeviceId } from "../api/auth";
import {
  getContents, createFolder, updateFolder, deleteFolder,
  uploadFile, updateFile, deleteFile,
  type FolderDto, type FileDto,
} from "../api/files";
import { api } from "../api/client";
import { useToast } from "../components/toast";
import { useTheme } from "../components/useTheme";
import { ConfirmDialog, PromptDialog } from "../components/Modal";
import { SortMenu } from "../components/SortMenu";
import { DEFAULT_SORT, isValidSort, type SortDir, type SortKey, type SortState } from "../components/sort";
import {
  ChevronRight, CloudIcon, DeviceIcon, DownloadIcon, FileIcon, FolderIcon,
  FolderPlusIcon, GridIcon, ListIcon, LogoutIcon, MoonIcon, PencilIcon,
  SearchIcon, StarIcon, StarFilledIcon, EyeIcon, InfoIcon, MoveIcon, HistoryIcon,
  SunIcon, TrashIcon, UploadIcon, CheckIcon, AlertIcon, XIcon,
} from "../components/icons";

interface Crumb {
  id: string | null;
  name: string;
}

type ViewMode = "grid" | "list";

type PromptMode = "newFolder" | "renameFolder" | "renameFile";

interface PromptState {
  mode: PromptMode;
  targetId?: string;
  parentId?: string | null;
  initial?: string;
}

interface ConfirmState {
  kind: "folder" | "file";
  id: string;
  name: string;
}

interface FolderItem {
  kind: "folder";
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  parentFolderId: string | null;
  originDeviceId: string | null;
}

interface FileItem {
  kind: "file";
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  folderId: string | null;
  size: number;
  contentType: string;
  originDeviceId: string | null;
}

type Item = FolderItem | FileItem;

// extensible sync model — backend doesn't provide per-file status yet, derive client-side
type SyncStatus = "synced" | "pending" | "syncing" | "failed" | "offline";

const FAV_KEY = "nexsync:fav";
const RECENT_KEY = "nexsync:recent";
const MAX_RECENT = 12;

type FavoriteId = string; // keyFor(item) = "kind:id"

function loadFavorites(): Set<string> {
  try {
    const raw = localStorage.getItem(FAV_KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw) as unknown;
    return new Set(Array.isArray(arr) ? (arr as string[]) : []);
  } catch { return new Set(); }
}
function saveFavorites(s: Set<string>) {
  try { localStorage.setItem(FAV_KEY, JSON.stringify([...s])); } catch { /* ignore */ }
}
function loadRecent(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY);
    if (!raw) return [];
    const arr = JSON.parse(raw) as unknown;
    return Array.isArray(arr) ? (arr as string[]).slice(0, MAX_RECENT) : [];
  } catch { return []; }
}
function saveRecent(ids: string[]) {
  try { localStorage.setItem(RECENT_KEY, JSON.stringify(ids.slice(0, MAX_RECENT))); } catch { /* ignore */ }
}

const SORT_KEY = "nexsync:sort";
const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: "base" });

function loadSort(): SortState {
  try {
    const raw = localStorage.getItem(SORT_KEY);
    if (!raw) return DEFAULT_SORT;
    const parsed: unknown = JSON.parse(raw);
    return isValidSort(parsed) ? parsed : DEFAULT_SORT;
  } catch {
    return DEFAULT_SORT;
  }
}

function timeMs(value: string | undefined): number {
  const t = Date.parse(value ?? "");
  return Number.isNaN(t) ? 0 : t;
}

function compareItems(a: Item, b: Item, key: SortKey, dir: SortDir): number {
  const m = dir === "asc" ? 1 : -1;
  let r: number;
  switch (key) {
    case "name":
      r = collator.compare(a.name, b.name);
      break;
    case "modified":
      r = timeMs(a.updatedAt) - timeMs(b.updatedAt);
      break;
    case "created":
      r = timeMs(a.createdAt) - timeMs(b.createdAt);
      break;
    case "size":
      r = (a.kind === "file" ? a.size : -1) - (b.kind === "file" ? b.size : -1);
      break;
    case "type":
      r = collator.compare(
        a.kind === "folder" ? "" : a.contentType || "file",
        b.kind === "folder" ? "" : b.contentType || "file",
      );
      break;
  }
  if (r === 0) r = collator.compare(a.name, b.name);
  return m * r;
}

// keep values in the form the platform already gives you — no extra deps

type FileKind = "image" | "pdf" | "archive" | "audio" | "video" | "doc" | "sheet" | "code" | "file";

function getFileKind(contentType: string, name: string): FileKind {
  const ct = (contentType || "").toLowerCase();
  const ext = (name.split(".").pop() || "").toLowerCase();
  if (ct.startsWith("image/")) return "image";
  if (ct.includes("pdf") || ext === "pdf") return "pdf";
  if (ct.includes("zip") || ct.includes("archive") || ct.includes("compressed") || ["zip", "rar", "7z", "tar", "gz", "bz2"].includes(ext)) return "archive";
  if (ct.startsWith("audio/")) return "audio";
  if (ct.startsWith("video/")) return "video";
  if (ct.includes("spreadsheet") || ct.includes("excel") || ["xls", "xlsx", "csv"].includes(ext)) return "sheet";
  if (ct.includes("presentation") || ["ppt", "pptx"].includes(ext)) return "doc";
  if (ct.includes("word") || ct.includes("document") || ["doc", "docx"].includes(ext)) return "doc";
  if (["js", "ts", "tsx", "jsx", "json", "html", "css", "py", "rs", "go", "java", "cs", "cpp", "c", "h", "yml", "yaml", "md"].includes(ext)) return "code";
  return "file";
}

function fileKindStyle(kind: FileKind): CSSProperties {
  // uses existing CSS variables so theme stays consistent
  switch (kind) {
    case "image":
      return { background: "color-mix(in srgb, var(--brand) 14%, var(--surface))", color: "var(--brand)", border: "1px solid color-mix(in srgb, var(--brand) 18%, transparent)" };
    case "pdf":
      return { background: "var(--danger-soft)", color: "var(--danger)", border: "1px solid color-mix(in srgb, var(--danger) 18%, transparent)" };
    case "archive":
      return { background: "var(--warning-soft)", color: "var(--warning)", border: "1px solid color-mix(in srgb, var(--warning) 18%, transparent)" };
    case "audio":
    case "video":
      return { background: "var(--brand-soft)", color: "var(--brand)", border: "1px solid color-mix(in srgb, var(--brand) 18%, transparent)" };
    case "sheet":
      return { background: "var(--success-soft)", color: "var(--success)", border: "1px solid color-mix(in srgb, var(--success) 18%, transparent)" };
    case "doc":
      return { background: "color-mix(in srgb, var(--brand) 10%, var(--surface-2))", color: "var(--brand)", border: "1px solid var(--border)" };
    case "code":
      return { background: "var(--surface-3)", color: "var(--text-muted)", border: "1px solid var(--border)" };
    default:
      return { background: "var(--surface-3)", color: "var(--text-muted)", border: "1px solid var(--border)" };
  }
}

function deriveSyncStatus(item: Item, isOffline: boolean, pendingIds: Set<string>): SyncStatus {
  if (isOffline) return "offline";
  if (pendingIds.has(item.id)) return "pending";
  // future: check syncing/failed sets here — keep model extensible for Pending/Syncing/Failed/Offline
  if (!item.id) return "pending";
  // synced is default — file exists, has id, has size/contentType or folder has id
  return "synced";
}

function formatRelativeSync(d: Date | null): string {
  if (!d) return "—";
  const diff = Date.now() - d.getTime();
  if (diff < 60_000) return "just now";
  if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
  if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
  return d.toLocaleString();
}

function MoreIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="1.7" fill="currentColor" />
      <circle cx="12" cy="5.4" r="1.7" fill="currentColor" />
      <circle cx="12" cy="18.6" r="1.7" fill="currentColor" />
    </svg>
  );
}

interface FileNameMarqueeProps {
  name: string;
  isFav?: boolean;
}

function FileNameMarquee({ name, isFav }: FileNameMarqueeProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [overflowDistance, setOverflowDistance] = useState(0);

  const measure = useCallback(() => {
    if (!containerRef.current || !textRef.current) return;
    const diff = textRef.current.scrollWidth - containerRef.current.clientWidth;
    setOverflowDistance(diff > 2 ? diff : 0);
  }, []);

  useEffect(() => {
    measure();
    const raf = requestAnimationFrame(measure);
    let ro: ResizeObserver | null = null;
    if (typeof ResizeObserver !== "undefined" && containerRef.current) {
      ro = new ResizeObserver(() => measure());
      ro.observe(containerRef.current);
    }
    return () => {
      cancelAnimationFrame(raf);
      ro?.disconnect();
    };
  }, [name, measure]);

  const isOverflowing = overflowDistance > 0;
  const duration = Math.max(2.6, Math.min(8, overflowDistance / 40 + 1.8));

  return (
    <div className="card-name-row" title={name}>
      <div
        ref={containerRef}
        className="card-name-wrap"
        onMouseEnter={measure}
      >
        <span
          ref={textRef}
          className={`card-name-text${isOverflowing ? " is-overflowing" : ""}`}
          style={
            isOverflowing
              ? ({
                  "--scroll-dist": `-${overflowDistance + 8}px`,
                  "--scroll-dur": `${duration}s`,
                } as CSSProperties)
              : undefined
          }
        >
          {name}
        </span>
      </div>
      {isFav && (
        <span title="Favorite" aria-label="Favorite" className="card-fav-star">
          <StarFilledIcon size={13} />
        </span>
      )}
    </div>
  );
}

export function DashboardPage() {
  const { user, logout } = useAuth();
  const toast = useToast();
  const { theme, toggle: toggleTheme } = useTheme();

  const [crumbs, setCrumbs] = useState<Crumb[]>([{ id: null, name: "Root" }]);
  const [folders, setFolders] = useState<FolderDto[]>([]);
  const [files, setFiles] = useState<FileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<ViewMode>("grid");
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<SortState>(loadSort);
  const [deviceFilter, setDeviceFilter] = useState<string | null>(null);
  const [uploadPct, setUploadPct] = useState<number | null>(null);
  const [prompt, setPrompt] = useState<PromptState | null>(null);
  const [confirm, setConfirm] = useState<ConfirmState | null>(null);
  const [busy, setBusy] = useState(false);
  const [online, setOnline] = useState<boolean>(() => (typeof navigator !== "undefined" ? navigator.onLine : true));
  const [lastSyncAt, setLastSyncAt] = useState<Date | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [showDeviceDetails, setShowDeviceDetails] = useState(false);
  const [selectMode, setSelectMode] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const lastSelectedRef = useRef<string | null>(null);
  // favorites & recent (local, no backend change) — very useful, zero risk
  const [favorites, setFavorites] = useState<Set<string>>(() => loadFavorites());
  const [recentIds, setRecentIds] = useState<string[]>(() => loadRecent());
  const [dragOver, setDragOver] = useState(false);
  const [preview, setPreview] = useState<FileItem | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [movePicker, setMovePicker] = useState<{ keys: string[] } | null>(null);
  const [moveTarget, setMoveTarget] = useState<string | null>(null); // folder id or null = Root
  const [infoItem, setInfoItem] = useState<Item | null>(null);
  const [filterMode, setFilterMode] = useState<"all" | "fav" | "recent">("all");
  // tick forces relative time refresh every 30s without extra deps
  const [, setTick] = useState(0);
  const fileInput = useRef<HTMLInputElement>(null);

  const keyFor = useCallback((item: Item) => `${item.kind}:${item.id}`, []);

  const toggleFavorite = useCallback((k: FavoriteId) => {
    setFavorites((prev) => {
      const next = new Set(prev);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      saveFavorites(next);
      return next;
    });
  }, []);

  const pushRecent = useCallback((k: FavoriteId) => {
    setRecentIds((prev) => {
      const next = [k, ...prev.filter((x) => x !== k)].slice(0, MAX_RECENT);
      saveRecent(next);
      return next;
    });
  }, []);

  const current = crumbs[crumbs.length - 1];
  const device = currentDeviceId();
  const isDeviceFiltering = deviceFilter !== null;

  useEffect(() => {
    localStorage.setItem(SORT_KEY, JSON.stringify(sort));
  }, [sort]);

  useEffect(() => {
    const onOnline = () => setOnline(true);
    const onOffline = () => setOnline(false);
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("offline", onOffline);
    };
  }, []);

  useEffect(() => {
    if (!lastSyncAt) return;
    const id = window.setInterval(() => setTick((t) => t + 1), 30_000);
    return () => window.clearInterval(id);
  }, [lastSyncAt]);

  useEffect(() => {
    if (!openMenuId) return;
    const onDoc = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (!target.closest("[data-kebab-root]")) setOpenMenuId(null);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpenMenuId(null);
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [openMenuId]);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const data = await getContents(current.id);
      setFolders(data.folders);
      setFiles(data.files);
      setLastSyncAt(new Date());
    } catch (e) {
      const msg = describeError(e);
      setLoadError(msg);
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }, [current.id, toast]);

  useEffect(() => {
    void load();
  }, [load]);

  const enterSelectMode = useCallback((initialKey?: string) => {
    setSelectMode(true);
    if (initialKey) {
      setSelected(new Set([initialKey]));
      lastSelectedRef.current = initialKey;
    } else {
      setSelected(new Set());
      lastSelectedRef.current = null;
    }
  }, []);
  const exitSelectMode = useCallback(() => {
    setSelectMode(false);
    setSelected(new Set());
    lastSelectedRef.current = null;
  }, []);
  const clearSelection = useCallback(() => {
    if (selected.size === 0) { setSelectMode(false); lastSelectedRef.current = null; return; }
    setSelected(new Set());
    lastSelectedRef.current = null;
  }, [selected.size]);

  const enterFolder = (item: FolderItem) => {
    if (selectMode) exitSelectMode(); else clearSelection();
    setQuery("");
    setOpenMenuId(null);
    setCrumbs((c) => [...c, { id: item.id, name: item.name }]);
  };
  const goCrumb = (i: number) => {
    if (selectMode) exitSelectMode(); else clearSelection();
    setQuery("");
    setOpenMenuId(null);
    setCrumbs((c) => c.slice(0, i + 1));
  };

  const q = query.trim().toLowerCase();

  const items = useMemo<Item[]>(() => {
    const folderItems: FolderItem[] = folders
      .filter((f) => !q || f.name.toLowerCase().includes(q))
      .filter((f) => !isDeviceFiltering || f.originDeviceId === deviceFilter)
      .map((f) => ({
        kind: "folder",
        id: f.id,
        name: f.name,
        createdAt: f.createdAt,
        updatedAt: f.updatedAt,
        parentFolderId: f.parentFolderId,
        originDeviceId: f.originDeviceId,
      }));
    const fileItems: FileItem[] = files
      .filter((f) => !q || f.name.toLowerCase().includes(q))
      .filter((f) => !isDeviceFiltering || f.originDeviceId === deviceFilter)
      .map((f) => ({
        kind: "file",
        id: f.id,
        name: f.name,
        createdAt: f.createdAt,
        updatedAt: f.updatedAt,
        folderId: f.folderId,
        size: f.size,
        contentType: f.contentType,
        originDeviceId: f.originDeviceId,
      }));
    return [...folderItems, ...fileItems];
  }, [folders, files, q, deviceFilter, isDeviceFiltering]);

  const comparator = useCallback(
    (a: Item, b: Item) => compareItems(a, b, sort.key, sort.dir),
    [sort.key, sort.dir],
  );

  // filtered view: All / Favorites / Recent (client-only, no backend)
  const visibleItems = useMemo(() => {
    if (filterMode === "fav") return items.filter((it) => favorites.has(keyFor(it)));
    if (filterMode === "recent") {
      const order = new Map(recentIds.map((k, i) => [k, i] as const));
      return items
        .filter((it) => order.has(keyFor(it)))
        .slice()
        .sort((a, b) => (order.get(keyFor(a))! - order.get(keyFor(b))!));
    }
    return items;
  }, [items, filterMode, favorites, recentIds, keyFor]);

  const sortedFolders = useMemo(
    () => visibleItems.filter((i): i is FolderItem => i.kind === "folder").slice().sort((a, b) => {
      if (filterMode === "recent") return 0; // keep recent order for folders+files split — fallback to comparator
      return comparator(a, b);
    }),
    [visibleItems, comparator, filterMode],
  );
  const sortedFiles = useMemo(
    () => visibleItems.filter((i): i is FileItem => i.kind === "file").slice().sort((a, b) => {
      if (filterMode === "recent") return 0;
      return comparator(a, b);
    }),
    [visibleItems, comparator, filterMode],
  );
  const combined = useMemo(() => {
    if (filterMode === "recent") return visibleItems.slice();
    return visibleItems.slice().sort(comparator);
  }, [visibleItems, comparator, filterMode]);
  const favCount = useMemo(() => items.filter((it) => favorites.has(keyFor(it))).length, [items, favorites, keyFor]);

  // pendingIds stays empty for now — all existing files are Synced — keep extensible
  const pendingIds = useMemo(() => new Set<string>(), []);
  const pendingCount = uploadPct !== null ? 1 : 0;

  // --- selection (Explorer-like): Ctrl/Cmd toggle, Shift range, checkbox, Select all ---
  // prune stale ids when folder/filter changes
  useEffect(() => {
    if (selected.size === 0) return;
    const alive = new Set(items.map((it) => keyFor(it)));
    let changed = false;
    const next = new Set<string>();
    selected.forEach((k) => { if (alive.has(k)) next.add(k); else changed = true; });
    if (changed) setSelected(next);
    if (changed && lastSelectedRef.current && !alive.has(lastSelectedRef.current)) lastSelectedRef.current = null;
  }, [items, keyFor, selected]);

  const isSelected = useCallback((k: string) => selected.has(k), [selected]);

  const toggleOne = useCallback((k: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      return next;
    });
    lastSelectedRef.current = k;
  }, []);

  const selectRange = useCallback((ordered: Item[], targetKey: string) => {
    const last = lastSelectedRef.current;
    if (!last) { toggleOne(targetKey); return; }
    const keys = ordered.map((it) => keyFor(it));
    const a = keys.indexOf(last);
    const b = keys.indexOf(targetKey);
    if (a === -1 || b === -1) { toggleOne(targetKey); return; }
    const [lo, hi] = a < b ? [a, b] : [b, a];
    const range = keys.slice(lo, hi + 1);
    setSelected((prev) => {
      const next = new Set(prev);
      range.forEach((k) => next.add(k));
      return next;
    });
    lastSelectedRef.current = targetKey;
  }, [keyFor, toggleOne]);

  const handleSelectClick = useCallback((e: React.MouseEvent, ordered: Item[], item: Item) => {
    const k = keyFor(item);
    if (e.shiftKey) { e.preventDefault(); selectRange(ordered, k); return; }
    if (e.ctrlKey || e.metaKey) { e.preventDefault(); toggleOne(k); return; }
    // plain click when selection exists — Explorer: select only this item
    e.preventDefault();
    setSelected(new Set([k]));
    lastSelectedRef.current = k;
  }, [keyFor, selectRange, toggleOne]);

  const selectAllVisible = useCallback((ordered: Item[]) => {
    setSelected(new Set(ordered.map((it) => keyFor(it))));
    lastSelectedRef.current = ordered.length ? keyFor(ordered[ordered.length - 1]) : null;
  }, [keyFor]);

  const runBulkDelete = useCallback(async (keys: string[]) => {
    if (keys.length === 0) return;
    const toDelete = keys.map((k) => {
      const [kind, id] = k.split(":") as ["folder" | "file", string];
      const found = [...folders, ...files].find((x) => x.id === id);
      return { kind, id, name: (found as { name?: string })?.name ?? id };
    });
    setBusy(true);
    try {
      for (const it of toDelete) {
        if (it.kind === "folder") await deleteFolder(it.id);
        else await deleteFile(it.id);
      }
      toast.success(keys.length === 1 ? `Deleted "${toDelete[0].name}".` : `Deleted ${keys.length} items.`);
      clearSelection();
      await load();
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setBusy(false);
    }
  }, [folders, files, toast, clearSelection, load]);

  // Explorer-like keyboard — only when select mode is on (triggered via ⋮ → Select)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase();
      const inInput = tag === "input" || tag === "textarea" || (e.target as HTMLElement | null)?.isContentEditable;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "a" && !inInput && selectMode) {
        e.preventDefault();
        if (combined.length) selectAllVisible(combined);
      } else if (e.key === "Escape" && !openMenuId && !prompt && !confirm && !showDeviceDetails) {
        if (selectMode) { e.preventDefault(); exitSelectMode(); }
        else if (selected.size > 0) { e.preventDefault(); clearSelection(); }
      } else if ((e.key === "Delete" || e.key === "Backspace") && selectMode && selected.size > 0 && !inInput) {
        e.preventDefault();
        const keys = Array.from(selected);
        const label = keys.length === 1 ? "1 item" : `${keys.length} items`;
        if (window.confirm(`Delete ${label}? This cannot be undone.`)) void runBulkDelete(keys);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [combined, selected, openMenuId, prompt, confirm, showDeviceDetails, selectAllVisible, clearSelection, selectMode, exitSelectMode, runBulkDelete]);

  const onUpload = async (picked: FileList | null) => {
    if (!picked || picked.length === 0) return;
    try {
      for (const file of Array.from(picked)) {
        setUploadPct(0);
        await uploadFile(file, current.id, setUploadPct);
      }
      setUploadPct(null);
      toast.success(`Uploaded ${picked.length} file${picked.length > 1 ? "s" : ""}.`);
      await load();
    } catch (e) {
      setUploadPct(null);
      toast.error(describeError(e));
    }
  };

  const submitPrompt = async (value: string) => {
    if (!prompt) return;
    setBusy(true);
    try {
      if (prompt.mode === "newFolder") {
        await createFolder(value, current.id);
        toast.success(`Folder "${value}" created.`);
      } else if (prompt.mode === "renameFolder" && prompt.targetId) {
        await updateFolder(prompt.targetId, value, prompt.parentId ?? null);
        toast.success("Folder renamed.");
      } else if (prompt.mode === "renameFile" && prompt.targetId) {
        await updateFile(prompt.targetId, value, prompt.parentId ?? null);
        toast.success("File renamed.");
      }
      setPrompt(null);
      await load();
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setBusy(false);
    }
  };

  const runConfirm = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      if (confirm.kind === "folder") {
        await deleteFolder(confirm.id);
        toast.success(`Folder "${confirm.name}" deleted.`);
      } else {
        await deleteFile(confirm.id);
        toast.success(`File "${confirm.name}" deleted.`);
      }
      setConfirm(null);
      // if deleted item was selected, prune it
      setSelected((prev) => {
        const k = `${confirm.kind}:${confirm.id}`;
        if (!prev.has(k)) return prev;
        const n = new Set(prev); n.delete(k); return n;
      });
      await load();
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setBusy(false);
    }
  };

  const onDownload = useCallback(async (item: { id: string; name: string }) => {
    try {
      const res = await api.get(`/files/${item.id}/download`, { responseType: "blob" });
      const url = URL.createObjectURL(res.data);
      const a = document.createElement("a");
      a.href = url;
      a.download = item.name;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      toast.error(describeError(e));
    }
  }, [toast]);

  const runBulkDownload = async (keys: string[]) => {
    const fileKeys = keys.filter((k) => k.startsWith("file:"));
    if (fileKeys.length === 0) { toast.error("No files selected."); return; }
    let ok = 0;
    for (const k of fileKeys) {
      const id = k.slice(5);
      const f = files.find((x) => x.id === id);
      if (!f) continue;
      try { await onDownload({ id, name: f.name }); ok++; } catch { /* onDownload already toasts */ }
    }
    if (ok > 1) toast.success(`Started ${ok} downloads.`);
  };

  const openPreview = useCallback(async (item: FileItem) => {
    const ct = (item.contentType || "").toLowerCase();
    const ext = item.name.split(".").pop()?.toLowerCase() ?? "";
    const isPreviewable =
      ct.startsWith("image/") ||
      ct.startsWith("text/") ||
      ct.includes("pdf") ||
      ct.includes("json") ||
      ["md", "txt", "csv", "log"].includes(ext);
    if (!isPreviewable) { void onDownload(item); pushRecent(keyFor(item)); return; }
    setPreview(item);
    setPreviewUrl(null);
    pushRecent(keyFor(item));
    try {
      const res = await api.get(`/files/${item.id}/download`, { responseType: "blob" });
      const url = URL.createObjectURL(res.data);
      setPreviewUrl(url);
    } catch (e) {
      toast.error(describeError(e));
      setPreview(null);
    }
  }, [keyFor, pushRecent, toast, onDownload]);

  useEffect(() => {
    if (!previewUrl) return;
    return () => { URL.revokeObjectURL(previewUrl); };
  }, [previewUrl]);

  const confirmMove = useCallback(async () => {
    if (!movePicker || movePicker.keys.length === 0) return;
    const target = moveTarget; // null = Root
    setBusy(true);
    try {
      for (const k of movePicker.keys) {
        const [kind, id] = k.split(":") as [Item["kind"], string];
        if (kind === "folder") {
          const f = folders.find((x) => x.id === id);
          if (!f) continue;
          if (f.parentFolderId === target) continue;
          // avoid moving folder into itself
          if (target === id) { toast.error(`Cannot move "${f.name}" into itself.`); continue; }
          await updateFolder(id, f.name, target);
        } else {
          const f = files.find((x) => x.id === id);
          if (!f) continue;
          if (f.folderId === target) continue;
          await updateFile(id, f.name, target);
        }
      }
      toast.success(movePicker.keys.length === 1 ? "Moved 1 item." : `Moved ${movePicker.keys.length} items.`);
      setMovePicker(null);
      exitSelectMode();
      await load();
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setBusy(false);
    }
  }, [movePicker, moveTarget, folders, files, toast, load, exitSelectMode]);

  const openItem = (item: Item) => {
    if (item.kind === "folder") enterFolder(item);
    else void openPreview(item);
  };

  const renameItem = (item: Item) => {
    if (item.kind === "folder") {
      setPrompt({ mode: "renameFolder", targetId: item.id, parentId: item.parentFolderId, initial: item.name });
    } else {
      setPrompt({ mode: "renameFile", targetId: item.id, parentId: item.folderId, initial: item.name });
    }
  };

  const askDelete = (item: Item) => setConfirm({ kind: item.kind, id: item.id, name: item.name });

  const copyDeviceId = async () => {
    if (!device) return;
    try {
      await navigator.clipboard.writeText(device);
      toast.success("Device ID copied.");
    } catch {
      toast.error("Could not copy device ID.");
    }
  };

  const initials = (user?.fullName ?? user?.email ?? "?")
    .split(/\s+/)
    .map((s) => s[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  const promptConfig = promptConfigFor(prompt, current.name);

  // ---- sync helpers — quiet by default: synced shows nothing (normal state shouldn't scream) ----
  const syncDot = (status: SyncStatus) => {
    if (status === "synced") return null;
    const style: Record<SyncStatus, CSSProperties> = {
      synced: { background: "var(--success)", boxShadow: "0 0 0 3px var(--success-soft)" },
      pending: { background: "var(--warning)", boxShadow: "0 0 0 3px var(--warning-soft)" },
      syncing: { background: "var(--brand)", boxShadow: "0 0 0 3px var(--brand-soft)" },
      failed: { background: "var(--danger)", boxShadow: "0 0 0 3px var(--danger-soft)" },
      offline: { background: "var(--text-faint)", boxShadow: "0 0 0 3px var(--surface-3)" },
    };
    const label: Record<SyncStatus, string> = {
      synced: "Synced",
      pending: "Pending",
      syncing: "Syncing",
      failed: "Failed",
      offline: "Offline",
    };
    return (
      <span style={{ display: "inline-flex", alignItems: "center", gap: 6, fontSize: "0.72rem", fontWeight: 650, letterSpacing: "0.02em" }}>
        <span aria-hidden="true" style={{ width: 8, height: 8, borderRadius: 999, flexShrink: 0, ...style[status] }} />
        <span style={{ color: status === "offline" ? "var(--text-faint)" : status === "failed" ? "var(--danger)" : "var(--text-muted)" }}>{label[status]}</span>
      </span>
    );
  };

  const KebabMenu = ({ item }: { item: Item }) => {
    const isOpen = openMenuId === item.id;
    const kFav = keyFor(item);
    const isFav = favorites.has(kFav);
    return (
      <div data-kebab-root style={{ position: "relative" }}>
        <button
          type="button"
          className="icon-btn"
          aria-label={`More actions for ${item.name}`}
          aria-haspopup="menu"
          aria-expanded={isOpen}
          title="More actions"
          onClick={(e) => {
            e.stopPropagation();
            setOpenMenuId(isOpen ? null : item.id);
          }}
          style={{
            opacity: 1,
            background: isOpen ? "var(--surface-3)" : undefined,
            borderColor: isOpen ? "var(--border-strong)" : undefined,
          }}
        >
          <MoreIcon size={16} />
        </button>
        {isOpen && (
          <div
            role="menu"
            aria-label={`Actions for ${item.name}`}
            onClick={(e) => e.stopPropagation()}
            style={{
              position: "absolute",
              top: "calc(100% + 6px)",
              right: 0,
              minWidth: 184,
              background: "var(--surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              boxShadow: "var(--shadow-lg)",
              padding: 6,
              zIndex: 40,
              display: "flex",
              flexDirection: "column",
              gap: 2,
            }}
          >
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem" }}
              onClick={() => {
                setOpenMenuId(null);
                const k = keyFor(item);
                if (!selectMode) enterSelectMode(k);
                else toggleOne(k);
                if (!selectMode) setTimeout(() => toast.success("Selection mode on — pick more files, then use the bar above."), 0);
              }}
            >
              <span className="menu-ico"><CheckIcon size={16} /></span>
              <span className="menu-text">{selectMode && selected.has(keyFor(item)) ? "Deselect" : "Select"}</span>
            </button>
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem" }}
              onClick={() => { setOpenMenuId(null); toggleFavorite(kFav); }}
            >
              <span className="menu-ico">{isFav ? <StarFilledIcon size={16} /> : <StarIcon size={16} />}</span>
              <span className="menu-text">{isFav ? "Unfavorite" : "Favorite"}</span>
            </button>
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem" }}
              onClick={() => { setOpenMenuId(null); setInfoItem(item); }}
            >
              <span className="menu-ico"><InfoIcon size={16} /></span>
              <span className="menu-text">Details</span>
            </button>
            {item.kind === "file" && (
              <button
                type="button"
                role="menuitem"
                className="menu-row"
                style={{ fontSize: "0.86rem" }}
                onClick={() => { setOpenMenuId(null); void openPreview(item as FileItem); }}
              >
                <span className="menu-ico"><EyeIcon size={16} /></span>
                <span className="menu-text">Preview</span>
              </button>
            )}
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem" }}
              onClick={() => { setOpenMenuId(null); setMovePicker({ keys: [keyFor(item)] }); setMoveTarget(current.id); }}
            >
              <span className="menu-ico"><MoveIcon size={16} /></span>
              <span className="menu-text">Move to…</span>
            </button>
            <div style={{ height: 1, background: "var(--border)", margin: "4px 2px" }} aria-hidden="true" />
            {item.kind === "file" && (
              <button
                type="button"
                role="menuitem"
                className="menu-row"
                style={{ fontSize: "0.86rem" }}
                onClick={() => {
                  setOpenMenuId(null);
                  void onDownload(item);
                }}
              >
                <span className="menu-ico"><DownloadIcon size={16} /></span>
                <span className="menu-text">Download</span>
              </button>
            )}
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem" }}
              onClick={() => {
                setOpenMenuId(null);
                renameItem(item);
              }}
            >
              <span className="menu-ico"><PencilIcon size={16} /></span>
              <span className="menu-text">Rename</span>
            </button>
            <button
              type="button"
              role="menuitem"
              className="menu-row"
              style={{ fontSize: "0.86rem", color: "var(--danger)" }}
              onClick={() => {
                setOpenMenuId(null);
                askDelete(item);
              }}
            >
              <span className="menu-ico" style={{ color: "var(--danger)" }}><TrashIcon size={16} /></span>
              <span className="menu-text">Delete</span>
            </button>
          </div>
        )}
      </div>
    );
  };

  // quiet by default — only show Pending/Syncing/Failed/Offline; Synced is normal, no dot
  const cardMetaLine = (item: Item) => {
    const status = deriveSyncStatus(item, !online, pendingIds);
    const dot = syncDot(status);
    if (item.kind === "folder") {
      return dot ? (
        <span style={{ display: "inline-flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <span>Folder</span>
          <span style={{ opacity: 0.6 }}>·</span>
          {dot}
        </span>
      ) : (
        <span>Folder</span>
      );
    }
    return dot ? (
      <span style={{ display: "inline-flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
        <span>{formatSize(item.size)}</span>
        <span style={{ opacity: 0.6 }}>·</span>
        {dot}
      </span>
    ) : (
      <span>{formatSize(item.size)}</span>
    );
  };

  const renderGrid = (list: Item[]) => (
    <div className="grid" data-select-mode={selectMode ? "true" : undefined}>
      {list.map((item) => {
        const k = keyFor(item);
        const checked = selectMode && isSelected(k);
        const status = deriveSyncStatus(item, !online, pendingIds);
        const kind = item.kind === "file" ? getFileKind(item.contentType, item.name) : "file" as const;
        const iconStyle = item.kind === "folder" ? undefined : fileKindStyle(kind);
        const showStatus = status !== "synced";
        return (
          <div className={`card${checked ? " is-selected" : ""}`} key={k} data-selected={checked ? "true" : undefined} aria-selected={checked} style={openMenuId === k ? { zIndex: 5 } : undefined}>
            {selectMode && (
              <label className="card-check" onClick={(e) => e.stopPropagation()} title={checked ? "Deselect" : "Select"}>
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={(e) => {
                    void e;
                    if ((e.nativeEvent as unknown as { shiftKey?: boolean }).shiftKey) selectRange(list, k);
                    else toggleOne(k);
                  }}
                  onClick={(e) => {
                    const me = e as unknown as React.MouseEvent<HTMLInputElement>;
                    if (me.shiftKey) { e.preventDefault(); selectRange(list, k); }
                  }}
                  aria-label={`Select ${item.name}`}
                />
                <span aria-hidden="true" />
              </label>
            )}
            <div className="card-actions" style={{ opacity: 1, display: "flex", gap: 4, alignItems: "center" }}>
              {showStatus && (
                <span
                  aria-hidden="true"
                  title={status}
                  style={{
                    width: 8, height: 8, borderRadius: 999, marginRight: 4, flexShrink: 0,
                    background: status === "offline" ? "var(--text-faint)" : status === "failed" ? "var(--danger)" : status === "pending" ? "var(--warning)" : "var(--brand)",
                    boxShadow: status === "failed" ? "0 0 0 3px var(--danger-soft)" : status === "offline" ? "0 0 0 3px var(--surface-3)" : "0 0 0 3px var(--warning-soft)",
                  }}
                />
              )}
              <KebabMenu item={item} />
            </div>
            <div
              className="card-main"
              role="button"
              tabIndex={0}
              title={item.name}
              aria-label={`${item.kind === "folder" ? "Open folder" : "Download file"} ${item.name}${showStatus ? `, ${status}` : ""}`}
              onClick={(e) => {
                if (!selectMode) { openItem(item); return; }
                // select mode: Explorer-like selection on card
                if (e.ctrlKey || e.metaKey || e.shiftKey) { handleSelectClick(e, list, item); return; }
                handleSelectClick({ ...e, ctrlKey: false, metaKey: false, shiftKey: false, preventDefault() { e.preventDefault(); } } as unknown as React.MouseEvent, list, item);
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openItem(item); }
              }}
            >
              <span className={`card-icon ${item.kind}`} style={iconStyle as CSSProperties} aria-hidden="true">
                {item.kind === "folder" ? <FolderIcon size={21} /> : <FileIcon size={21} />}
              </span>
              <div className="card-info">
                <FileNameMarquee name={item.name} isFav={favorites.has(keyFor(item))} />
                <div className="card-meta">{cardMetaLine(item)}</div>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );

  const renderList = (list: Item[]) => (
    <div className="list" role="list" data-select-mode={selectMode ? "true" : undefined}>
      {list.map((item) => {
        const k = keyFor(item);
        const checked = selectMode && isSelected(k);
        const status = deriveSyncStatus(item, !online, pendingIds);
        const kind = item.kind === "file" ? getFileKind(item.contentType, item.name) : "file" as const;
        const iconStyle = item.kind === "folder" ? undefined : fileKindStyle(kind);
        const showStatus = status !== "synced";
        return (
          <div className={`list-row${checked ? " is-selected" : ""}`} key={k} role="listitem" data-selected={checked ? "true" : undefined} aria-selected={checked} style={openMenuId === k ? { zIndex: 5, position: "relative" } : undefined}>
            {selectMode && (
              <label className="list-check" onClick={(e) => e.stopPropagation()} title={checked ? "Deselect" : "Select"}>
                <input type="checkbox" checked={checked} onChange={() => toggleOne(k)} onClick={(e) => { if ((e as unknown as React.MouseEvent).shiftKey) { e.preventDefault(); selectRange(list, k); } }} aria-label={`Select ${item.name}`} />
                <span aria-hidden="true" />
              </label>
            )}
            <span className={`card-icon ${item.kind}`} style={iconStyle as CSSProperties} aria-hidden="true">
              {item.kind === "folder" ? <FolderIcon size={18} /> : <FileIcon size={18} />}
            </span>
            <button
              className="grow list-open"
              onClick={(e) => {
                if (!selectMode) { openItem(item); return; }
                handleSelectClick(e as unknown as React.MouseEvent, list, item);
              }}
              title={item.name}
              aria-label={`${item.kind === "folder" ? "Open" : "Download"} ${item.name}`}
            >
              <FileNameMarquee name={item.name} isFav={favorites.has(keyFor(item))} />
              <span className="card-meta">
                {item.kind === "folder" ? (
                  showStatus ? <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>Folder <span style={{ opacity: 0.6 }}>·</span> {syncDot(status)}</span> : <span>Folder</span>
                ) : (
                  showStatus ? <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>{formatSize(item.size)} · {item.contentType || kind} <span style={{ opacity: 0.6 }}>·</span> {syncDot(status)}</span>
                  : <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>{formatSize(item.size)} · {item.contentType || kind}</span>
                )}
              </span>
            </button>
            <div className="list-actions" style={{ display: "flex", alignItems: "center", gap: 4 }}>
              {showStatus && (
                <span
                  aria-hidden="true"
                  title={status}
                  style={{
                    width: 8, height: 8, borderRadius: 999, flexShrink: 0, marginRight: 2,
                    background: status === "failed" ? "var(--danger)" : status === "offline" ? "var(--text-faint)" : "var(--warning)",
                    boxShadow: status === "failed" ? "0 0 0 3px var(--danger-soft)" : "0 0 0 3px var(--surface-3)",
                  }}
                />
              )}
              <KebabMenu item={item} />
            </div>
          </div>
        );
      })}
    </div>
  );

  const renderItems = (list: Item[]) => (view === "grid" ? renderGrid(list) : renderList(list));

  const showEmpty = (icon: ReactNode, title: string, hint: string, action?: ReactNode) => (
    <div className="empty" role="status" aria-live="polite">
      <span className="empty-icon" aria-hidden="true">{icon}</span>
      <h4>{title}</h4>
      <p>{hint}</p>
      {action}
    </div>
  );

  const newFolderBtn = (
    <button className="btn btn-outline btn-sm" onClick={() => setPrompt({ mode: "newFolder" })}>
      <FolderPlusIcon size={16} /> New folder
    </button>
  );
  const uploadBtn = (
    <button className="btn btn-outline btn-sm" onClick={() => fileInput.current?.click()}>
      <UploadIcon size={16} /> Upload
    </button>
  );

  const clearSearchBtn = (
    <button type="button" className="btn btn-outline btn-sm" onClick={() => setQuery("")}>
      Clear search
    </button>
  );

  const hasQuery = q.length > 0;
  const noResults = !loading && hasQuery && combined.length === 0;

  // selection bar — only renders when select mode is active (triggered via ⋮ → Select)
  const selectionBar = (ordered: Item[]) => {
    if (!selectMode) return null;
    const count = selected.size;
    const allSelected = ordered.length > 0 && ordered.every((it) => selected.has(keyFor(it)));
    const someSelected = ordered.some((it) => selected.has(keyFor(it)));
    const selectedKeys = Array.from(selected);
    const hasFileSelection = selectedKeys.some((k) => k.startsWith("file:"));
    if (ordered.length === 0) return null;
    return (
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10, flexWrap: "wrap" }}>
        <label style={{ display: "inline-flex", alignItems: "center", gap: 8, fontSize: "0.84rem", fontWeight: 600, color: "var(--text-muted)", cursor: "pointer" }}>
          <input
            type="checkbox"
            checked={allSelected}
            ref={(el) => { if (el) el.indeterminate = !allSelected && someSelected; }}
            onChange={() => {
              if (allSelected) {
                const next = new Set(selected);
                ordered.forEach((it) => next.delete(keyFor(it)));
                setSelected(next);
              } else {
                selectAllVisible(ordered);
              }
            }}
            aria-label="Select all visible"
            style={{ accentColor: "var(--brand)" }}
          />
          Select all
        </label>
        {count > 0 ? (
          <>
            <span className="count-pill" aria-live="polite">{count} selected</span>
            <span style={{ display: "inline-flex", gap: 6, marginLeft: 4 }}>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => void runBulkDownload(selectedKeys)} disabled={!hasFileSelection || busy} title={hasFileSelection ? "Download selected files" : "Select at least one file"}>
                <DownloadIcon size={14} /> Download
              </button>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => { setMovePicker({ keys: selectedKeys }); setMoveTarget(current.id); }} disabled={busy} title="Move selected to folder">
                <MoveIcon size={14} /> Move
              </button>
              <button type="button" className="btn btn-sm" style={{ background: "var(--danger)", color: "#fff", border: "1px solid var(--danger)" }} onClick={() => {
                const names = selectedKeys.slice(0, 3).map((k) => {
                  const id = k.split(":")[1];
                  const m = [...folders, ...files].find((x) => x.id === id);
                  return (m as { name?: string } | undefined)?.name ?? id;
                }).join(", ");
                const label = selectedKeys.length === 1 ? `"${names}"` : `${selectedKeys.length} items${names ? ` (${names}${selectedKeys.length > 3 ? "…" : ""})` : ""}`;
                if (window.confirm(`Delete ${label}? This cannot be undone.`)) void runBulkDelete(selectedKeys);
              }} disabled={busy}>
                <TrashIcon size={14} /> Delete
              </button>
              <button type="button" className="btn btn-ghost btn-sm" onClick={clearSelection}>Clear</button>
            </span>
            <span className="faint" style={{ fontSize: "0.78rem" }}>Tip: Ctrl/Cmd+click toggle · Shift+click range</span>
          </>
        ) : (
          <span className="faint" style={{ fontSize: "0.78rem" }}>Pick items — Ctrl/Cmd toggles, Shift for range</span>
        )}
        <button type="button" className="btn btn-ghost btn-sm" onClick={exitSelectMode} style={{ marginLeft: "auto" }}>Exit selection</button>
      </div>
    );
  };

  return (
    <div className="app">
      <aside className="sidebar" aria-label="Sidebar">
        <div className="brand">
          <span className="logo-badge" aria-hidden="true">
            <CloudIcon size={20} />
          </span>
          <span className="brand-name">NexSync</span>
        </div>
        <nav className="side-nav" aria-label="Primary">
          <button
            className={`nav-item${filterMode === "all" && !isDeviceFiltering ? " active" : ""}`}
            onClick={() => { setFilterMode("all"); setDeviceFilter(null); }}
            aria-pressed={filterMode === "all" && !isDeviceFiltering}
            title="Show all files and folders"
          >
            <FolderIcon size={18} /> All files
          </button>
          <button
            className={`nav-item${filterMode === "fav" ? " active" : ""}`}
            onClick={() => { setFilterMode(filterMode === "fav" ? "all" : "fav"); }}
            aria-pressed={filterMode === "fav"}
            title="Only favorites"
          >
            <StarIcon size={18} /> Favorites {favCount > 0 && <span className="count-pill" style={{ marginLeft: "auto" }}>{favCount}</span>}
          </button>
          <button
            className={`nav-item${filterMode === "recent" ? " active" : ""}`}
            onClick={() => { setFilterMode(filterMode === "recent" ? "all" : "recent"); }}
            aria-pressed={filterMode === "recent"}
            title="Recently opened"
          >
            <HistoryIcon size={18} /> Recent
          </button>
        </nav>
        <button
          type="button"
          className={`device-card-btn${isDeviceFiltering ? " active" : ""}`}
          onClick={() => setDeviceFilter(isDeviceFiltering ? null : device)}
          disabled={!device}
          title={device ? (isDeviceFiltering ? "Show all — clear device filter" : "Show only files from this browser device") : undefined}
          aria-pressed={isDeviceFiltering}
          aria-label={isDeviceFiltering ? "Device filter active, click to clear" : "Filter to this browser device"}
        >
          <div className="device-row">
            <span className="device-dot" aria-hidden="true" />
            <span style={{ fontWeight: 650, fontSize: "0.86rem" }}>
              This browser
            </span>
            {device && (
              <span
                className="count-pill"
                title={device}
                onClick={(e) => { e.stopPropagation(); void copyDeviceId(); }}
                style={{ cursor: "copy", marginLeft: "auto" }}
                aria-label={`Device ID ${device}, click to copy`}
              >
                Active · {device.slice(0, 8)}
              </span>
            )}
          </div>
          <span className="faint" style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
            <DeviceIcon size={14} aria-hidden="true" />
            {device ? (
              <>
                <span title={device}>{device.slice(0, 12)}…</span>
                <button
                  type="button"
                  className="link"
                  style={{ fontSize: "0.78rem", marginLeft: 6 }}
                  onClick={(e) => { e.stopPropagation(); setShowDeviceDetails(true); }}
                  aria-label="Show device details"
                >
                  Details
                </button>
              </>
            ) : (
              "not enrolled"
            )}
          </span>
        </button>
        <div className="sidebar-spacer" />
        <button className="nav-item" onClick={toggleTheme} aria-label={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}>
          {theme === "dark" ? <SunIcon size={18} /> : <MoonIcon size={18} />}
          {theme === "dark" ? "Light mode" : "Dark mode"}
        </button>
        <div className="user-card">
          <span className="avatar" aria-hidden="true">{initials}</span>
          <div className="user-meta">
            <div className="name">{user?.fullName ?? "Signed in"}</div>
            <div className="email" title={user?.email}>{user?.email}</div>
          </div>
          <button className="icon-btn" title="Sign out" aria-label="Sign out" onClick={() => void logout()}>
            <LogoutIcon size={17} />
          </button>
        </div>
      </aside>

      <div className="main">
        <div className="mobile-bar">
          <span className="logo-badge" style={{ width: 32, height: 32, borderRadius: 9 }} aria-hidden="true">
            <CloudIcon size={17} />
          </span>
          <strong style={{ flex: 1 }}>NexSync</strong>
          <button className="icon-btn" aria-label="Toggle theme" onClick={toggleTheme}>
            {theme === "dark" ? <SunIcon size={17} /> : <MoonIcon size={17} />}
          </button>
          <button className="icon-btn" aria-label="Sign out" onClick={() => void logout()}>
            <LogoutIcon size={17} />
          </button>
        </div>

        <header className="topbar">
          <div className="topbar-title">
            <h1>{current.name === "Root" ? "All files" : current.name}</h1>
          </div>
          <button className="btn btn-outline btn-sm" onClick={() => fileInput.current?.click()} aria-label="Upload files">
            <UploadIcon size={16} /> Upload
          </button>
          <button className="btn btn-primary btn-sm" onClick={() => setPrompt({ mode: "newFolder" })} aria-label="Create new folder">
            <FolderPlusIcon size={16} /> New folder
          </button>
          <input
            ref={fileInput}
            type="file"
            multiple
            hidden
            onChange={(e) => {
              void onUpload(e.target.files);
              e.target.value = "";
            }}
            aria-hidden="true"
            tabIndex={-1}
          />
        </header>

        <div
          className="content"
          onDragOver={(e) => { e.preventDefault(); if (!dragOver) setDragOver(true); }}
          onDragEnter={(e) => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={(e) => { const t = e.currentTarget as HTMLElement; if (!t.contains(e.relatedTarget as Node)) setDragOver(false); }}
          onDrop={async (e) => {
            e.preventDefault();
            setDragOver(false);
            const list = e.dataTransfer.files;
            if (!list || list.length === 0) return;
            await onUpload(list);
          }}
          style={dragOver ? { outline: "2px dashed var(--brand)", outlineOffset: -2, borderRadius: "var(--radius-lg)", background: "color-mix(in srgb, var(--brand) 6%, transparent)" } : undefined}
          aria-label="File drop zone"
        >
          {dragOver && (
            <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "0.6rem 0.9rem", borderRadius: "var(--radius)", background: "var(--brand-soft)", border: "1px dashed var(--brand)", color: "var(--brand)", fontWeight: 650, fontSize: "0.88rem", marginBottom: 12 }}>
              <UploadIcon size={16} /> Drop files to upload to “{current.name}”
            </div>
          )}
          <nav className="crumbs" aria-label="Breadcrumb">
            {crumbs.map((c, i) => (
              <span key={c.id ?? "root"} style={{ display: "inline-flex", alignItems: "center" }}>
                {i > 0 && (
                  <span className="crumb-sep" aria-hidden="true">
                    <ChevronRight size={15} />
                  </span>
                )}
                <button
                  className={`crumb${i === crumbs.length - 1 ? " current" : ""}`}
                  onClick={() => goCrumb(i)}
                  disabled={i === crumbs.length - 1}
                  aria-current={i === crumbs.length - 1 ? "page" : undefined}
                  title={i === crumbs.length - 1 ? `Current folder: ${c.name}` : `Go to ${c.name}`}
                >
                  {c.name}
                </button>
              </span>
            ))}
          </nav>

          {/* P0 SyncStatusBar: online/offline, last sync, pending, retry */}
          <div
            role="status"
            aria-live="polite"
            aria-label="Sync status"
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              flexWrap: "wrap",
              padding: "0.65rem 0.9rem",
              borderRadius: "var(--radius)",
              border: `1px solid ${!online ? "color-mix(in srgb, var(--warning) 26%, var(--border))" : loadError ? "color-mix(in srgb, var(--danger) 22%, var(--border))" : "var(--border)"}`,
              background: !online ? "var(--warning-soft)" : loadError ? "var(--danger-soft)" : "var(--surface)",
              marginBottom: "1rem",
              fontSize: "0.85rem",
              boxShadow: "var(--shadow-xs)",
            }}
          >
            <span style={{ display: "inline-flex", alignItems: "center", gap: 8, fontWeight: 600 }}>
              {!online ? (
                <>
                  <span style={{ width: 8, height: 8, borderRadius: 999, background: "var(--warning)", boxShadow: "0 0 0 3px color-mix(in srgb, var(--warning) 18%, transparent)", flexShrink: 0 }} aria-hidden="true" />
                  <AlertIcon size={15} aria-hidden="true" />
                  Offline
                </>
              ) : loadError ? (
                <>
                  <span style={{ width: 8, height: 8, borderRadius: 999, background: "var(--danger)", boxShadow: "0 0 0 3px var(--danger-soft)", flexShrink: 0 }} aria-hidden="true" />
                  <AlertIcon size={15} aria-hidden="true" />
                  Sync failed
                </>
              ) : loading ? (
                <>
                  <span className="spinner" style={{ width: 14, height: 14, borderWidth: 2 }} aria-hidden="true" />
                  Syncing…
                </>
              ) : pendingCount > 0 ? (
                <>
                  <span style={{ width: 8, height: 8, borderRadius: 999, background: "var(--warning)", boxShadow: "0 0 0 3px var(--warning-soft)", flexShrink: 0 }} aria-hidden="true" />
                  {pendingCount} pending
                </>
              ) : (
                <>
                  <span style={{ width: 8, height: 8, borderRadius: 999, background: "var(--success)", boxShadow: "0 0 0 3px var(--success-soft)", flexShrink: 0 }} aria-hidden="true" />
                  <CheckIcon size={15} aria-hidden="true" />
                  All synced
                </>
              )}
            </span>
            <span className="faint" style={{ fontSize: "0.82rem" }}>
              {!online ? "— changes will sync when back online" : loadError ? `— ${loadError}` : `· Last sync ${formatRelativeSync(lastSyncAt)}`}
            </span>
            <span style={{ marginLeft: "auto", display: "inline-flex", gap: 8, alignItems: "center" }}>
              <span
                title={online ? "Online" : "Offline"}
                aria-label={online ? "Online" : "Offline"}
                style={{
                  width: 8,
                  height: 8,
                  borderRadius: 999,
                  background: online ? "var(--success)" : "var(--text-faint)",
                  boxShadow: online ? "0 0 0 3px var(--success-soft)" : "0 0 0 3px var(--surface-3)",
                  display: "inline-block",
                  flexShrink: 0,
                }}
              />
              <span className="faint" style={{ fontSize: "0.78rem" }}>{online ? "Online" : "Offline"}</span>
              <button type="button" className="btn btn-ghost btn-sm" onClick={() => void load()} aria-label="Retry sync" style={{ padding: "0.3rem 0.6rem" }}>
                Retry
              </button>
            </span>
          </div>

          <div className="toolbar">
            <div className="search grow">
              <SearchIcon size={16} aria-hidden="true" />
              <input
                className="input"
                placeholder="Search in this folder…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search in this folder"
                type="search"
              />
            </div>
            <SortMenu sort={sort} onChange={setSort} />
            <div className="segmented" role="group" aria-label="View mode">
              <button
                className={view === "grid" ? "active" : ""}
                onClick={() => setView("grid")}
                aria-label="Grid view"
                aria-pressed={view === "grid"}
                title="Grid view"
              >
                <GridIcon size={17} />
              </button>
              <button
                className={view === "list" ? "active" : ""}
                onClick={() => setView("list")}
                aria-label="List view"
                aria-pressed={view === "list"}
                title="List view"
              >
                <ListIcon size={17} />
              </button>
            </div>
          </div>

          {(isDeviceFiltering || filterMode !== "all") && !loading && (
            <div className="filter-banner" role="status" aria-live="polite">
              {filterMode === "fav" ? <StarIcon size={16} aria-hidden="true" /> : filterMode === "recent" ? <HistoryIcon size={16} aria-hidden="true" /> : <DeviceIcon size={16} aria-hidden="true" />}
              <span>
                {filterMode === "fav" ? <>Favorites <strong>— {combined.length} shown</strong></>
                  : filterMode === "recent" ? <>Recent <strong>— {combined.length} shown</strong></>
                  : <>Only items from this device<strong> — {device!.slice(0, 8)}…</strong></>}
              </span>
              <span className="count-pill" aria-label={`${sortedFolders.length + sortedFiles.length} items shown`}>
                {sortedFolders.length + sortedFiles.length} shown
              </span>
              <button
                type="button"
                className="btn btn-sm btn-outline"
                onClick={() => { setDeviceFilter(null); setFilterMode("all"); }}
                aria-label="Clear filter"
              >
                Show all
              </button>
            </div>
          )}

          {uploadPct !== null && (
            <div className="upload-banner" role="status" aria-live="polite" aria-label={`Uploading ${uploadPct} percent`}>
              <UploadIcon size={18} aria-hidden="true" />
              <span style={{ fontSize: "0.88rem", fontWeight: 600 }}>Uploading…</span>
              <div className="progress" aria-hidden="true">
                <span style={{ width: `${uploadPct}%` }} />
              </div>
              <span className="faint">{uploadPct}%</span>
            </div>
          )}

          {loading ? (
            <div className="skeleton-grid" aria-busy="true" aria-label="Loading files">
              {Array.from({ length: 6 }).map((_, i) => (
                <div className="skeleton" key={i} />
              ))}
            </div>
          ) : noResults ? (
            showEmpty(
              <SearchIcon size={28} />,
              `No matches for "${query.trim()}"`,
              `No files or folders match your search in "${current.name}". Try a different term or clear the search.`,
              clearSearchBtn,
            )
          ) : sort.foldersFirst ? (
            <>
              <section aria-label="Folders">
                <div className="section-head">
                  <h3>Folders</h3>
                  <span className="count-pill" aria-label={`${sortedFolders.length} folders`}>{sortedFolders.length}</span>
                </div>
                {sortedFolders.length === 0
                  ? showEmpty(
                      <FolderIcon size={28} />,
                      hasQuery ? `No matching folders for "${query.trim()}"` : "No folders yet",
                      hasQuery ? "Try a different search term or clear the filter." : "Create a folder to organise your files — it will sync to all your devices.",
                      hasQuery ? clearSearchBtn : newFolderBtn,
                    )
                  : (
                    <>
                      {selectionBar(sortedFolders)}
                      {renderItems(sortedFolders)}
                    </>
                  )}
              </section>
              <section aria-label="Files">
                <div className="section-head">
                  <h3>Files</h3>
                  <span className="count-pill" aria-label={`${sortedFiles.length} files`}>{sortedFiles.length}</span>
                </div>
                {sortedFiles.length === 0
                  ? showEmpty(
                      <FileIcon size={28} />,
                      hasQuery ? `No matching files for "${query.trim()}"` : "No files yet",
                      hasQuery ? "Try a different search term or clear the filter." : "Upload files to see them sync everywhere. Drag and drop also works.",
                      hasQuery ? clearSearchBtn : uploadBtn,
                    )
                  : (
                    <>
                      {selectionBar(sortedFiles)}
                      {renderItems(sortedFiles)}
                    </>
                  )}
              </section>
            </>
          ) : (
            <section aria-label="All items">
              <div className="section-head">
                <h3>All items</h3>
                <span className="count-pill">{combined.length}</span>
              </div>
              {combined.length === 0
                ? showEmpty(
                    <SearchIcon size={28} />,
                    hasQuery ? `No matches for "${query.trim()}"` : "This folder is empty",
                    hasQuery ? "Try a different search term or clear the search." : "Create a folder or upload files to get started — everything syncs automatically.",
                    hasQuery ? clearSearchBtn : newFolderBtn,
                  )
                : (
                  <>
                    {selectionBar(combined)}
                    {renderItems(combined)}
                  </>
                )}
            </section>
          )}
        </div>
      </div>

      <PromptDialog
        open={prompt !== null}
        title={promptConfig.title}
        description={promptConfig.description}
        initialValue={prompt?.initial ?? ""}
        placeholder={promptConfig.placeholder}
        confirmLabel={promptConfig.confirmLabel}
        busy={busy}
        onCancel={() => setPrompt(null)}
        onConfirm={(v) => void submitPrompt(v)}
      />

      <ConfirmDialog
        open={confirm !== null}
        title={confirm?.kind === "folder" ? "Delete folder?" : "Delete file?"}
        danger
        confirmLabel="Delete"
        busy={busy}
        description={
          confirm?.kind === "folder" ? (
            <>
              This permanently removes <strong>{confirm.name}</strong> and everything inside it on
              every device.
            </>
          ) : (
            <>
              This permanently removes <strong>{confirm?.name}</strong> on every device.
            </>
          )
        }
        onCancel={() => setConfirm(null)}
        onConfirm={() => void runConfirm()}
      />

      {/* Move picker — choose destination folder (uses existing updateFolder/updateFile) */}
      {movePicker && (
        <div className="modal-overlay" role="dialog" aria-modal="true" aria-label="Move items" onMouseDown={() => setMovePicker(null)}>
          <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ maxWidth: 440 }}>
            <div className="modal-head">
              <div className="modal-ico info" aria-hidden="true"><MoveIcon size={20} /></div>
              <div>
                <h2>Move {movePicker.keys.length} item{movePicker.keys.length > 1 ? "s" : ""}</h2>
                <p className="modal-desc">Choose destination. Uses rename with new parent — same API as drag.</p>
              </div>
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 6, maxHeight: 260, overflow: "auto" }}>
              <button
                type="button"
                className="menu-row"
                style={{ background: moveTarget === null ? "var(--brand-soft)" : undefined, border: moveTarget === null ? "1px solid var(--brand)" : "1px solid var(--border)", borderRadius: "var(--radius-sm)" }}
                onClick={() => setMoveTarget(null)}
              >
                <span className="menu-ico"><FolderIcon size={16} /></span>
                <span className="menu-text">Root</span>
                {moveTarget === null && <CheckIcon size={14} />}
              </button>
              {folders
                .filter((f) => !movePicker.keys.includes(`folder:${f.id}`))
                .map((f) => (
                  <button
                    key={f.id}
                    type="button"
                    className="menu-row"
                    style={{ background: moveTarget === f.id ? "var(--brand-soft)" : undefined, border: moveTarget === f.id ? "1px solid var(--brand)" : "1px solid var(--border)", borderRadius: "var(--radius-sm)" }}
                    onClick={() => setMoveTarget(f.id)}
                  >
                    <span className="menu-ico"><FolderIcon size={16} /></span>
                    <span className="menu-text">{f.name}</span>
                    {moveTarget === f.id && <CheckIcon size={14} />}
                  </button>
                ))}
            </div>
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setMovePicker(null)}>Cancel</button>
              <button className="btn btn-primary" disabled={busy} onClick={() => void confirmMove()}>Move</button>
            </div>
          </div>
        </div>
      )}

      {/* Details */}
      {infoItem && (
        <div className="modal-overlay" role="dialog" aria-modal="true" aria-label="Details" onMouseDown={() => setInfoItem(null)}>
          <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ maxWidth: 460 }}>
            <div className="modal-head">
              <div className="modal-ico info" aria-hidden="true"><InfoIcon size={20} /></div>
              <div style={{ minWidth: 0 }}>
                <h2 style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{infoItem.name}</h2>
                <p className="modal-desc">{infoItem.kind === "folder" ? "Folder" : `${(infoItem as FileItem).contentType || "File"} · ${formatSize((infoItem as FileItem).size)}`}</p>
              </div>
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 8, fontSize: "0.88rem" }}>
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><span className="faint">Type</span><span>{infoItem.kind === "folder" ? "Folder" : (infoItem as FileItem).contentType || "—"}</span></div>
              {infoItem.kind === "file" && <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><span className="faint">Size</span><span>{formatSize((infoItem as FileItem).size)}</span></div>}
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><span className="faint">Modified</span><span>{new Date(infoItem.updatedAt).toLocaleString()}</span></div>
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><span className="faint">Created</span><span>{new Date(infoItem.createdAt).toLocaleString()}</span></div>
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><span className="faint">Location</span><span>{current.name}</span></div>
              {favorites.has(keyFor(infoItem)) && <div style={{ display: "inline-flex", alignItems: "center", gap: 6, color: "var(--warning)", fontWeight: 600 }}><StarFilledIcon size={14} /> Favorite</div>}
            </div>
            <div className="modal-actions">
              {infoItem.kind === "file" && <button className="btn btn-outline btn-sm" onClick={() => { const it = infoItem as FileItem; setInfoItem(null); void openPreview(it); }}><EyeIcon size={14} /> Preview</button>}
              <button className="btn btn-ghost" onClick={() => setInfoItem(null)}>Close</button>
            </div>
          </div>
        </div>
      )}

      {/* Preview */}
      {preview && (
        <div className="modal-overlay" role="dialog" aria-modal="true" aria-label={`Preview ${preview.name}`} onMouseDown={() => { setPreview(null); setPreviewUrl(null); }}>
          <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ maxWidth: 760, width: "min(92vw, 760px)" }}>
            <div className="modal-head">
              <div className="modal-ico info" aria-hidden="true"><EyeIcon size={20} /></div>
              <div style={{ minWidth: 0, flex: 1 }}>
                <h2 style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{preview.name}</h2>
                <p className="modal-desc">{preview.contentType || "File"} · {formatSize(preview.size)}</p>
              </div>
              <button className="icon-btn" aria-label="Close preview" onClick={() => { setPreview(null); setPreviewUrl(null); }}><XIcon size={16} /></button>
            </div>
            <div style={{ maxHeight: "62vh", overflow: "auto", borderRadius: "var(--radius)", border: "1px solid var(--border)", background: "var(--surface-2)", display: "grid", placeItems: "center", minHeight: 160 }}>
              {!previewUrl ? (
                <span style={{ padding: 24, display: "inline-flex", alignItems: "center", gap: 8 }}><span className="spinner" style={{ width: 18, height: 18 }} /> Loading…</span>
              ) : preview.contentType.startsWith("image/") ? (
                <img src={previewUrl} alt={preview.name} style={{ maxWidth: "100%", maxHeight: "60vh", objectFit: "contain", display: "block" }} />
              ) : preview.contentType.includes("pdf") ? (
                <iframe src={previewUrl} title={preview.name} style={{ width: "100%", height: "55vh", border: "none", background: "#fff" }} />
              ) : (
                <iframe src={previewUrl} title={preview.name} style={{ width: "100%", height: "48vh", border: "none", background: "var(--surface)" }} />
              )}
            </div>
            <div className="modal-actions">
              <button className="btn btn-outline btn-sm" onClick={() => { const p = preview; setPreview(null); setPreviewUrl(null); void onDownload(p); }}><DownloadIcon size={14} /> Download</button>
              <button className="btn btn-ghost" onClick={() => { setPreview(null); setPreviewUrl(null); }}>Close</button>
            </div>
          </div>
        </div>
      )}

      {/* Device details — friendly display with copy affordance, not raw UUID slice alone */}
      {showDeviceDetails && device && (
        <div className="modal-overlay" role="dialog" aria-modal="true" aria-label="Device details" onMouseDown={() => setShowDeviceDetails(false)}>
          <div className="modal" onMouseDown={(e) => e.stopPropagation()} style={{ maxWidth: 460 }}>
            <div className="modal-head">
              <div className="modal-ico info" aria-hidden="true"><DeviceIcon size={20} /></div>
              <div>
                <h2>This browser</h2>
                <p className="modal-desc">Active device for this browser. Files created here carry this ID.</p>
              </div>
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
                <span className="count-pill" style={{ background: "var(--success-soft)", color: "var(--success)", border: "1px solid color-mix(in srgb, var(--success) 18%, transparent)" }}>Active</span>
                <code style={{ fontFamily: "var(--font-mono)", fontSize: "0.82rem", background: "var(--surface-3)", padding: "4px 8px", borderRadius: "var(--radius-xs)", wordBreak: "break-all" }}>{device}</code>
              </div>
              <p className="faint" style={{ fontSize: "0.82rem" }}>Short ID: <strong style={{ color: "var(--text)" }}>{device.slice(0, 8)}</strong> · Full ID shown above</p>
            </div>
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setShowDeviceDetails(false)}>Close</button>
              <button className="btn btn-primary" onClick={() => void copyDeviceId()}>Copy ID</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function promptConfigFor(prompt: PromptState | null, location: string) {
  switch (prompt?.mode) {
    case "newFolder":
      return {
        title: "New folder",
        description: `Created in “${location}”.`,
        placeholder: "Folder name",
        confirmLabel: "Create",
      };
    case "renameFolder":
      return {
        title: "Rename folder",
        description: "Update the folder name across all devices.",
        placeholder: "Folder name",
        confirmLabel: "Rename",
      };
    case "renameFile":
      return {
        title: "Rename file",
        description: "Update the file name across all devices.",
        placeholder: "File name",
        confirmLabel: "Rename",
      };
    default:
      return { title: "", description: "", placeholder: "", confirmLabel: "Save" };
  }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function describeError(e: unknown): string {
  if (typeof e === "object" && e !== null && "response" in e) {
    const resp = (e as { response?: { status?: number; data?: { detail?: string; message?: string; code?: string } } }).response;
    if (resp?.status === 403) return "Access denied — this device may have been revoked. Please sign in again.";
    return resp?.data?.detail ?? resp?.data?.message ?? `Request failed (${resp?.status}).`;
  }
  return "Request failed.";
}
