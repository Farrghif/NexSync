import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
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
  SearchIcon, SunIcon, TrashIcon, UploadIcon,
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
}

type Item = FolderItem | FileItem;

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
  const [uploadPct, setUploadPct] = useState<number | null>(null);
  const [prompt, setPrompt] = useState<PromptState | null>(null);
  const [confirm, setConfirm] = useState<ConfirmState | null>(null);
  const [busy, setBusy] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const current = crumbs[crumbs.length - 1];

  useEffect(() => {
    localStorage.setItem(SORT_KEY, JSON.stringify(sort));
  }, [sort]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getContents(current.id);
      setFolders(data.folders);
      setFiles(data.files);
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setLoading(false);
    }
  }, [current.id, toast]);

  useEffect(() => {
    void load();
  }, [load]);

  const enterFolder = (item: FolderItem) => {
    setQuery("");
    setCrumbs((c) => [...c, { id: item.id, name: item.name }]);
  };
  const goCrumb = (i: number) => {
    setQuery("");
    setCrumbs((c) => c.slice(0, i + 1));
  };

  const q = query.trim().toLowerCase();

  const items = useMemo<Item[]>(() => {
    const folderItems: FolderItem[] = folders
      .filter((f) => !q || f.name.toLowerCase().includes(q))
      .map((f) => ({
        kind: "folder",
        id: f.id,
        name: f.name,
        createdAt: f.createdAt,
        updatedAt: f.updatedAt,
        parentFolderId: f.parentFolderId,
      }));
    const fileItems: FileItem[] = files
      .filter((f) => !q || f.name.toLowerCase().includes(q))
      .map((f) => ({
        kind: "file",
        id: f.id,
        name: f.name,
        createdAt: f.createdAt,
        updatedAt: f.updatedAt,
        folderId: f.folderId,
        size: f.size,
        contentType: f.contentType,
      }));
    return [...folderItems, ...fileItems];
  }, [folders, files, q]);

  const comparator = useCallback(
    (a: Item, b: Item) => compareItems(a, b, sort.key, sort.dir),
    [sort.key, sort.dir],
  );

  const sortedFolders = useMemo(
    () => items.filter((i): i is FolderItem => i.kind === "folder").slice().sort(comparator),
    [items, comparator],
  );
  const sortedFiles = useMemo(
    () => items.filter((i): i is FileItem => i.kind === "file").slice().sort(comparator),
    [items, comparator],
  );
  const combined = useMemo(() => items.slice().sort(comparator), [items, comparator]);

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
      await load();
    } catch (e) {
      toast.error(describeError(e));
    } finally {
      setBusy(false);
    }
  };

  const onDownload = async (item: { id: string; name: string }) => {
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
  };

  const openItem = (item: Item) => {
    if (item.kind === "folder") enterFolder(item);
    else void onDownload(item);
  };

  const renameItem = (item: Item) => {
    if (item.kind === "folder") {
      setPrompt({ mode: "renameFolder", targetId: item.id, parentId: item.parentFolderId, initial: item.name });
    } else {
      setPrompt({ mode: "renameFile", targetId: item.id, parentId: item.folderId, initial: item.name });
    }
  };

  const askDelete = (item: Item) => setConfirm({ kind: item.kind, id: item.id, name: item.name });

  const device = currentDeviceId();
  const initials = (user?.fullName ?? user?.email ?? "?")
    .split(/\s+/)
    .map((s) => s[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  const promptConfig = promptConfigFor(prompt, current.name);

  const renderGrid = (list: Item[]) => (
    <div className="grid">
      {list.map((item) => (
        <div className="card" key={`${item.kind}:${item.id}`}>
          <div className="card-actions">
            {item.kind === "file" && (
              <button className="icon-btn" title="Download" aria-label={`Download ${item.name}`} onClick={() => void onDownload(item)}>
                <DownloadIcon size={15} />
              </button>
            )}
            <button className="icon-btn" title="Rename" aria-label={`Rename ${item.name}`} onClick={() => renameItem(item)}>
              <PencilIcon size={15} />
            </button>
            <button className="icon-btn danger" title="Delete" aria-label={`Delete ${item.name}`} onClick={() => askDelete(item)}>
              <TrashIcon size={15} />
            </button>
          </div>
          <div
            className="card-main"
            role="button"
            tabIndex={0}
            onClick={() => openItem(item)}
            onKeyDown={(e) => e.key === "Enter" && openItem(item)}
          >
            <span className={`card-icon ${item.kind}`}>
              {item.kind === "folder" ? <FolderIcon size={21} /> : <FileIcon size={21} />}
            </span>
            <div style={{ minWidth: 0 }}>
              <div className="card-name">{item.name}</div>
              <div className="card-meta">{item.kind === "folder" ? "Folder" : formatSize(item.size)}</div>
            </div>
          </div>
        </div>
      ))}
    </div>
  );

  const renderList = (list: Item[]) => (
    <div className="list">
      {list.map((item) => (
        <div className="list-row" key={`${item.kind}:${item.id}`}>
          <span className={`card-icon ${item.kind}`}>
            {item.kind === "folder" ? <FolderIcon size={18} /> : <FileIcon size={18} />}
          </span>
          <button className="grow list-open" onClick={() => openItem(item)}>
            <span className="card-name">{item.name}</span>
            <span className="card-meta">
              {item.kind === "folder" ? "Folder" : `${formatSize(item.size)} · ${item.contentType}`}
            </span>
          </button>
          <div className="list-actions">
            {item.kind === "file" && (
              <button className="icon-btn" title="Download" aria-label={`Download ${item.name}`} onClick={() => void onDownload(item)}>
                <DownloadIcon size={15} />
              </button>
            )}
            <button className="icon-btn" title="Rename" aria-label={`Rename ${item.name}`} onClick={() => renameItem(item)}>
              <PencilIcon size={15} />
            </button>
            <button className="icon-btn danger" title="Delete" aria-label={`Delete ${item.name}`} onClick={() => askDelete(item)}>
              <TrashIcon size={15} />
            </button>
          </div>
        </div>
      ))}
    </div>
  );

  const renderItems = (list: Item[]) => (view === "grid" ? renderGrid(list) : renderList(list));

  const showEmpty = (icon: ReactNode, title: string, hint: string, action?: ReactNode) => (
    <div className="empty">
      <span className="empty-icon">{icon}</span>
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

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">
          <span className="logo-badge">
            <CloudIcon size={20} />
          </span>
          <span className="brand-name">NexSync</span>
        </div>
        <nav className="side-nav">
          <button className="nav-item active">
            <FolderIcon size={18} /> Files
          </button>
        </nav>
        <div className="device-card">
          <div className="device-row">
            <span className="device-dot" />
            <span style={{ fontWeight: 650, fontSize: "0.86rem" }}>Browser device</span>
          </div>
          <span className="faint" style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <DeviceIcon size={14} /> {device ? `${device.slice(0, 12)}…` : "not enrolled"}
          </span>
        </div>
        <div className="sidebar-spacer" />
        <button className="nav-item" onClick={toggleTheme}>
          {theme === "dark" ? <SunIcon size={18} /> : <MoonIcon size={18} />}
          {theme === "dark" ? "Light mode" : "Dark mode"}
        </button>
        <div className="user-card">
          <span className="avatar">{initials}</span>
          <div className="user-meta">
            <div className="name">{user?.fullName ?? "Signed in"}</div>
            <div className="email">{user?.email}</div>
          </div>
          <button className="icon-btn" title="Sign out" aria-label="Sign out" onClick={() => void logout()}>
            <LogoutIcon size={17} />
          </button>
        </div>
      </aside>

      <div className="main">
        <div className="mobile-bar">
          <span className="logo-badge" style={{ width: 32, height: 32, borderRadius: 9 }}>
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
          <button className="btn btn-outline btn-sm" onClick={() => fileInput.current?.click()}>
            <UploadIcon size={16} /> Upload
          </button>
          <button className="btn btn-primary btn-sm" onClick={() => setPrompt({ mode: "newFolder" })}>
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
          />
        </header>

        <div className="content">
          <nav className="crumbs" aria-label="Breadcrumb">
            {crumbs.map((c, i) => (
              <span key={c.id ?? "root"} style={{ display: "inline-flex", alignItems: "center" }}>
                {i > 0 && (
                  <span className="crumb-sep">
                    <ChevronRight size={15} />
                  </span>
                )}
                <button
                  className={`crumb${i === crumbs.length - 1 ? " current" : ""}`}
                  onClick={() => goCrumb(i)}
                  disabled={i === crumbs.length - 1}
                >
                  {c.name}
                </button>
              </span>
            ))}
          </nav>

          <div className="toolbar">
            <div className="search grow">
              <SearchIcon size={16} />
              <input
                className="input"
                placeholder="Search in this folder…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search"
              />
            </div>
            <SortMenu sort={sort} onChange={setSort} />
            <div className="segmented" role="group" aria-label="View mode">
              <button
                className={view === "grid" ? "active" : ""}
                onClick={() => setView("grid")}
                aria-label="Grid view"
                title="Grid view"
              >
                <GridIcon size={17} />
              </button>
              <button
                className={view === "list" ? "active" : ""}
                onClick={() => setView("list")}
                aria-label="List view"
                title="List view"
              >
                <ListIcon size={17} />
              </button>
            </div>
          </div>

          {uploadPct !== null && (
            <div className="upload-banner">
              <UploadIcon size={18} />
              <span style={{ fontSize: "0.88rem", fontWeight: 600 }}>Uploading…</span>
              <div className="progress">
                <span style={{ width: `${uploadPct}%` }} />
              </div>
              <span className="faint">{uploadPct}%</span>
            </div>
          )}

          {loading ? (
            <div className="skeleton-grid">
              {Array.from({ length: 6 }).map((_, i) => (
                <div className="skeleton" key={i} />
              ))}
            </div>
          ) : sort.foldersFirst ? (
            <>
              <section>
                <div className="section-head">
                  <h3>Folders</h3>
                  <span className="count-pill">{sortedFolders.length}</span>
                </div>
                {sortedFolders.length === 0
                  ? showEmpty(
                      <FolderIcon size={28} />,
                      q ? "No matching folders" : "No folders yet",
                      q ? "Try a different search term." : "Create a folder to organise your files.",
                      !q ? newFolderBtn : undefined,
                    )
                  : renderItems(sortedFolders)}
              </section>
              <section>
                <div className="section-head">
                  <h3>Files</h3>
                  <span className="count-pill">{sortedFiles.length}</span>
                </div>
                {sortedFiles.length === 0
                  ? showEmpty(
                      <FileIcon size={28} />,
                      q ? "No matching files" : "No files yet",
                      q ? "Try a different search term." : "Upload files to see them sync everywhere.",
                      !q ? uploadBtn : undefined,
                    )
                  : renderItems(sortedFiles)}
              </section>
            </>
          ) : (
            <section>
              <div className="section-head">
                <h3>All items</h3>
                <span className="count-pill">{combined.length}</span>
              </div>
              {combined.length === 0
                ? showEmpty(
                    <SearchIcon size={28} />,
                    q ? "No matches" : "This folder is empty",
                    q ? "Try a different search term." : "Create a folder or upload files to get started.",
                    !q ? newFolderBtn : undefined,
                  )
                : renderItems(combined)}
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
